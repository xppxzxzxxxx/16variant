using System.Buffers;
using System.Security.Claims;
using System.Text;
using DrivingSchool.Core;
using DrivingSchool.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DrivingSchool.Api;

internal static class SchoolEndpoints
{
    public static void MapSchoolEndpoints(this WebApplication app)
    {
        app.MapGet("/health", CheckHealth);

        var auth = app.MapGroup("/api/auth").WithTags("Аутентификация");
        auth.MapPost("/login", Login).RequireRateLimiting("Login");
        auth.MapGet("/me", (ClaimsPrincipal user) => SessionTokens.User(user)).RequireAuthorization();
        auth.MapPost("/logout", (ClaimsPrincipal user, IMemoryCache cache) =>
        {
            SessionTokens.Revoke(user, cache);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/students/import", Import).WithTags("Ученики")
            .RequireAuthorization("Staff").Accepts<string>("text/csv");
        app.MapGet("/api/lessons/export/{format}", Export).WithTags("Занятия").RequireAuthorization("Staff");
    }

    private static async Task<IResult> CheckHealth(SchoolDb db, ILogger<HealthInfo> logger, CancellationToken ct)
    {
        try
        {
            await db.Lessons.AnyAsync(ct);
            return Results.Ok(new HealthInfo("ok"));
        }
        catch (SqliteException error)
        {
            logger.LogWarning("Проверка базы не прошла: код SQLite {ErrorCode}", error.SqliteErrorCode);
            return Results.Problem(statusCode: 503, title: "База данных недоступна");
        }
    }

    private static async Task<IResult> Login(LoginInput input, IUserRepository users, SessionTokens tokens,
        ILogger<SessionTokens> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Login) || input.Login.Length > 80 ||
            string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
            return Results.Unauthorized();

        string login = input.Login.Trim().ToLowerInvariant();
        var user = await users.FindUser(login, ct);
        const string dummyHash = "00000000000000000000000000000000:0000000000000000000000000000000000000000000000000000000000000000";
        if (!SchoolDatabase.Verify(input.Password, user?.PasswordHash ?? dummyHash) || user is null)
        {
            logger.LogWarning("Неудачная попытка авторизации");
            return Results.Unauthorized();
        }

        var result = new UserInfo(user.Id, user.Login, user.DisplayName, user.Role);
        logger.LogInformation("Авторизация пользователя {UserId} с ролью {Role}", user.Id, user.Role);
        return Results.Ok(new LoginResult(tokens.Issue(result), result));
    }

    private static async Task<IResult> Import(HttpRequest request, IStudentRepository store,
        ClaimsPrincipal user, ILogger<IStudentRepository> logger, CancellationToken ct)
    {
        if (request.ContentLength > SchoolFiles.MaxImportBytes)
            return Results.Problem(statusCode: 413, title: "Размер CSV превышает 1 МБ.");

        using var buffer = new MemoryStream();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int count;
            while ((count = await request.Body.ReadAsync(chunk.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + count > SchoolFiles.MaxImportBytes)
                    return Results.Problem(statusCode: 413, title: "Размер CSV превышает 1 МБ.");
                buffer.Write(chunk, 0, count);
            }
            try
            {
                var csv = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                int imported = await store.ImportStudents(csv, ct);
                logger.LogInformation("Пользователем {UserId} импортировано учеников: {ImportedCount}",
                    SessionTokens.User(user).Id, imported);
                return Results.Ok(new ImportResult(imported));
            }
            catch (DecoderFallbackException)
            {
                throw new RuleException("Сохраните CSV в кодировке UTF-8.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }
    }

    private static async Task<IResult> Export(string format, LessonStatus? status, string? q,
        ClaimsPrincipal user, ILessonRepository store, CancellationToken ct)
    {
        format = format.ToLowerInvariant();
        var result = SchoolFiles.Export(format, await store.FindLessons(SessionTokens.User(user), status, q, ct));
        return Results.File(result.Bytes, result.ContentType, $"lessons.{format}");
    }
}
