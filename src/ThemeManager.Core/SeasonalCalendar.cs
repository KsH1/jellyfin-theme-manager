using System.Globalization;

namespace ThemeManager.Core;

public static class SeasonalCalendar
{
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var n = h + l - (7 * m) + 114;
        return new DateOnly(year, n / 31, (n % 31) + 1);
    }

    public static DateOnly LocalDate(DateTimeOffset now, string zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);

    public static void Validate(CalendarRule rule)
    {
        if (rule.Priority is < -1000 or > 1000) throw new InvalidDataException("Calendar priority must be between -1000 and 1000.");
        switch (rule.Kind)
        {
            case "Month" when rule.Month is >= 1 and <= 12: return;
            case "Range":
                ParseMonthDay(rule.Start);
                ParseMonthDay(rule.End);
                return;
            case "EasterOffset" when rule.StartOffset >= -90 && rule.EndOffset <= 90 && rule.StartOffset <= rule.EndOffset: return;
            default: throw new InvalidDataException("Invalid calendar rule.");
        }
    }

    private static int ParseMonthDay(string? value)
    {
        if (value is null || value.Length != 5 || !DateOnly.TryParseExact("2000-" + value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException("Calendar dates must use MM-DD.");
        return (date.Month * 100) + date.Day;
    }

    public static bool Matches(CalendarRule rule, DateOnly date)
    {
        Validate(rule);
        if (rule.Kind == "Month") return date.Month == rule.Month;
        if (rule.Kind == "EasterOffset")
        {
            var delta = date.DayNumber - Easter(date.Year).DayNumber;
            return delta >= rule.StartOffset && delta <= rule.EndOffset;
        }

        var day = (date.Month * 100) + date.Day;
        var start = ParseMonthDay(rule.Start);
        var end = ParseMonthDay(rule.End);
        return start <= end ? day >= start && day <= end : day >= start || day <= end;
    }

    public static string? Resolve(IEnumerable<ResolvedTheme> themes, DateOnly date)
        => themes.Where(t => t.Manifest.Calendar is { } r && Matches(r, date))
            .OrderByDescending(t => t.Manifest.Calendar!.Priority)
            .ThenBy(t => t.Manifest.Id, StringComparer.Ordinal)
            .Select(t => t.Manifest.Id).FirstOrDefault();
}
