using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DrivingSchool.Core;
using Xunit;

namespace DrivingSchool.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("en-US", "1500.50")]
    [InlineData("en-US", "1500,50")]
    [InlineData("ru-RU", "1500.50")]
    [InlineData("ru-RU", "1500,50")]
    [InlineData("de-DE", "1500,50")]
    public void AmountIsIndependentOfRegion(string region, string text)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);
            Assert.Equal(1500.50m, InputRules.ParseAmount(text));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("1,500")]
    [InlineData("1 500")]
    [InlineData("1,500.50")]
    [InlineData("4,")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000000000")]
    public void UnclearOrOutOfRangeAmountIsRejected(string text) =>
        Assert.Throws<RuleException>(() => InputRules.ParseAmount(text));

    [Theory]
    [InlineData("29.02.2024 13:05")]
    [InlineData("2024-02-29 13:05")]
    public void ScheduleAcceptsBothDocumentedFormats(string text) =>
        Assert.Equal(new DateTime(2024, 2, 29, 13, 5, 0), InputRules.ParseScheduledAt(text)!.Value.DateTime);

    [Theory]
    [InlineData("29.02.2026")]
    [InlineData("02/03/2026")]
    [InlineData("2026-11-31")]
    public void InvalidOrAmbiguousScheduleIsRejected(string text) =>
        Assert.Throws<RuleException>(() => InputRules.ParseScheduledAt(text));

    [Theory]
    [InlineData(149, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    [InlineData(0, false)]
    public void StudentNameBoundary(int length, bool valid)
    {
        var input = new StudentInput(new string('А', length), "123", null);
        if (valid) Assert.Equal(input.Name, InputRules.Student(input).Name);
        else Assert.Throws<RuleException>(() => InputRules.Student(input));
    }

    [Theory]
    [InlineData(39, true)]
    [InlineData(40, true)]
    [InlineData(41, false)]
    [InlineData(0, false)]
    public void StudentPhoneBoundary(int length, bool valid)
    {
        var input = new StudentInput("Ученик", new string('1', length), null);
        if (valid) Assert.Equal(input.Phone, InputRules.Student(input).Phone);
        else Assert.Throws<RuleException>(() => InputRules.Student(input));
    }

    [Fact]
    public void StatisticsIgnoresInvalidDurationButKeepsAmountAndCount()
    {
        var start = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
        LessonMetrics[] lessons =
        [
            new(LessonStatus.Completed, 6.24m, start, start.AddDays(1.5)),
            new(LessonStatus.Completed, 3.01m, start, start.AddDays(-1)),
            new(LessonStatus.Completed, null, start, null),
            new(LessonStatus.Rescheduled, 2.75m, start, start.AddDays(15))
        ];
        var result = LessonRules.StatisticsFromMetrics(lessons);
        Assert.Equal(1.5, result.AverageDays);
        Assert.Equal(4, result.Total);
        Assert.Equal(3, result.Completed);
        Assert.Equal(9.25m, result.CompletedAmount);
        Assert.Equal(12.00m, result.TotalAmount);
        Assert.Equal(4, result.ByStatus.Sum(row => row.Count));
    }

    [Fact]
    public void CsvReportsPhysicalLineNumbersAfterBlankLines()
    {
        const string csv = "\nName;Phone;Email\n\nЗапись;100;\n\nОшибочная;;невозможно";
        var error = Assert.Throws<CsvImportException>(() => SchoolFiles.ReadStudents(csv));
        Assert.Equal(6, Assert.Single(error.Errors).Line);
    }

    [Fact]
    public void ExportTimestampsHaveUtcZone()
    {
        var start = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
        var row = LessonRulesTests.Row(LessonStatus.Completed, 10m, start, start.AddHours(6));
        using var json = JsonDocument.Parse(SchoolFiles.Export("json", [row]).Bytes);
        Assert.Equal("2026-11-08T03:00:00Z", json.RootElement[0].GetProperty("createdAt").GetString());
        var xml = XElement.Load(new MemoryStream(SchoolFiles.Export("xml", [row]).Bytes));
        Assert.EndsWith("Z", xml.Element("Lesson")!.Element("CompletedAt")!.Value, StringComparison.Ordinal);
    }
}
