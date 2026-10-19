namespace ThemeManager.Core;

/// <summary>Which standard holidays take part in Automatic Seasonal when an administrator has not chosen.</summary>
public static class RegionalDefaults
{
    /// <summary>Holidays celebrated mainly in Denmark. Valentine's Day, Easter, Halloween, Christmas and New Year are general.</summary>
    public static readonly IReadOnlyList<string> DanishHolidays = ["fastelavn", "sankthans"];

    /// <summary>Danish servers keep every holiday; elsewhere the Danish ones are left out.</summary>
    public static IReadOnlyList<string> Excluded(string? timeZoneId, string? regionCode)
        => timeZoneId == "Europe/Copenhagen" || string.Equals(regionCode, "DK", StringComparison.OrdinalIgnoreCase) ? [] : DanishHolidays;
}
