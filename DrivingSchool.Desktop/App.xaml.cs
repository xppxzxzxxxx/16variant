using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using DrivingSchool.Api;
using Microsoft.AspNetCore.Builder;

namespace DrivingSchool.Desktop;

public partial class App : Application
{
    private WebApplication? host;
    private HttpClient? http;
    private Task<SchoolClient>? client;
    private X509Certificate2? certificate;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ClientLog.Start();
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU")));
        client = Task.Run(StartClient);
        ShowLogin();
    }

    private async Task<SchoolClient> StartClient()
    {
        certificate = LoopbackCertificate.Create();
        var expected = certificate.GetCertHash(HashAlgorithmName.SHA256);
        host = SchoolHost.Build([], certificate);
        await SchoolHost.Initialize(host, embedded: true);
        await host.StartAsync();
        var transport = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (request, presented, _, _) =>
                request.RequestUri is { Scheme: "https", Host: "127.0.0.1" } &&
                presented is not null &&
                CryptographicOperations.FixedTimeEquals(expected, presented.GetCertHash(HashAlgorithmName.SHA256))
        };
        http = new HttpClient(transport)
        {
            BaseAddress = new Uri(host.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(12)
        };
        return new SchoolClient(http);
    }

    private void ShowLogin()
    {
        var model = new LoginViewModel(client!, ShowWorkspace);
        SwitchTo(new LoginWindow { DataContext = model });
    }

    private void ShowWorkspace(SchoolClient api, Core.LoginResult login)
    {
        var shell = new WorkspaceWindow();
        var model = new WorkspaceViewModel(api, login, new SchoolDialogs(shell), ShowLogin);
        shell.DataContext = model;
        SwitchTo(shell);
        model.RefreshLessons.Execute(null);
    }

    private void SwitchTo(Window next)
    {
        var previous = MainWindow;
        MainWindow = next;
        next.Show();
        if (previous is not null && previous != next)
            previous.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (client is not null)
                Task.WhenAny(client, Task.Delay(1500)).GetAwaiter().GetResult();
            if (client?.IsCompleted == true && host is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                host.StopAsync(timeout.Token).GetAwaiter().GetResult();
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception error)
        {
            ClientLog.Write(error);
        }
        finally
        {
            http?.Dispose();
            if (client?.IsCompleted == true)
                certificate?.Dispose();
            ClientLog.Info("Завершение работы WPF-клиента");
            base.OnExit(e);
        }
    }
}
