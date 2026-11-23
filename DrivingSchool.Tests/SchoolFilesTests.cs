using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DrivingSchool.Core;
using Xunit;

namespace DrivingSchool.Tests;

public sealed class SchoolFilesTests
{
    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    public void ReadsQuotedCsvAndUtf8Bom(char separator)
    {
        string input = $"\uFEFFName{separator}Phone{separator}Email\r\n\"Петрова, Елена; группа 16\"{separator}+79990001234{separator}test@example.com";
        var student = Assert.Single(SchoolFiles.ReadStudents(input));
        Assert.Equal("Петрова, Елена; группа 16", student.Name);
    }

    [Theory]
    [InlineData("Wrong,Phone,Email\nПетрова,123,")]
    [InlineData("Name,Phone,Email\nПетрова,,")]
    [InlineData("Name,Phone,Email\nПетрова,123,bad-email")]
    [InlineData("Name,Phone,Email\n\"незакрытая кавычка,123,")]
    [InlineData("Name,Phone,Email")]
    public void RejectsInvalidCsv(string input) =>
        Assert.ThrowsAny<RuleException>(() => SchoolFiles.ReadStudents(input));

    [Fact]
    public void ExportsThreeReadableFormats()
    {
        var row = LessonRulesTests.Row(LessonStatus.Confirmed, 4200.5m, DateTime.UnixEpoch, null);
        Assert.Equal(row, Assert.Single(JsonSerializer.Deserialize<LessonInfo[]>(
            SchoolFiles.Export("json", [row]).Bytes, SchoolFiles.JsonOptions)!));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(SchoolFiles.Export("xml", [row]).Bytes));
        Assert.Equal(row.Title, xml.Root!.Element("Lesson")!.Element("Title")!.Value);
        var csv = Encoding.UTF8.GetString(SchoolFiles.Export("csv", [row]).Bytes);
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("\"Маршрут \"\"Город\"\" <центр>\"", csv);
        Assert.Contains("\"'+79990001234\"", csv);
        Assert.Contains("\"4200.5\"", csv);
    }
}
