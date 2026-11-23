using System.Net;
using System.Net.Http.Json;
using DrivingSchool.Core;
using DrivingSchool.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DrivingSchool.Tests;

public sealed partial class ApiTests
{
    [Theory]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/api/lessons", HttpStatusCode.Unauthorized)]
    [InlineData("/api/lessons/statistics", HttpStatusCode.Unauthorized)]
    [InlineData("/api/lessons/export/xml", HttpStatusCode.Unauthorized)]
    [InlineData("/api/students", HttpStatusCode.Unauthorized)]
    [InlineData("/api/users", HttpStatusCode.Unauthorized)]
    [InlineData("/api/debug/requests", HttpStatusCode.Unauthorized)]
    public async Task AnonymousHealthIsPublicAndBusinessDataIsProtected(string path, HttpStatusCode expected)
    {
        using var server = new DatabaseFactory();
        using var http = server.CreateClient();
        using var response = await http.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task LoginIsRateLimitedAfterFiveFailures()
    {
        using var server = new DatabaseFactory();
        using var http = server.CreateClient();
        for (int attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, await Post(http, "/api/auth/login", new LoginInput("manager", "wrong")));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Post(http, "/api/auth/login", new LoginInput("manager", TestPassword)));
    }

    [Fact]
    public async Task HealthFailsWhenLessonSchemaIsMissing()
    {
        using var server = new DatabaseFactory();
        using var http = server.CreateClient();
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDb>();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE Lessons");
        using var response = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("no such table", body);
    }

    [Fact]
    public async Task LiteralSearchAndUnknownLessonDoNotRevealOtherRecords()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", TestPassword, Roles.Manager);
        int student = await CreateStudent(manager);
        int lesson = await CreateLesson(manager, student, 3, "Учебный маршрут");
        Assert.Equal(lesson, Assert.Single(await Get<LessonInfo[]>(manager, "/api/lessons?q=" +
            Uri.EscapeDataString("уЧЕБНЫЙ"))).Id);
        Assert.Empty(await Get<LessonInfo[]>(manager, "/api/lessons?q=" + Uri.EscapeDataString("%' --")));
        using var missing = await manager.GetAsync("/api/lessons/99999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StudentLimitsAreEnforcedAtCreateEndpoint()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", TestPassword, Roles.Manager);
        using var accepted = await manager.PostAsJsonAsync("/api/students", new StudentInput(new string('Я', 150), new string('1', 40), null));
        using var nameTooLong = await manager.PostAsJsonAsync("/api/students", new StudentInput(new string('Я', 151), "1", null));
        using var phoneTooLong = await manager.PostAsJsonAsync("/api/students", new StudentInput("Ученик", new string('1', 41), null));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nameTooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, phoneTooLong.StatusCode);
    }
}
