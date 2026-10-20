namespace PcImpressz.Core;

internal static class Identifiers
{
    public static bool IsValid(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');

    public static void Require(string? value, string name)
    {
        if (!IsValid(value))
            throw new ArgumentException($"Invalid {name}: use lowercase letters, digits, '.' or '-'.");
    }
}
