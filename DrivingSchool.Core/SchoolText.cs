namespace DrivingSchool.Core;

public static class SchoolText
{
    public static string Status(LessonStatus value) => value switch
    {
        LessonStatus.Planned => "Запланировано",
        LessonStatus.Confirmed => "Подтверждено",
        LessonStatus.Missed => "Пропущено",
        LessonStatus.Completed => "Проведено",
        LessonStatus.Rescheduled => "Перенесено",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string Role(string role) => role switch
    {
        Roles.Admin => "Администратор",
        Roles.Manager => "Менеджер",
        Roles.Instructor => "Инструктор",
        _ => role
    };
}
