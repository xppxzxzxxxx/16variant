using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Xunit;

namespace DrivingSchool.UiTests;

public sealed class LoginFlowTests
{
    [Theory]
    [InlineData("admin", "ADMIN", "Администратор · admin", true, true)]
    [InlineData("manager", "MANAGER", "Менеджер · manager", true, false)]
    [InlineData("nikita", "NIKITA", "Инструктор · Никита", false, false)]
    [InlineData("sergey", "SERGEY", "Инструктор · Сергей", false, false)]
    public void LoginShowsRoleAndLogoutReturnsToLogin(string login, string account,
        string caption, bool staff, bool admin)
    {
        using var run = new DesktopRun();
        var window = run.Window("Вход — Автошкола");
        var password = Environment.GetEnvironmentVariable($"SCHOOL_DEMO_{account}_PASSWORD")
            ?? $"Auto16-{login}!";
        SetCredentials(window, login, password);
        Invoke(window, "SignInButton");

        var workspace = run.Window("Автошкола");
        Assert.Equal(caption, Element(workspace, "SessionCaption").Name);
        Assert.Equal(staff, Visible(workspace, "NewLessonButton"));
        Assert.Equal(staff, Visible(workspace, "StudentsTab"));
        Assert.Equal(admin, Visible(workspace, "UsersTab"));
        Assert.True(Visible(workspace, "LessonsTab"));
        Assert.True(Visible(workspace, "StatisticsTab"));
        Assert.True(Visible(workspace, "LessonSearchInput"));
        Assert.True(Visible(workspace, "LessonStatusFilter"));
        Assert.True(Visible(workspace, "RefreshLessonsButton"));

        Invoke(workspace, "LogoutButton");
        var returned = run.Window("Вход — Автошкола");
        Assert.Empty(Element(returned, "LoginInput").AsTextBox().Text);
        Assert.True(Visible(returned, "SignInButton"));
    }

    [Fact]
    public void WrongPasswordKeepsLoginOpen()
    {
        using var run = new DesktopRun();
        var window = run.Window("Вход — Автошкола");
        SetCredentials(window, "manager", "wrong-password");
        Invoke(window, "SignInButton");

        Assert.True(SpinWait.SpinUntil(
            () => Element(window, "LoginMessage").Name == "Неверный логин или пароль.",
            TimeSpan.FromSeconds(18)), "Окно входа не показало сообщение о неверном пароле.");
        Assert.True(Visible(window, "SignInButton"));
        Assert.True(run.LogRotationVerified());
    }

    [Theory]
    [InlineData("ГОРОДСКОЙ", "Все статусы", 1)]
    [InlineData("", "Проведено", 2)]
    public void SearchAndStatusChangeNumberOfVisibleLessons(string search, string status, int expected)
    {
        using var run = new DesktopRun(seedDemo: true);
        var login = run.Window("Вход — Автошкола");
        var password = Environment.GetEnvironmentVariable("SCHOOL_DEMO_MANAGER_PASSWORD") ??
            "Auto16-manager!";
        SetCredentials(login, "manager", password);
        Invoke(login, "SignInButton");
        var workspace = run.Window("Автошкола");
        WaitForLessonCount(workspace, 7);
        Element(workspace, "LessonSearchInput").AsTextBox().Text = search;
        var filter = Element(workspace, "LessonStatusFilter");
        var choice = filter.FindFirstDescendant(condition => condition.ByName(status));
        Assert.NotNull(choice);
        choice.Patterns.SelectionItem.Pattern.Select();
        Assert.True(choice.Patterns.SelectionItem.Pattern.IsSelected.Value);
        Invoke(workspace, "RefreshLessonsButton");
        WaitForLessonCount(workspace, expected);
    }

    private static void WaitForLessonCount(Window window, int expected)
    {
        var count = Element(window, "LessonCount");
        Assert.True(SpinWait.SpinUntil(() => count.Name == $"Занятий в списке: {expected}",
            TimeSpan.FromSeconds(18)), $"Ожидается {expected} занятий, получено «{count.Name}».");
    }

    private static void SetCredentials(Window window, string login, string password)
    {
        Element(window, "LoginInput").AsTextBox().Text = login;
        var field = Element(window, "PasswordInput");
        try
        {
            field.Patterns.Value.Pattern.SetValue(password);
        }
        catch (Exception error) when (error is FlaUI.Core.Exceptions.PatternNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            window.Focus();
            field.Focus();
            Keyboard.Type(password);
        }
    }

    private static void Invoke(Window window, string id)
    {
        var element = Element(window, id);
        Assert.True(SpinWait.SpinUntil(() => element.IsEnabled, TimeSpan.FromSeconds(10)));
        element.AsButton().Invoke();
    }

    private static AutomationElement Element(Window window, string id) =>
        window.FindFirstDescendant(condition => condition.ByAutomationId(id)) ??
        throw new InvalidOperationException($"Элемент UIA '{id}' отсутствует.");

    private static bool Visible(Window window, string id) =>
        window.FindFirstDescendant(condition => condition.ByAutomationId(id)) is { IsOffscreen: false };

    private sealed class DesktopRun : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "DrivingSchool.UiTests", Guid.NewGuid().ToString("N"));
        private readonly UIA3Automation automation = new();
        private readonly Process process;

        public DesktopRun(bool seedDemo = false)
        {
            var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
            var root = testOutput;
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "DrivingSchool.slnx")))
                root = root.Parent;
            var executable = Environment.GetEnvironmentVariable("SCHOOL_UI_EXECUTABLE") ??
                Path.Combine(root?.FullName ?? throw new InvalidOperationException("Решение не найдено."),
                "DrivingSchool.Desktop", "bin", testOutput.Parent!.Name, "net10.0-windows",
                "DrivingSchool.Desktop.exe");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            start.Environment["School__Database"] = Path.Combine(directory, "ui.db");
            start.Environment["School__SeedDemo"] = seedDemo ? "true" : "false";
            var logs = Path.Combine(directory, "logs");
            Directory.CreateDirectory(logs);
            WriteOldLog(logs, "desktop-20200101.log");
            WriteOldLog(logs, "api-20200101.log");
            process = Process.Start(start) ?? throw new InvalidOperationException("WPF не запустился.");
        }

        private static void WriteOldLog(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "Старый журнал");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-15));
        }

        public bool LogRotationVerified() =>
            !File.Exists(Path.Combine(directory, "logs", "desktop-20200101.log")) &&
            File.Exists(Path.Combine(directory, "logs", "api-20200101.log")) &&
            ReadLog().Contains("[WRN] Действие отклонено", StringComparison.Ordinal);

        public Window Window(string title)
        {
            Window? found = null;
            bool ready = SpinWait.SpinUntil(() =>
            {
                if (process.HasExited)
                    throw new InvalidOperationException($"WPF завершился с кодом {process.ExitCode}.");
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero || process.MainWindowTitle != title)
                    return false;
                try
                {
                    found = automation.FromHandle(handle).AsWindow();
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(25));
            if (!ready)
                throw new InvalidOperationException($"Окно «{title}» не найдено. {ReadLog()}");
            return found!;
        }

        private string ReadLog()
        {
            var logs = Path.Combine(directory, "logs");
            return Directory.Exists(logs)
                ? string.Join(Environment.NewLine, Directory.EnumerateFiles(logs).Select(ReadShared))
                : "";
        }

        private static string ReadShared(string path)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            if (!process.HasExited && (!process.CloseMainWindow() || !process.WaitForExit(5000)))
            {
                process.Kill();
                process.WaitForExit();
            }
            automation.Dispose();
            process.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
