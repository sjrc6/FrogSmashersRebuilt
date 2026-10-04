namespace FrogSmashers.Network;

public sealed record NetworkCompatibility(string Protocol, string CodeHash, string GameplayHash)
{
    public const int MaximumLength = 192;
    public string Identifier => $"{Protocol}|{CodeHash}|{GameplayHash}";

    public static string? Rejection(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return null;
        string[] local = expected.Split('|');
        string[] remote = actual.Split('|');
        if (local.Length != 3 || remote.Length != 3 || local[0] != remote[0])
            return "NETWORK PROTOCOL MISMATCH";
        if (local[1] != remote[1])
            return "SIMULATION BUILD MISMATCH";
        return "GAMEPLAY DATA MISMATCH";
    }
}
