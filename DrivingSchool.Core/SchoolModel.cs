using System.Diagnostics;

namespace DrivingSchool.Core;

public enum LessonStatus { Planned, Confirmed, Completed, Rescheduled, Missed }
public enum UserRole { Admin, Manager, Instructor }

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Manager = nameof(UserRole.Manager);
    public const string Instructor = nameof(UserRole.Instructor);
    public const string Staff = Admin + "," + Manager;
}

[DebuggerDisplay("Ученик #{Id}")]
public sealed class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime? RegisteredAt { get; set; }
}

[DebuggerDisplay("Пользователь #{Id} ({Role,nq})")]
public sealed class SchoolUser
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

[DebuggerDisplay("Занятие #{Id} · {Status} · версия {Version}")]
public sealed class PracticalLesson
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? InstructorId { get; set; }
    public SchoolUser? Instructor { get; set; }
    public LessonStatus Status { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string? ClosingReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Version { get; set; }
}

public sealed record StudentInput(string Name, string Phone, string? Email);
public sealed record StudentInfo(int Id, string Name, string Phone, string Email);
public sealed record UserInfo(int Id, string Login, string DisplayName, string Role);
public sealed record LoginInput(string Login, string Password);
public sealed record LoginResult(string Token, UserInfo User);
public sealed record LessonInput(int StudentId, string Title, string Address, string? Notes,
    int? InstructorId, decimal? Amount, DateTimeOffset? ScheduledAt, int? Version = null);
public sealed record TransitionInput(LessonStatus Status, string? Reason, int Version, DateTimeOffset? FinishedAt = null);
public sealed record LessonInfo(int Id, int StudentId, string Student, string Phone, string Title,
    string Address, string Notes, int? InstructorId, string? Instructor, LessonStatus Status,
    decimal? Amount, DateTimeOffset? ScheduledAt, string? ClosingReason, DateTime CreatedAt,
    DateTime? CompletedAt, int Version);
public sealed record StatusCount(LessonStatus Status, int Count);
public readonly record struct LessonMetrics(LessonStatus Status, decimal? Amount, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record SchoolStatistics(int Total, int Open, int Completed, int Rescheduled, int Missed,
    decimal TotalAmount, decimal CompletedAmount, double? AverageDays, StatusCount[] ByStatus);
public sealed record ImportResult(int Imported);
public sealed record HealthInfo(string Status);

public class RuleException(string message) : Exception(message);
public sealed class LessonConflictException() : Exception("Обновите список, откройте занятие и повторите действие.");
public sealed record CsvRowError(long Line, string Message);
public sealed class CsvImportException(IReadOnlyList<CsvRowError> errors) :
    RuleException(string.Join(Environment.NewLine, errors.Take(10).Select(row => $"Строка {row.Line}: {row.Message}"))
        + (errors.Count > 10 ? $"\nВсего ошибок: {errors.Count}." : ""))
{
    public IReadOnlyList<CsvRowError> Errors { get; } = errors;
}
