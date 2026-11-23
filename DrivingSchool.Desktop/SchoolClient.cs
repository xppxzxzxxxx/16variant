using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrivingSchool.Core;

namespace DrivingSchool.Desktop;

internal sealed class SchoolClient(HttpClient http)
{
    private string? token;

    public async Task<LoginResult> Login(string login, string password)
    {
        var result = await Json<LoginResult>(HttpMethod.Post, "api/auth/login", new LoginInput(login, password));
        token = result.Token;
        return result;
    }

    public async Task Logout()
    {
        try
        {
            await Send(HttpMethod.Post, "api/auth/logout");
        }
        finally
        {
            token = null;
        }
    }

    public Task<LessonInfo[]> Lessons(LessonStatus? status, string? search) =>
        Json<LessonInfo[]>(HttpMethod.Get, Filter("api/lessons", status, search));

    public Task<StudentInfo[]> Students() => Json<StudentInfo[]>(HttpMethod.Get, "api/students");

    public Task<UserInfo[]> Users(bool instructorsOnly) =>
        Json<UserInfo[]>(HttpMethod.Get, instructorsOnly ? "api/users/instructors" : "api/users");

    public Task<SchoolStatistics> Statistics() =>
        Json<SchoolStatistics>(HttpMethod.Get, "api/lessons/statistics");

    public Task SaveLesson(int? id, LessonInput lesson) =>
        id.HasValue ? Send(HttpMethod.Put, $"api/lessons/{id}", lesson) : Send(HttpMethod.Post, "api/lessons", lesson);

    public Task SaveStudent(int? id, StudentInput student) =>
        id.HasValue ? Send(HttpMethod.Put, $"api/students/{id}", student) : Send(HttpMethod.Post, "api/students", student);

    public Task Transition(int id, TransitionInput input) =>
        Send(HttpMethod.Post, $"api/lessons/{id}/transition", input);

    public async Task<ImportResult> Import(byte[] csv)
    {
        using var message = new ByteArrayContent(csv);
        message.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
        using var response = await Request(HttpMethod.Post, "api/students/import", message);
        return (await response.Content.ReadFromJsonAsync<ImportResult>(SchoolFiles.JsonOptions))!;
    }

    public async Task<byte[]> Export(string format, LessonStatus? status, string? search)
    {
        using var response = await Request(HttpMethod.Get, Filter($"api/lessons/export/{format}", status, search));
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task Send(HttpMethod method, string path, object? input = null)
    {
        using var response = await Request(method, path, Content(input));
    }

    private async Task<T> Json<T>(HttpMethod method, string path, object? input = null)
    {
        using var response = await Request(method, path, Content(input));
        return await response.Content.ReadFromJsonAsync<T>(SchoolFiles.JsonOptions) ??
            throw new ClientException("Сервер вернул пустой ответ.");
    }

    private async Task<HttpResponseMessage> Request(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (TaskCanceledException error)
        {
            ClientLog.Write(error);
            throw new ClientException("Истекло время ожидания локального сервера. Повторите действие.");
        }
        catch (HttpRequestException error)
        {
            ClientLog.Write(error);
            throw new ClientException("Локальный сервер не ответил. Повторите действие.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            if (path == "api/students/import" && response.StatusCode == HttpStatusCode.BadRequest)
            {
                var report = await response.Content.ReadFromJsonAsync<CsvProblem>(SchoolFiles.JsonOptions);
                if (report?.Errors is { Length: > 0 } errors)
                    throw new CsvImportException(errors);
                throw new ClientException(report?.Detail ?? report?.Title ?? "Не удалось прочитать CSV.");
            }
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized when path == "api/auth/login" => "Неверный логин или пароль.",
                HttpStatusCode.Unauthorized => "Сеанс завершён. Выйдите и войдите заново.",
                HttpStatusCode.Forbidden => "У вашей роли нет доступа к этому действию.",
                HttpStatusCode.NotFound => "Запись недоступна. Обновите список.",
                HttpStatusCode.TooManyRequests => "Слишком много попыток. Повторите вход через минуту.",
                _ => await ReadProblem(response)
            };
            throw new ClientException(message);
        }
    }

    private static async Task<string> ReadProblem(HttpResponseMessage response)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { } message)
                return message;
            if (json.RootElement.TryGetProperty("title", out var title) && title.GetString() is { } summary)
                return summary;
        }
        catch (JsonException)
        {
            // Some framework responses have an empty body.
        }
        return $"Не удалось выполнить действие. Код ответа {(int)response.StatusCode}.";
    }

    private static HttpContent? Content(object? input) =>
        input is null ? null : JsonContent.Create(input, options: SchoolFiles.JsonOptions);

    private static string Filter(string path, LessonStatus? status, string? search)
    {
        return $"{path}?q={Uri.EscapeDataString(search?.Trim() ?? "")}" +
            (status is null ? "" : $"&status={status}");
    }

    private sealed record CsvProblem(string? Title, string? Detail, CsvRowError[]? Errors);
}

internal sealed class ClientException(string message) : Exception(message);
