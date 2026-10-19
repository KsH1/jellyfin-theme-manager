using System.Text.RegularExpressions;

namespace ThemeManager.Core;

public static partial class SafePaths
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();

    public static bool IsId(string? value) => value is { Length: > 0 and <= 80 } && IdPattern().IsMatch(value);

    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\'))
            throw new InvalidDataException("An asset must be a relative pack path.");
        var parts = relative.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.TrimEnd(' ', '.') != p || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || p.Any(char.IsControl) || DeviceName().IsMatch(p)))
            throw new InvalidDataException("Invalid path segment.");
        var fullRoot = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Path escapes pack directory.");
        var current = fullRoot;
        RejectLink(current);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            RejectLink(current);
        }
        return result;
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Links and reparse points are not allowed in theme packs.");
    }
}
