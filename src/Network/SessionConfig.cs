using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class SessionConfig
{
    public const int MaxInputs = LobbyRoster.MaxPlayers + 1;
    public int[][] PeerSlots { get; }
    public int[] InputPlayerSlots { get; }
    public int[] ActivePeers { get; }
    public int LocalPeer { get; }
    public ulong Generation { get; }
    public RollbackPreferences Rollback { get; }
    public int MaxPrediction { get; }
    public int HistoryFrames { get; }
    public IReadOnlyDictionary<int, RollbackInput[]> InitialInputs { get; }
    public byte[] Fingerprint { get; }
    public int InputCount => InputPlayerSlots.Length;

    public SessionConfig(
        int[][] peerSlots,
        int localPeer,
        string contentHash,
        World initialWorld,
        ulong generation,
        int maxPrediction = RollbackPreferences.PredictionFrames,
        int historyFrames = 360,
        int[]? activePeers = null,
        IReadOnlyDictionary<int, RollbackInput[]>? initialInputs = null,
        RollbackPreferences? rollback = null,
        int[]? inputPlayerSlots = null
    )
    {
        ActivePeers = (activePeers ?? Enumerable.Range(0, peerSlots.Length).ToArray()).Order().ToArray();
        if (
            generation == 0
            || peerSlots.Length is < 1 or > LobbyRoster.MaxPeers
            || peerSlots[0].Length == 0
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
        if (slots.Length is < 1 or > MaxInputs || !slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
            throw new ArgumentException("Roster must own every player input exactly once");
        InputPlayerSlots = inputPlayerSlots?.ToArray() ?? slots.ToArray();
        int[] bodies = InputPlayerSlots.Where(slot => slot >= 0).ToArray();
        if (
            InputPlayerSlots.Length != slots.Length
            || bodies.Any(slot => slot >= initialWorld.Players.Length || initialWorld.Rules.CpuPlayers[slot])
            || bodies.Distinct().Count() != bodies.Length
            || InputPlayerSlots.Count(slot => slot == -1) > 1
            || InputPlayerSlots
                .Where((slot, handle) => slot < 0 && (slot != -1 || handle != 0 || !peerSlots[0].Contains(handle)))
                .Any()
        )
            throw new ArgumentException("Invalid mapping from input streams to frogs");
        PeerSlots = peerSlots.Select(x => x.Order().ToArray()).ToArray();
        LocalPeer = localPeer;
        Generation = generation;
        Rollback = rollback ?? new();
        if (!Rollback.IsValid || Rollback.Donation > maxPrediction)
            throw new ArgumentException("Invalid rollback preferences");
        if (maxPrediction < 2 || historyFrames < maxPrediction + Rollback.Delay + Rollback.MaxExtraDelay + 64)
            throw new ArgumentException("Invalid rollback window");
        MaxPrediction = maxPrediction;
        HistoryFrames = historyFrames;
        InitialInputs =
            initialInputs?.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())
            ?? new Dictionary<int, RollbackInput[]>();
        if (
            InitialInputs.Any(pair =>
                !slots.Contains(pair.Key) || pair.Value.Length > RollbackPreferences.InputCapacityFrames
            )
        )
            throw new ArgumentException(
                "Initial inputs must belong to an existing handle and fit the delayed-input capacity"
            );
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
        foreach (int slot in InputPlayerSlots)
            writer.Write(slot);
        writer.Write(ActivePeers.Length);
        foreach (int peer in ActivePeers)
            writer.Write(peer);
        writer.Write(initialWorld.Capture());
        writer.Flush();
        Fingerprint = SHA256.HashData(stream.ToArray());
    }
}
