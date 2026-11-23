using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DrivingSchool.Core;
using DrivingSchool.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DrivingSchool.Tests;

public sealed partial class ApiTests
{
    private static readonly string TestPassword = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task UnreadableStoredHashRejectsLoginWithoutServerError()
    {
        using var server = new DatabaseFactory();
        using var client = server.CreateClient();
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDb>();
        var user = await db.Users.FindAsync(2);
        user!.PasswordHash = "invalid-hash";
        await db.SaveChangesAsync();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginInput("manager", TestPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("invalid-hash", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AccessIsEnforcedForAllRolesAndLogoutRevokesToken()
    {
        using var server = new DatabaseFactory();
        using var anonymous = server.CreateClient();
        using var admin = await SignIn(server, "admin", TestPassword, Roles.Admin);
        using var manager = await SignIn(server, "manager", TestPassword, Roles.Manager);
        using var first = await SignIn(server, "nikita", TestPassword, Roles.Instructor);
        using var second = await SignIn(server, "sergey", TestPassword, Roles.Instructor);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/lessons")).StatusCode);
        Assert.Equal(4, (await Get<UserInfo[]>(admin, "/api/users")).Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/students")).StatusCode);
        Assert.Equal(2, (await Get<UserInfo[]>(manager, "/api/users/instructors")).Length);

        int student = await CreateStudent(manager);
        int lesson1 = await CreateLesson(manager, student, 3, "Парковка");
        int lesson2 = await CreateLesson(admin, student, 4, "Учебный маршрут");

        Assert.Equal(2, (await Get<LessonInfo[]>(admin, "/api/lessons")).Length);
        Assert.Equal(lesson1, Assert.Single(await Get<LessonInfo[]>(first, "/api/lessons")).Id);
        Assert.Equal(lesson2, Assert.Single(await Get<LessonInfo[]>(second, "/api/lessons")).Id);
        Assert.Equal(1, (await Get<SchoolStatistics>(first, "/api/lessons/statistics")).Total);
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/lessons/{lesson2}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await Post(first, $"/api/lessons/{lesson2}/transition",
            new TransitionInput(LessonStatus.Confirmed, null, 1)));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(first, "/api/lessons", new { }));
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync("/api/lessons/export/json")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await Post(first, $"/api/lessons/{lesson1}/transition",
            new TransitionInput(LessonStatus.Confirmed, null, 1)));

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/lessons")).StatusCode);
    }

    [Fact]
    public async Task LessonAdvancesWithValidationAndRejectsStaleEdits()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", TestPassword, Roles.Manager);
        int student = await CreateStudent(manager);
        int lesson = await CreateLesson(manager, student, null, "Учебный маршрут");
        var path = $"/api/lessons/{lesson}";
        var state = await Get<LessonInfo>(manager, path);
        Assert.Equal(LessonStatus.Planned, state.Status);
        Assert.Equal(HttpStatusCode.BadRequest, await Step(LessonStatus.Completed, 1));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(LessonStatus.Confirmed, 1));
        Assert.Equal(HttpStatusCode.NoContent, await Edit(1, 3, null, state.CreatedAt));
        Assert.Equal(HttpStatusCode.Conflict, await Step(LessonStatus.Confirmed, 1));
        Assert.Equal(HttpStatusCode.NoContent, await Step(LessonStatus.Confirmed, 2));
        Assert.Equal(HttpStatusCode.BadRequest, await Edit(3, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(LessonStatus.Completed, 3));
        Assert.Equal(HttpStatusCode.NoContent, await Edit(3, 3, 1800, state.CreatedAt));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(LessonStatus.Completed, 4));
        Assert.Equal(HttpStatusCode.NoContent, await Step(LessonStatus.Completed, 4, finishedAt: DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(LessonStatus.Rescheduled, 5, "Передумал"));
        var statistics = await Get<SchoolStatistics>(manager, "/api/lessons/statistics");
        Assert.Equal(1800, statistics.CompletedAmount);
        Assert.NotNull(statistics.AverageDays);

        int rescheduled = await CreateLesson(manager, student, 4, "Разворот");
        Assert.Equal(HttpStatusCode.BadRequest, await Post(manager, $"/api/lessons/{rescheduled}/transition",
            new TransitionInput(LessonStatus.Rescheduled, " ", 1)));
        Assert.Equal(HttpStatusCode.NoContent, await Post(manager, $"/api/lessons/{rescheduled}/transition",
            new TransitionInput(LessonStatus.Rescheduled, "Отказ", 1)));
        Assert.Equal("Отказ", (await Get<LessonInfo>(manager, $"/api/lessons/{rescheduled}")).ClosingReason);

        Task<HttpStatusCode> Step(LessonStatus target, int version, string? reason = null, DateTimeOffset? finishedAt = null) =>
            Post(manager, path + "/transition", new TransitionInput(target, reason, version, finishedAt));

        async Task<HttpStatusCode> Edit(int version, int? instructor, decimal? amount, DateTimeOffset? date)
        {
            using var response = await manager.PutAsJsonAsync(path,
                new LessonInput(student, "Учебный маршрут", "Ул. Парковая", null, instructor, amount, date, version),
                SchoolFiles.JsonOptions);
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task ImportsAtomicallyAndExportsFilteredLessons()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", TestPassword, Roles.Manager);
        using var wrong = new StringContent("Name;Phone;Email\nУченик;123;\nОшибка;;", Encoding.UTF8, "text/csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/students/import", wrong)).StatusCode);
        Assert.Empty(await Get<StudentInfo[]>(manager, "/api/students"));

        using var csv = new StringContent("Name;Phone;Email\n\"Орлова, Анна\";123;anna@example.com\nПетров;456;",
            Encoding.UTF8, "text/csv");
        using var imported = await manager.PostAsync("/api/students/import", csv);
        Assert.Equal(2, (await imported.Content.ReadFromJsonAsync<ImportResult>())!.Imported);

        var students = await Get<StudentInfo[]>(manager, "/api/students");
        int exportId = await CreateLesson(manager, students[0].Id, 3, "Городской маршрут");
        await CreateLesson(manager, students[1].Id, 4, "Парковка");
        var query = "?status=Planned&q=" + Uri.EscapeDataString("городской");
        Assert.Equal(exportId, Assert.Single(await Get<LessonInfo[]>(manager, "/api/lessons" + query)).Id);

        foreach (string format in new[] { "csv", "json", "xml" })
        {
            using var response = await manager.GetAsync($"/api/lessons/export/{format}{query}");
            response.EnsureSuccessStatusCode();
            Assert.Equal($"lessons.{format}", response.Content.Headers.ContentDisposition!.FileName);
            var document = await response.Content.ReadAsStringAsync();
            Assert.Contains("Городской маршрут", format == "json"
                ? Assert.Single(System.Text.Json.JsonSerializer.Deserialize<LessonInfo[]>(document, SchoolFiles.JsonOptions)!).Title
                : document);
            Assert.DoesNotContain("Парковка", document);
        }
    }

    private static async Task<HttpClient> SignIn(DatabaseFactory server, string login, string password, string role)
    {
        var http = server.CreateClient();
        using var response = await http.PostAsJsonAsync("/api/auth/login", new LoginInput(login, password));
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<LoginResult>())!;
        Assert.Equal(role, session.User.Role);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return http;
    }

    private static async Task<int> CreateStudent(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/students", new StudentInput("Ученик", "123", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<int> CreateLesson(HttpClient client, int student, int? instructor, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/lessons",
            new LessonInput(student, title, "Ул. Парковая", null, instructor, null, DateTimeOffset.UtcNow));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<T> Get<T>(HttpClient http, string url) =>
        (await http.GetFromJsonAsync<T>(url, SchoolFiles.JsonOptions))!;

    private static async Task<HttpStatusCode> Post(HttpClient http, string url, object value)
    {
        using var result = await http.PostAsJsonAsync(url, value, SchoolFiles.JsonOptions);
        return result.StatusCode;
    }

    private sealed record Created(int Id);

    private sealed class DatabaseFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "DrivingSchool.Tests", Guid.NewGuid().ToString("N"));

        protected override IHost CreateHost(IHostBuilder builder)
        {
            const string setting = "School__Database";
            string? previous = Environment.GetEnvironmentVariable(setting);
            try
            {
                Environment.SetEnvironmentVariable(setting, Path.Combine(directory, "integration.db"));
                var host = base.CreateHost(builder);
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SchoolDb>();
                var salt = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
                var hash = salt + ":" + SchoolDatabase.Hash(TestPassword, salt);
                foreach (var user in db.Users)
                    user.PasswordHash = hash;
                db.SaveChanges();
                return host;
            }
            finally
            {
                Environment.SetEnvironmentVariable(setting, previous);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory))
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
