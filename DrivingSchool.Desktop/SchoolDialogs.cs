using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using DrivingSchool.Core;
using Microsoft.Win32;

namespace DrivingSchool.Desktop;

internal sealed class SchoolDialogs(Window owner)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public LessonInput? Lesson(LessonInfo? lesson, StudentInfo[] students, UserInfo[] instructors)
    {
        var form = new Form(owner, lesson is null ? "Новое занятие" : $"Занятие №{lesson.Id}");
        var student = new ComboBox { ItemsSource = students, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
        student.SelectedValue = lesson?.StudentId ?? students[0].Id;
        form.Add("StudentSelect", "_Ученик", student);

        var title = form.Text("TitleInput", "_Упражнение", lesson?.Title, 180);
        var address = form.Text("AddressInput", "_Место встречи", lesson?.Address, 220);
        var notes = form.Text("NotesInput", "_Примечания", lesson?.Notes, 2000, multiline: true);

        var assignees = new[] { new UserInfo(0, "", "Без назначения", "") }.Concat(instructors).ToArray();
        var instructor = new ComboBox { ItemsSource = assignees, DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
        instructor.SelectedValue = lesson?.InstructorId ?? 0;
        form.Add("InstructorSelect", "_Инструктор", instructor);

        var amount = form.Text("AmountInput", "С_умма, ₽", lesson?.Amount?.ToString("0.##", Russian), 24);
        var schedule = form.Text("ScheduledAtInput", "_Время занятия (ДД.ММ.ГГГГ ЧЧ:ММ)",
            lesson?.ScheduledAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), 16);

        return form.Save("Сохранить", () =>
        {
            if (student.SelectedValue is not int studentId)
                throw new RuleException("Выберите ученика.");

            int? instructorId = instructor.SelectedValue is int id && id > 0 ? id : null;
            return new LessonInput(studentId, InputRules.Required(title.Text, "Упражнение", 180),
                InputRules.Required(address.Text, "Место встречи", 220), notes.Text, instructorId,
                InputRules.ParseAmount(amount.Text), InputRules.ParseScheduledAt(schedule.Text), lesson?.Version);
        });
    }

    public StudentInput? Student(StudentInfo? student)
    {
        var form = new Form(owner, student is null ? "Новый ученик" : "Редактирование ученика");
        var name = form.Text("StudentNameInput", "_Имя", student?.Name, 150);
        var phone = form.Text("StudentPhoneInput", "_Телефон", student?.Phone, 40);
        var email = form.Text("StudentEmailInput", "_Эл. почта", student?.Email, 150);
        return form.Save("Сохранить", () => InputRules.Student(new StudentInput(name.Text, phone.Text, email.Text)));
    }

    public TransitionInput? Transition(LessonInfo lesson)
    {
        var form = new Form(owner, $"Смена статуса · занятие №{lesson.Id}");
        form.Summary($"Сейчас: {SchoolText.Status(lesson.Status)}");
        var options = LessonRules.Next(lesson.Status).Select(status => new StatusOption(status, SchoolText.Status(status))).ToArray();
        var status = new ComboBox { ItemsSource = options, DisplayMemberPath = "Name", SelectedIndex = 0 };
        form.Add("NextStatusSelect", "Новый _статус", status);
        var reason = form.Text("ClosingReasonInput", "_Причина завершения", null, 500, multiline: true);
        var finished = form.Text("FinishedAtInput", "_Фактическое завершение (ДД.ММ.ГГГГ ЧЧ:ММ)",
            DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), 16);
        void UpdateFields()
        {
            var selected = ((StatusOption?)status.SelectedItem)?.Status;
            reason.IsEnabled = selected is LessonStatus.Rescheduled or LessonStatus.Missed;
            finished.IsEnabled = selected == LessonStatus.Completed;
        }
        UpdateFields();
        status.SelectionChanged += (_, _) => UpdateFields();
        return form.Save("Применить", () =>
        {
            var next = ((StatusOption?)status.SelectedItem)?.Status ?? throw new RuleException("Выберите статус.");
            return new TransitionInput(next, next is LessonStatus.Rescheduled or LessonStatus.Missed
                ? InputRules.Required(reason.Text, "Причина завершения", 500) : null, lesson.Version,
                next == LessonStatus.Completed ? InputRules.ParseScheduledAt(finished.Text) : null);
        });
    }

    public void ShowLesson(LessonInfo lesson)
    {
        var form = new Form(owner, $"Занятие №{lesson.Id} · {lesson.Title}");
        form.Summary($"Статус: {SchoolText.Status(lesson.Status)}");
        form.Summary($"Ученик: {lesson.Student}  ·  {lesson.Phone}");
        form.Summary($"Место встречи: {lesson.Address}");
        form.Summary($"Инструктор: {lesson.Instructor ?? "—"}");
        form.Summary($"Сумма: {(lesson.Amount is { } amount ? amount.ToString("N2", Russian) + " ₽" : "—")}");
        form.Summary($"Время занятия: {lesson.ScheduledAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—"}");
        form.Summary($"Создан: {lesson.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        if (lesson.CompletedAt is { } completed)
            form.Summary($"Проведено: {completed.ToLocalTime():dd.MM.yyyy HH:mm}");
        if (!string.IsNullOrEmpty(lesson.ClosingReason))
            form.Summary($"Причина завершения: {lesson.ClosingReason}");
        if (!string.IsNullOrEmpty(lesson.Notes))
            form.Summary($"Примечания: {lesson.Notes}");
        form.Show();
    }

    public string? OpenCsv()
    {
        var dialog = new OpenFileDialog { Filter = "Файлы CSV (*.csv)|*.csv", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void ShowImportErrors(IReadOnlyList<CsvRowError> errors)
    {
        var form = new Form(owner, "Ошибки импорта CSV");
        form.Summary($"Записи не добавлены. Исправьте ошибки в файле ({errors.Count}).");
        form.Add("ImportErrorReport", "Строки с ошибками", new TextBox
        {
            IsReadOnly = true,
            MaxHeight = 360,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, errors.Select(row => $"{row.Line}: {row.Message}"))
        });
        form.Show();
    }

    public string? SaveExport(string format)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"lessons-{DateTime.Today:yyyy-MM-dd}.{format}",
            Filter = $"{format.ToUpperInvariant()} (*.{format})|*.{format}",
            DefaultExt = format,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private sealed class Form
    {
        private readonly Window window;
        private readonly StackPanel fields = new();
        private readonly StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock message = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(157, 44, 39)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 9)
        };

        public Form(Window owner, string title)
        {
            window = new Window
            {
                Owner = owner, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 540, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 40,
                ResizeMode = ResizeMode.NoResize, Style = (Style)Application.Current.FindResource(typeof(Window))
            };
            var layout = new DockPanel { Margin = new Thickness(22) };
            var buttons = new StackPanel();
            buttons.Children.Add(message);
            buttons.Children.Add(footer);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = layout;
        }

        public void Add(string id, string caption, Control control)
        {
            fields.Children.Add(new Label { Content = caption, Target = control, Padding = new Thickness(0, 7, 0, 4) });
            AutomationProperties.SetAutomationId(control, id);
            fields.Children.Add(control);
        }

        public TextBox Text(string id, string caption, string? value, int max, bool multiline = false)
        {
            var control = new TextBox
            {
                Text = value ?? "", MaxLength = max, AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            if (multiline)
            {
                control.MinHeight = 55;
                control.MaxHeight = 90;
                control.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            Add(id, caption, control);
            return control;
        }

        public void Summary(string value) =>
            fields.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8) });

        public T? Save<T>(string label, Func<T> value) where T : class
        {
            T? result = null;
            var cancel = new Button { Content = "Закрыть", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = label, IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
            save.Click += (_, _) =>
            {
                try
                {
                    result = value();
                    window.DialogResult = true;
                }
                catch (RuleException error)
                {
                    message.Text = error.Message;
                }
            };
            AutomationProperties.SetAutomationId(cancel, "FormCloseButton");
            AutomationProperties.SetAutomationId(save, "FormSaveButton");
            footer.Children.Add(cancel);
            footer.Children.Add(save);
            return window.ShowDialog() == true ? result : null;
        }

        public void Show()
        {
            var close = new Button { Content = "Закрыть", IsCancel = true, IsDefault = true };
            AutomationProperties.SetAutomationId(close, "FormCloseButton");
            footer.Children.Add(close);
            window.ShowDialog();
        }
    }
}
