using System.Diagnostics;
using System.IO;
using System.Text;

namespace DrivingSchool.Desktop;

internal static class ClientLog
{
    private static readonly object Gate = new();

    private static string DirectoryPath
    {
        get
        {
            string? database = Environment.GetEnvironmentVariable("School__Database");
            var directory = string.IsNullOrWhiteSpace(database)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrivingSchool")
                : Path.GetDirectoryName(Path.GetFullPath(database))!;
            return Path.Combine(directory, "logs");
        }
    }

    public static void Start()
    {
        try
        {
            var directory = Directory.CreateDirectory(DirectoryPath);
            var cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (var file in directory.EnumerateFiles("desktop-*.log")
                .Where(file => file.LastWriteTimeUtc < cutoff))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Write(error);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine(error);
        }
        Info("Запуск WPF-клиента");
    }

    public static void Info(string message) => Append("INF", message);
    public static void Warning(string message) => Append("WRN", message);
    public static void Write(Exception error) => Append("ERR", $"{error.GetType().Name}{Environment.NewLine}{error.StackTrace}");

    private static void Append(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(directory.FullName, $"desktop-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine(error);
        }
    }
}
