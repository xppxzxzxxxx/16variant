using DrivingSchool.Core;
using Xunit;

namespace DrivingSchool.Tests;

public sealed class LessonRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(LessonStatus.Planned, LessonStatus.Confirmed)]
    [InlineData(LessonStatus.Confirmed, LessonStatus.Completed)]
    public void AdvancesThroughEachStage(LessonStatus from, LessonStatus to)
    {
        var lesson = Ready(from);
        LessonRules.Transition(lesson, to, null, Now, Now);
        Assert.Equal(to, lesson.Status);
        Assert.Equal(2, lesson.Version);
        Assert.Equal(to == LessonStatus.Completed ? Now : (DateTime?)null, lesson.CompletedAt);
    }

    [Theory]
    [InlineData(LessonStatus.Planned, LessonStatus.Rescheduled)]
    [InlineData(LessonStatus.Confirmed, LessonStatus.Rescheduled)]
    [InlineData(LessonStatus.Confirmed, LessonStatus.Missed)]
    public void ExceptionalStatusesRequireReason(LessonStatus from, LessonStatus target)
    {
        var lesson = Ready(from);
        Assert.Throws<RuleException>(() => LessonRules.Transition(lesson, target, "  ", Now));
        LessonRules.Transition(lesson, target, "  Изменилось расписание  ", Now);
        Assert.Equal(target, lesson.Status);
        Assert.Equal("Изменилось расписание", lesson.ClosingReason);
        Assert.True(LessonRules.IsFinal(target));
    }

    [Theory]
    [InlineData(LessonStatus.Planned, LessonStatus.Completed)]
    [InlineData(LessonStatus.Missed, LessonStatus.Confirmed)]
    [InlineData(LessonStatus.Completed, LessonStatus.Rescheduled)]
    [InlineData(LessonStatus.Rescheduled, LessonStatus.Planned)]
    public void RejectsInvalidTransitions(LessonStatus from, LessonStatus to)
    {
        var lesson = Ready(from);
        Assert.Throws<RuleException>(() => LessonRules.Transition(lesson, to, "Причина", Now));
        Assert.Equal(from, lesson.Status);
        Assert.Equal(1, lesson.Version);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ConfirmationRequiresInstructorAndStart(bool instructor, bool start)
    {
        var lesson = Ready(LessonStatus.Planned);
        lesson.InstructorId = instructor ? 3 : null;
        lesson.ScheduledAt = start ? Now : null;
        Assert.Throws<RuleException>(() => LessonRules.Transition(lesson, LessonStatus.Confirmed, null, Now));
        Assert.Equal(1, lesson.Version);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, null)]
    [InlineData(true, -5)]
    [InlineData(true, 5)]
    public void CompletionRequiresAmountAndValidFinish(bool amount, int? minutes)
    {
        var lesson = Ready(LessonStatus.Confirmed);
        lesson.Amount = amount ? 5000 : null;
        DateTimeOffset? finished = minutes is { } offset ? Now.AddMinutes(offset) : null;
        Assert.Throws<RuleException>(() => LessonRules.Transition(lesson, LessonStatus.Completed, null, Now, finished));
        Assert.Null(lesson.CompletedAt);
    }

    [Fact]
    public void StatisticsUsesCompletedLessonsForAverage()
    {
        var rows = new[] { Row(LessonStatus.Completed, 6000, Now.AddDays(-1.5), Now),
            Row(LessonStatus.Completed, 4000, Now.AddDays(-2.5), Now),
            Row(LessonStatus.Rescheduled, 1000, Now.AddDays(-5), null),
            Row(LessonStatus.Confirmed, 2000, Now.AddDays(-10), null) };
        var statistics = LessonRules.Statistics(rows);
        Assert.Equal(4, statistics.Total);
        Assert.Equal(1, statistics.Open);
        Assert.Equal(2, statistics.Completed);
        Assert.Equal(1, statistics.Rescheduled);
        Assert.Equal(13000, statistics.TotalAmount);
        Assert.Equal(10000, statistics.CompletedAmount);
        Assert.Equal(2, statistics.AverageDays);
        Assert.Equal(5, statistics.ByStatus.Length);
        Assert.Null(LessonRules.Statistics([]).AverageDays);
    }

    private static PracticalLesson Ready(LessonStatus status) => new()
    {
        Status = status, InstructorId = 3, Amount = 5000, ScheduledAt = Now, CreatedAt = Now.AddDays(-1), Version = 1
    };

    internal static LessonInfo Row(LessonStatus status, decimal amount, DateTime created, DateTime? completed) =>
        new(1, 1, "Елена; Петрова", "+79990001234", "Маршрут \"Город\" <центр>", "Ул. Парковая, 10", "",
            3, "Инструктор", status, amount, null, null, created, completed, 1);
}
