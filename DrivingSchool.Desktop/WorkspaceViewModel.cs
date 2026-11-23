using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using DrivingSchool.Core;

namespace DrivingSchool.Desktop;

internal sealed record StatusOption(LessonStatus? Status, string Name);
internal sealed record StatusSummary(string Name, int Count);
internal sealed record LessonRow(LessonInfo Lesson)
{
    public string Status => SchoolText.Status(Lesson.Status);
    public string Amount => Lesson.Amount is { } amount ? $"{amount:N2} ₽" : "—";
    public string Schedule => Lesson.ScheduledAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—";
}
internal sealed record UserRow(UserInfo User)
{
    public string RoleName => SchoolText.Role(User.Role);
}

internal sealed class WorkspaceViewModel : AsyncViewModel
{
    private readonly SchoolClient api;
    private readonly SchoolDialogs dialogs;
    private LessonRow[] lessons = [];
    private StudentInfo[] students = [];
    private UserRow[] users = [];
    private SchoolStatistics? statistics;
    private LessonRow? selectedLesson;
    private StudentInfo? selectedStudent;
    private int tab;

    public string SessionCaption { get; }
    public bool IsStaff { get; }
    public bool IsAdmin { get; }
    public string LessonHeading => IsStaff ? "Занятия автошколы" : "Мои занятия";
    public string EmptyLessons => Lessons.Length == 0 ? "Занятия по заданным условиям отсутствуют." : "";
    public string LessonCount => $"Занятий в списке: {Lessons.Length}";
    public string AverageDays => Statistics?.AverageDays is { } days ? $"{days:N2} дн." : "—";
    public StatusSummary[] Statuses => Statistics?.ByStatus.Select(row =>
        new StatusSummary(SchoolText.Status(row.Status), row.Count)).ToArray() ?? [];

    public StatusOption[] Filters { get; } = [new(null, "Все статусы"), .. Enum.GetValues<LessonStatus>()
        .Select(status => new StatusOption(status, SchoolText.Status(status)))];
    public StatusOption? SelectedFilter { get; set; }
    public string Search { get; set; } = "";
    public LessonRow[] Lessons { get => lessons; private set => Set(ref lessons, value); }
    public StudentInfo[] Students { get => students; private set => Set(ref students, value); }
    public UserRow[] Users { get => users; private set => Set(ref users, value); }
    public SchoolStatistics? Statistics { get => statistics; private set => Set(ref statistics, value); }
    public LessonRow? SelectedLesson { get => selectedLesson; set => Set(ref selectedLesson, value); }
    public StudentInfo? SelectedStudent { get => selectedStudent; set => Set(ref selectedStudent, value); }
    public int Tab
    {
        get => tab;
        set
        {
            if (tab == value)
                return;
            Set(ref tab, value);
            var command = value switch
            {
                1 when IsStaff => RefreshStudents,
                2 => RefreshStatistics,
                3 when IsAdmin => RefreshUsers,
                _ => null
            };
            command?.Execute(null);
        }
    }

    public ICommand RefreshLessons { get; }
    public ICommand RefreshStudents { get; }
    public ICommand RefreshStatistics { get; }
    public ICommand RefreshUsers { get; }
    public ICommand NewLesson { get; }
    public ICommand EditLesson { get; }
    public ICommand Details { get; }
    public ICommand Transition { get; }
    public ICommand NewStudent { get; }
    public ICommand EditStudent { get; }
    public ICommand Import { get; }
    public ICommand Export { get; }
    public ICommand Logout { get; }

    public WorkspaceViewModel(SchoolClient api, LoginResult login, SchoolDialogs dialogs, Action signedOut)
    {
        this.api = api;
        this.dialogs = dialogs;
        IsStaff = login.User.Role is Roles.Admin or Roles.Manager;
        IsAdmin = login.User.Role == Roles.Admin;
        var sessionName = login.User.Role == Roles.Instructor ? login.User.DisplayName : login.User.Login;
        SessionCaption = $"{SchoolText.Role(login.User.Role)} · {sessionName}";
        SelectedFilter = Filters[0];

        RefreshLessons = Run(_ => LoadLessons());
        RefreshStudents = Run(_ => LoadStudents(), () => IsStaff);
        RefreshStatistics = Run(async _ =>
        {
            Statistics = await api.Statistics();
            Changed(nameof(Statuses));
            Changed(nameof(AverageDays));
        });
        RefreshUsers = Run(async _ => Users = (await api.Users(false)).Select(user => new UserRow(user)).ToArray(),
            () => IsAdmin);

        NewLesson = Run(_ => SaveLesson(null), () => IsStaff);
        EditLesson = Run(_ => SaveLesson(SelectedLesson!.Lesson),
            () => IsStaff && SelectedLesson is { } row && !LessonRules.IsFinal(row.Lesson.Status));
        Details = Run(_ =>
        {
            dialogs.ShowLesson(SelectedLesson!.Lesson);
            return Task.CompletedTask;
        }, () => SelectedLesson is not null);
        Transition = Run(async _ =>
        {
            var lesson = SelectedLesson!.Lesson;
            if (dialogs.Transition(lesson) is not { } input)
                return;
            await api.Transition(lesson.Id, input);
            ClientLog.Info($"Занятие {lesson.Id}: переход в {input.Status}");
            await LoadLessons(lesson.Id);
            Message = $"Занятие №{lesson.Id} переведён в статус «{SchoolText.Status(input.Status)}».";
        }, () => SelectedLesson is { } row && !LessonRules.IsFinal(row.Lesson.Status));

        NewStudent = Run(_ => SaveStudent(null), () => IsStaff);
        EditStudent = Run(_ => SaveStudent(SelectedStudent), () => IsStaff && SelectedStudent is not null);
        Import = Run(async _ =>
        {
            if (dialogs.OpenCsv() is not { } path)
                return;
            if (new FileInfo(path).Length > SchoolFiles.MaxImportBytes)
                throw new RuleException("Размер CSV превышает 1 МБ.");
            try
            {
                var result = await api.Import(await File.ReadAllBytesAsync(path));
                await LoadStudents();
                ClientLog.Info($"Импортировано учеников: {result.Imported}");
                Message = $"Импортировано учеников: {result.Imported}.";
            }
            catch (CsvImportException error)
            {
                ClientLog.Warning($"Отклонён CSV: ошибок {error.Errors.Count}");
                dialogs.ShowImportErrors(error.Errors);
                Message = $"Импорт отклонён. Ошибок: {error.Errors.Count}.";
            }
        }, () => IsStaff);
        Export = Run(async selectedFormat =>
        {
            var format = (string)selectedFormat!;
            if (dialogs.SaveExport(format) is not { } path)
                return;
            var timer = Stopwatch.StartNew();
            await File.WriteAllBytesAsync(path, await api.Export(format, SelectedFilter?.Status, Search));
            timer.Stop();
            Debug.WriteLine($"Экспорт {format}: {timer.ElapsedMilliseconds} мс");
            Message = $"Занятия выгружены в {Path.GetFileName(path)}.";
        }, () => IsStaff);
        Logout = Run(async _ =>
        {
            try
            {
                await api.Logout();
            }
            catch (ClientException error)
            {
                ClientLog.Write(error);
            }
            finally
            {
                signedOut();
            }
        });
    }

    private async Task LoadLessons(int? id = null)
    {
        var requestedId = id ?? SelectedLesson?.Lesson.Id;
        Lessons = (await api.Lessons(SelectedFilter?.Status, Search)).Select(lesson => new LessonRow(lesson)).ToArray();
        SelectedLesson = Lessons.FirstOrDefault(row => row.Lesson.Id == requestedId);
        Changed(nameof(LessonCount));
        Changed(nameof(EmptyLessons));
    }

    private async Task LoadStudents() => Students = await api.Students();

    private async Task SaveStudent(StudentInfo? student)
    {
        if (dialogs.Student(student) is not { } input)
            return;
        await api.SaveStudent(student?.Id, input);
        await LoadStudents();
        Message = "Данные ученика сохранены.";
    }

    private async Task SaveLesson(LessonInfo? lesson)
    {
        var availableStudents = await api.Students();
        if (availableStudents.Length == 0)
        {
            Tab = 1;
            throw new RuleException("Сначала добавьте ученика в справочник.");
        }
        var instructors = await api.Users(true);
        if (dialogs.Lesson(lesson, availableStudents, instructors) is not { } input)
            return;
        await api.SaveLesson(lesson?.Id, input);
        await LoadLessons(lesson?.Id);
        Message = "Занятие сохранён.";
    }
}
