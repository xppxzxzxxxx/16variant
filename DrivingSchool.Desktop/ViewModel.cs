using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrivingSchool.Core;

namespace DrivingSchool.Desktop;

internal abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Changed(property);
    }

    protected void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class Command(Action<object?> action, Func<bool>? allowed = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => allowed?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            action(parameter);
    }
}

internal abstract class AsyncViewModel : ViewModel
{
    private bool busy;
    private string message = "";
    public string Message { get => message; protected set => Set(ref message, value); }

    protected ICommand Run(Func<object?, Task> action, Func<bool>? allowed = null) =>
        new Command(async argument =>
        {
            busy = true;
            Message = "";
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await action(argument);
            }
            catch (Exception error) when (error is ClientException or RuleException)
            {
                ClientLog.Warning("Действие отклонено");
                Message = error.Message;
            }
            catch (Exception error)
            {
                ClientLog.Write(error);
                Message = "Действие не выполнено. Подробности сохранены в журнале.";
            }
            finally
            {
                busy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }, () => !busy && (allowed?.Invoke() ?? true));
}

internal sealed class LoginViewModel : AsyncViewModel
{
    public string Login { get; set; } = "";
    public ICommand SignIn { get; }

    public LoginViewModel(Task<SchoolClient> client, Action<SchoolClient, LoginResult> signedIn)
    {
        SignIn = Run(async argument =>
        {
            string password = (argument as Func<string>)?.Invoke() ?? "";
            if (string.IsNullOrWhiteSpace(Login) || password.Length == 0)
                throw new RuleException("Введите логин и пароль.");

            SchoolClient api;
            try
            {
                api = await client;
            }
            catch (Exception error)
            {
                ClientLog.Write(error);
                throw new ClientException("Локальный сервер не запустился. Проверьте журнал приложения.");
            }

            var result = await api.Login(Login, password);
            ClientLog.Info($"Начат сеанс пользователя {result.User.Id}, роль {result.User.Role}");
            signedIn(api, result);
        });
    }
}
