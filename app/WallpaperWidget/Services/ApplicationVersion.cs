using System.Reflection;

namespace WallpaperWidget.Services;

public static class ApplicationVersion
{
    public static string Display { get; } = ResolveDisplayVersion();
    public static SemanticVersion Parsed { get; } = SemanticVersion.TryParse(Display, out var version)
        ? version
        : new SemanticVersion(0, 0, 0, null);

    private static string ResolveDisplayVersion()
    {
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}

public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimStart('v', 'V').Split('+')[0];
        var parts = normalized.Split('-', 2);
        var core = parts[0].Split('.');
        var patch = 0;
        if (core.Length is < 2 or > 4 ||
            !int.TryParse(core[0], out var major) ||
            !int.TryParse(core[1], out var minor) ||
            (core.Length > 2 && !int.TryParse(core[2], out patch))) return false;
        var prerelease = parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null;
        version = new SemanticVersion(major, minor, core.Length > 2 ? patch : 0, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            var leftNumeric = int.TryParse(left[index], out var leftNumber);
            var rightNumeric = int.TryParse(right[index], out var rightNumber);
            int comparison;
            if (leftNumeric && rightNumeric) comparison = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric) comparison = -1;
            else if (rightNumeric) comparison = 1;
            else comparison = string.Compare(left[index], right[index], StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Prerelease is null ? string.Empty : $"-{Prerelease}")}";
}
