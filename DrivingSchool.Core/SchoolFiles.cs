using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace DrivingSchool.Core;

public static class SchoolFiles
{
    public const int MaxImportBytes = 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static IReadOnlyList<StudentInput> ReadStudents(string csv)
    {
        csv = csv.TrimStart('\uFEFF');
        using var parser = new TextFieldParser(new StringReader(csv));
        var physicalLines = csv.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var headerLine = physicalLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        parser.SetDelimiters(headerLine?.Contains(';') == true ? ";" : ",");
        var students = new List<StudentInput>();
        var errors = new List<CsvRowError>();
        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(new[] { "Name", "Phone", "Email" }, StringComparer.OrdinalIgnoreCase))
                throw new RuleException("Заголовок CSV должен содержать Name,Phone,Email.");
        }
        catch (MalformedLineException error)
        {
            throw new CsvImportException([new CsvRowError(error.LineNumber, "Некорректный заголовок CSV.")]);
        }

        int records = 0;
        while (!parser.EndOfData)
        {
            if (++records > 5000)
                throw new RuleException("Один импорт допускает до 5000 учеников.");

            long line = parser.LineNumber;
            while (line > 0 && line <= physicalLines.Length && string.IsNullOrWhiteSpace(physicalLines[line - 1]))
                line++;
            try
            {
                var fields = parser.ReadFields()!;
                if (fields.Length != 3)
                    throw new RuleException("Ожидаются три поля.");
                students.Add(InputRules.Student(new StudentInput(fields[0], fields[1], fields[2])));
            }
            catch (RuleException error)
            {
                errors.Add(new CsvRowError(line, error.Message));
            }
            catch (MalformedLineException error)
            {
                errors.Add(new CsvRowError(error.LineNumber, "Некорректный CSV."));
            }
        }
        if (errors.Count != 0)
            throw new CsvImportException(errors);
        if (students.Count == 0)
            throw new RuleException("В CSV отсутствуют ученики.");
        return students;
    }

    public static (byte[] Bytes, string ContentType) Export(string format, IReadOnlyList<LessonInfo> lessons)
    {
        if (format == "json")
            return (JsonSerializer.SerializeToUtf8Bytes(lessons, JsonOptions), "application/json");

        string[] columns = ["Id", "StudentId", "Student", "Phone", "Title", "Address", "Notes",
            "InstructorId", "Instructor", "Status", "Amount", "ScheduledAt", "ClosingReason",
            "CreatedAt", "CompletedAt", "Version"];
        var rows = lessons.Select(Row);

        if (format == "xml")
        {
            var root = new XElement("Lessons", rows.Select(row =>
                new XElement("Lesson", columns.Zip(row, (column, value) => new XElement(column, value)))));
            return (Encoding.UTF8.GetBytes(root.ToString()), "application/xml");
        }

        if (format != "csv")
            throw new RuleException("Доступные форматы выгрузки: CSV, JSON и XML.");

        var csv = new StringBuilder("\uFEFF").AppendJoin(';', columns).Append("\r\n");
        foreach (var row in rows)
            csv.AppendJoin(';', row.Select((value, index) => ForCsv(value, index))).Append("\r\n");
        return (Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8");
    }

    private static string?[] Row(LessonInfo lesson) =>
    [
        Invariant(lesson.Id), Invariant(lesson.StudentId), lesson.Student, lesson.Phone,
        lesson.Title, lesson.Address, lesson.Notes, Invariant(lesson.InstructorId), lesson.Instructor,
        lesson.Status.ToString(), Invariant(lesson.Amount), lesson.ScheduledAt?.ToString("O", CultureInfo.InvariantCulture),
        lesson.ClosingReason, lesson.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        lesson.CompletedAt?.ToString("O", CultureInfo.InvariantCulture), Invariant(lesson.Version)
    ];

    private static string? Invariant(IFormattable? value) => value?.ToString(null, CultureInfo.InvariantCulture);

    private static string ForCsv(string? value, int index)
    {
        value ??= "";
        if (index is 2 or 3 or 4 or 5 or 6 or 8 or 12 &&
            value.TrimStart() is ['=' or '+' or '-' or '@', ..])
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
