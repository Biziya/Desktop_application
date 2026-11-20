namespace Frontend.Calculation.CoreInformation;

/// <summary>
/// CT007 probes report names such as "CT007-M", "CT007-T" or "CT007-M-0001". Everything after "CT007" is a
/// model/serial suffix, so names are normalised to "CT007" before they are matched.
/// </summary>
public static class DeviceName
{
    public const string Family = "CT007";

    /// <summary>"CT007-M-0001" → "CT007"; other names are only trimmed.</summary>
    public static string Normalize(string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.StartsWith(Family, StringComparison.OrdinalIgnoreCase) ? Family : trimmed;
    }

    /// <summary>True when <paramref name="name"/> matches the configured prefix after both are normalised.</summary>
    public static bool Matches(string? name, string prefix)
    {
        var expected = Normalize(prefix);
        if (expected.Length == 0)
        {
            return false;
        }

        var actual = Normalize(name);
        return expected == Family
            ? string.Equals(actual, Family, StringComparison.OrdinalIgnoreCase)
            : actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
    }
}
