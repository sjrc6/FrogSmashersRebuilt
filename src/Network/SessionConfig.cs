using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class SessionConfig
{
    public int[][] PeerSlots { get; }
    public int[] ActivePeers { get; }
    public int LocalPeer { get; }
    public ulong Generation { get; }
    public int InputDelay { get; }
    public int MaxPrediction { get; }
    public int HistoryFrames { get; }
    public IReadOnlyDictionary<int, RollbackInput[]> InitialInputs { get; }
    public byte[] Fingerprint { get; }
    public int PlayerCount => PeerSlots.Sum(x => x.Length);

    public SessionConfig(
        int[][] peerSlots,
        int localPeer,
        string contentHash,
        World initialWorld,
        ulong generation,
        int inputDelay = 2,
        int maxPrediction = 24,
        int historyFrames = 360,
        int[]? activePeers = null,
        IReadOnlyDictionary<int, RollbackInput[]>? initialInputs = null
    )
    {
        ActivePeers = (activePeers ?? Enumerable.Range(0, peerSlots.Length).ToArray()).Order().ToArray();
        if (
            generation == 0
            || peerSlots.Length is < 1 or > LobbyRoster.MaxPeers
            || localPeer < 0
            || localPeer >= peerSlots.Length
            || ActivePeers.Length == 0
            || ActivePeers.Distinct().Count() != ActivePeers.Length
            || ActivePeers.Any(peer => peer < 0 || peer >= peerSlots.Length)
            || !ActivePeers.Contains(0)
            || !ActivePeers.Contains(localPeer)
            || ActivePeers.Count(peer => peerSlots[peer].Length == 0) > LobbyRoster.MaxSpectators
            || peerSlots.Where((_, peer) => !ActivePeers.Contains(peer)).Any(slots => slots.Length != 0)
        )
            throw new ArgumentException("Invalid session generation or peer roster");

        var slots = peerSlots.SelectMany(x => x).Order().ToArray();
        if (slots.Length is < 1 or > 8 || !slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
            throw new ArgumentException("Roster must own every player input exactly once");
        if (inputDelay is < 0 or > 12 || maxPrediction < 2 || historyFrames < maxPrediction + inputDelay + 64)
            throw new ArgumentException("Invalid rollback window");

        PeerSlots = peerSlots.Select(x => x.Order().ToArray()).ToArray();
        LocalPeer = localPeer;
        Generation = generation;
        InputDelay = inputDelay;
        MaxPrediction = maxPrediction;
        HistoryFrames = historyFrames;
        InitialInputs =
            initialInputs?.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())
            ?? new Dictionary<int, RollbackInput[]>();
        if (InitialInputs.Any(pair => !slots.Contains(pair.Key) || pair.Value.Length != inputDelay))
            throw new ArgumentException("Initial inputs must fill the configured delay for an existing handle");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("FrogSmashersRebuilt.GGCS.GameplayAndLobby.1");
        writer.Write(NetworkBuild.ContentFingerprint(contentHash));
        writer.Write(peerSlots.Length);
        foreach (var peer in PeerSlots)
        {
            writer.Write(peer.Length);
            foreach (int slot in peer)
                writer.Write(slot);
        }
        writer.Write(ActivePeers.Length);
        foreach (int peer in ActivePeers)
            writer.Write(peer);
        writer.Write(initialWorld.Capture());
        writer.Flush();
        Fingerprint = SHA256.HashData(stream.ToArray());
    }
}
