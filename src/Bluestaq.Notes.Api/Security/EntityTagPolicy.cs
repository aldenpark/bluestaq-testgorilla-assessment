namespace Bluestaq.Notes.Api.Security;

/// <summary>Reads and writes strong integer entity tags used by optimistic HTTP preconditions.</summary>
public static class EntityTagPolicy
{
    /// <summary>Formats an integer version as a strong HTTP entity tag.</summary>
    public static string Format(int version) => $"\"{version}\"";

    /// <summary>Parses the single strong integer entity tag supported by mutation endpoints.</summary>
    public static bool TryParse(string value, out int version)
    {
        version = 0;
        return value.Length >= 3
            && value[0] == '"'
            && value[^1] == '"'
            && int.TryParse(value[1..^1], out version)
            && version > 0;
    }
}
