namespace FrogSmashers.Network;

public sealed record SteamFriend(ulong Id, string Name, bool Online);

public sealed record SteamAvatar(int Image, int Width, int Height, byte[] Pixels);
