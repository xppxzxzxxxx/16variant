using System.Linq.Expressions;
using DrivingSchool.Core;
using Microsoft.EntityFrameworkCore;

namespace DrivingSchool.Data;

public sealed class SchoolStore(SchoolDb db, TimeProvider time) : ILessonRepository, IStudentRepository, IUserRepository
{
    private static readonly Expression<Func<PracticalLesson, LessonInfo>> LessonProjection = row =>
        new LessonInfo(row.Id, row.StudentId, row.Student.Name, row.Student.Phone, row.Title,
            row.Address, row.Notes, row.InstructorId, row.Instructor == null ? null : row.Instructor.DisplayName,
            row.Status, row.Amount, row.ScheduledAt, row.ClosingReason, row.CreatedAt,
            row.CompletedAt, row.Version);

    public Task<List<LessonInfo>> FindLessons(UserInfo user, LessonStatus? status, string? search, CancellationToken ct)
    {
        var query = VisibleLessons(user).AsNoTracking();
        if (status.HasValue)
            query = query.Where(row => row.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            string uppercase = term.ToUpperInvariant();
            bool byId = int.TryParse(term.TrimStart('#'), out int lessonId);
            // SQLite's built-in NOCASE collation does not fold Cyrillic.
            query = query.Where(row => (byId && row.Id == lessonId) ||
                SchoolContains(row.Title, uppercase) || SchoolContains(row.Address, uppercase) ||
                SchoolContains(row.Student.Name, uppercase) || row.Student.Phone.Contains(term));
        }

        return query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(LessonProjection).ToListAsync(ct);
    }

    [DbFunction("school_contains", IsBuiltIn = true)]
    public static bool SchoolContains(string value, string uppercase) =>
        throw new InvalidOperationException("Функция выполняется в SQLite.");

    public Task<LessonInfo?> FindLesson(int id, UserInfo user, CancellationToken ct) =>
        VisibleLessons(user).AsNoTracking().Where(row => row.Id == id)
            .Select(LessonProjection).SingleOrDefaultAsync(ct);

    public Task<List<LessonMetrics>> FindMetrics(UserInfo user, CancellationToken ct) =>
        VisibleLessons(user).AsNoTracking()
            .Select(row => new LessonMetrics(row.Status, row.Amount, row.CreatedAt, row.CompletedAt)).ToListAsync(ct);

    public Task<List<StudentInfo>> GetStudents(CancellationToken ct) => db.Students.AsNoTracking()
        .OrderBy(student => student.Name).ThenBy(student => student.Id)
        .Select(student => new StudentInfo(student.Id, student.Name, student.Phone, student.Email)).ToListAsync(ct);

    public Task<List<UserInfo>> GetUsers(bool instructorsOnly, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(user => !instructorsOnly || user.Role == Roles.Instructor)
            .OrderBy(user => user.Id)
            .Select(user => new UserInfo(user.Id, user.Login, user.DisplayName, user.Role)).ToListAsync(ct);

    public Task<SchoolUser?> FindUser(string login, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Login == login, ct);

    public async Task<int> CreateStudent(StudentInput input, CancellationToken ct)
    {
        var student = NewStudent(InputRules.Student(input));
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return student.Id;
    }

    public async Task<bool> UpdateStudent(int id, StudentInput input, CancellationToken ct)
    {
        var student = await db.Students.FindAsync([id], ct);
        if (student is null)
            return false;

        var values = InputRules.Student(input);
        student.Name = values.Name;
        student.Phone = values.Phone;
        student.Email = values.Email!;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportStudents(string csv, CancellationToken ct)
    {
        var values = SchoolFiles.ReadStudents(csv);
        db.Students.AddRange(values.Select(NewStudent));
        await db.SaveChangesAsync(ct);
        return values.Count;
    }

    public async Task<int> CreateLesson(LessonInput input, CancellationToken ct)
    {
        var lesson = new PracticalLesson { CreatedAt = time.GetUtcNow().UtcDateTime, Version = 1 };
        await SetDetails(lesson, input, ct);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(ct);
        return lesson.Id;
    }

    public async Task<bool> UpdateLesson(int id, LessonInput input, CancellationToken ct)
    {
        var lesson = await db.Lessons.FindAsync([id], ct);
        if (lesson is null)
            return false;

        CheckVersion(lesson, input.Version);
        if (LessonRules.IsFinal(lesson.Status))
            throw new RuleException("Завершённое занятие закрыто для редактирования.");

        await SetDetails(lesson, input, ct);
        lesson.Version++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct)
    {
        var lesson = await VisibleLessons(user).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (lesson is null)
            return false;

        CheckVersion(lesson, input.Version);
        LessonRules.Transition(lesson, input.Status, input.Reason, time.GetUtcNow().UtcDateTime, input.FinishedAt);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<PracticalLesson> VisibleLessons(UserInfo user) =>
        user.Role == Roles.Instructor ? db.Lessons.Where(row => row.InstructorId == user.Id) : db.Lessons;

    private static void CheckVersion(PracticalLesson lesson, int? version)
    {
        if (version is null)
            throw new RuleException("Укажите версию занятия.");
        if (lesson.Version != version)
            throw new LessonConflictException();
    }

    private Student NewStudent(StudentInput input) =>
        new() { Name = input.Name, Phone = input.Phone, Email = input.Email!, RegisteredAt = time.GetUtcNow().UtcDateTime };

    private async Task SetDetails(PracticalLesson lesson, LessonInput input, CancellationToken ct)
    {
        lesson.Title = InputRules.Required(input.Title, "Упражнение", 180);
        lesson.Address = InputRules.Required(input.Address, "Место встречи", 220);
        lesson.Notes = InputRules.Optional(input.Notes, "Примечания", 2000);
        InputRules.CheckAmount(input.Amount);

        if (!await db.Students.AnyAsync(student => student.Id == input.StudentId, ct))
            throw new RuleException("Выберите существующего ученика.");

        if (input.InstructorId is { } instructorId &&
            !await db.Users.AnyAsync(user => user.Id == instructorId && user.Role == Roles.Instructor, ct))
            throw new RuleException("Выберите пользователя с ролью инструктора.");

        if (lesson.Status == LessonStatus.Confirmed && (input.InstructorId is null || input.ScheduledAt is null))
            throw new RuleException("Подтверждённому занятию нужны инструктор и время.");

        lesson.StudentId = input.StudentId;
        lesson.InstructorId = input.InstructorId;
        lesson.Amount = input.Amount;
        lesson.ScheduledAt = input.ScheduledAt;
    }
}
