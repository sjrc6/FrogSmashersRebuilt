using System.Security.Cryptography;

namespace FrogSmashers.Network;

public enum LobbyPrivacy
{
    Private,
    Friends,
    Public,
    PrivateCode,
}

internal sealed class LobbyAdmissionPolicy
{
    private readonly HashSet<ulong> grants = new();
    public LobbyPrivacy Privacy { get; }
    public string Secret { get; private set; }

    public LobbyAdmissionPolicy(LobbyPrivacy privacy)
    {
        if (!Enum.IsDefined(privacy))
            throw new ArgumentOutOfRangeException(nameof(privacy));
        Privacy = privacy;
        Secret = privacy == LobbyPrivacy.PrivateCode ? NewSecret() : "";
    }

    public bool Allows(ulong verifiedSteamId, string secret)
    {
        if (verifiedSteamId == 0)
            return false;
        if (Privacy != LobbyPrivacy.PrivateCode || grants.Contains(verifiedSteamId))
            return true;
        if (secret.Length != Secret.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(Secret)
        );
    }

    public bool Grant(ulong steamId)
    {
        if (steamId == 0)
            throw new ArgumentOutOfRangeException(nameof(steamId));
        return grants.Add(steamId);
    }

    public void Revoke(ulong steamId) => grants.Remove(steamId);

    public void Rotate()
    {
        if (Privacy != LobbyPrivacy.PrivateCode)
            throw new InvalidOperationException("Only code-protected lobbies have a secret");
        Secret = NewSecret();
        grants.Clear();
    }

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
