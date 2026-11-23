using System.Diagnostics;
using System.Globalization;

namespace DrivingSchool.Core;

public static class LessonRules
{
    public static bool IsFinal(LessonStatus status) =>
        status is LessonStatus.Completed or LessonStatus.Rescheduled or LessonStatus.Missed;

    public static IReadOnlyList<LessonStatus> Next(LessonStatus status) => status switch
    {
        LessonStatus.Planned => [LessonStatus.Confirmed, LessonStatus.Rescheduled, LessonStatus.Missed],
        LessonStatus.Confirmed => [LessonStatus.Completed, LessonStatus.Rescheduled, LessonStatus.Missed],
        _ => []
    };

    public static void Transition(PracticalLesson lesson, LessonStatus target, string? reason, DateTime now,
        DateTimeOffset? finishedAt = null)
    {
        if (!Next(lesson.Status).Contains(target))
            throw new RuleException("Переход из текущего статуса недоступен.");

        if (target == LessonStatus.Confirmed && (lesson.InstructorId is null || lesson.ScheduledAt is null))
            throw new RuleException("Для подтверждения назначьте инструктора и время занятия.");

        if (target == LessonStatus.Completed)
        {
            InputRules.CheckAmount(lesson.Amount);
            if (lesson.Amount is null || finishedAt is null || finishedAt.Value.UtcDateTime < lesson.CreatedAt
                || finishedAt.Value < lesson.ScheduledAt || finishedAt.Value.UtcDateTime > now)
                throw new RuleException("Для проведения укажите сумму и фактическое время от начала занятия до текущего момента.");
            lesson.CompletedAt = finishedAt.Value.UtcDateTime;
        }

        if (target is LessonStatus.Rescheduled or LessonStatus.Missed)
            lesson.ClosingReason = InputRules.Required(reason, "Причина завершения", 500);

        lesson.Status = target;
        lesson.Version++;
        Debug.Assert(lesson.Status != LessonStatus.Completed || lesson.CompletedAt.HasValue);
    }

    public static SchoolStatistics Statistics(IReadOnlyList<LessonInfo> rows) =>
        StatisticsFromMetrics(rows.Select(row => new LessonMetrics(row.Status, row.Amount, row.CreatedAt, row.CompletedAt)));

    public static SchoolStatistics StatisticsFromMetrics(IEnumerable<LessonMetrics> rows)
    {
        var statuses = Enum.GetValues<LessonStatus>();
        var counts = new int[statuses.Length];
        int total = 0, durationCount = 0;
        decimal totalAmount = 0, completedAmount = 0;
        double duration = 0;
        foreach (var row in rows)
        {
            total++;
            if ((uint)row.Status < counts.Length)
                counts[(int)row.Status]++;
            totalAmount += row.Amount ?? 0;
            if (row.Status != LessonStatus.Completed)
                continue;
            completedAmount += row.Amount ?? 0;
            if (row.CompletedAt is { } completion && completion >= row.CreatedAt)
            {
                duration += (completion - row.CreatedAt).TotalDays;
                durationCount++;
            }
        }
        int completed = counts[(int)LessonStatus.Completed], rescheduled = counts[(int)LessonStatus.Rescheduled];
        int missed = counts[(int)LessonStatus.Missed];
        return new SchoolStatistics(total, total - completed - rescheduled - missed, completed, rescheduled, missed, totalAmount,
            completedAmount, durationCount == 0 ? null : Math.Round(duration / durationCount, 2),
            statuses.Select(status => new StatusCount(status, counts[(int)status])).ToArray());
    }
}

public static class InputRules
{
    public static decimal? ParseAmount(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        int separator = text.IndexOfAny([',', '.']);
        if ((separator >= 0 && text.Length - separator - 1 is < 1 or > 2) ||
            !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var amount))
            throw new RuleException("Введите сумму без разделителей тысяч, максимум с двумя знаками после запятой.");
        CheckAmount(amount);
        return amount;
    }

    public static void CheckAmount(decimal? amount)
    {
        if (amount is <= 0 or > 999_999_999 || amount is { } value && value != decimal.Round(value, 2))
            throw new RuleException("Сумма должна быть положительной, до 999 999 999 ₽ и максимум с двумя знаками после запятой.");
    }

    public static DateTimeOffset? ParseScheduledAt(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        if (DateTimeOffset.TryParseExact(text, ["dd.MM.yyyy HH:mm", "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return date;
        throw new RuleException("Введите дату и время в формате ДД.ММ.ГГГГ ЧЧ:ММ.");
    }

    public static string Required(string? value, string field, int max)
    {
        var trimmed = Optional(value, field, max);
        if (trimmed.Length == 0)
            throw new RuleException($"Заполните поле «{field}».");
        return trimmed;
    }

    public static string Optional(string? value, string field, int max)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length > max)
            throw new RuleException($"Поле «{field}» допускает до {max} символов.");
        return trimmed;
    }

    public static StudentInput Student(StudentInput input)
    {
        var name = Required(input.Name, "Имя ученика", 150);
        var phone = Required(input.Phone, "Телефон", 40);
        var email = Optional(input.Email, "Эл. почта", 150);
        if (email.Length != 0 && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new RuleException("Введите корректный адрес электронной почты.");
        return new StudentInput(name, phone, email);
    }
}


