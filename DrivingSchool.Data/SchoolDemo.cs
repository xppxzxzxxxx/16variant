using DrivingSchool.Core;
using Microsoft.EntityFrameworkCore;

namespace DrivingSchool.Data;

internal static class SchoolDemo
{
    public static async Task Seed(DbContext db, CancellationToken ct)
    {
        if (await db.Set<Student>().AnyAsync(ct) || await db.Set<PracticalLesson>().AnyAsync(ct))
            return;

        var start = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
        Student[] students =
        [
            new() { Name = "Анна Белова", Phone = "+7 000 000-01-01", RegisteredAt = start },
            new() { Name = "Роман Орлов", Phone = "+7 000 000-01-02", RegisteredAt = start },
            new() { Name = "Мария Соколова", Phone = "+7 000 000-01-03", RegisteredAt = start }
        ];

        db.AddRange(
            Lesson(0, "Площадка и разворот", LessonStatus.Planned),
            Lesson(1, "Параллельная парковка", LessonStatus.Confirmed, 3, 1600),
            Lesson(2, "Городской маршрут", LessonStatus.Confirmed, 4, 1800),
            Lesson(3, "Заезд в бокс", LessonStatus.Missed, 4, 1500),
            Lesson(4, "Перекрёстки и повороты", LessonStatus.Completed, 3, 1850, 2),
            Lesson(5, "Движение в потоке", LessonStatus.Completed, 4, 1900, 3),
            Lesson(6, "Начало движения", LessonStatus.Rescheduled, 3, 1500));
        await db.SaveChangesAsync(ct);

        PracticalLesson Lesson(int index, string title, LessonStatus status, int? instructor = null,
            decimal? amount = null, int? days = null) => new()
        {
            Student = students[index % students.Length],
            Title = title,
            Address = $"Учебная площадка, сектор {index + 1}",
            Status = status,
            InstructorId = instructor,
            Amount = amount,
            ScheduledAt = start.AddDays(index + (days ?? 1)),
            CreatedAt = start.AddDays(index),
            CompletedAt = days is { } duration ? start.AddDays(index + duration).AddHours(1) : null,
            ClosingReason = status switch
            {
                LessonStatus.Rescheduled => "Ученик попросил выбрать другое время",
                LessonStatus.Missed => "Ученик не явился к началу занятия",
                _ => null
            },
            Version = 1
        };
    }
}
