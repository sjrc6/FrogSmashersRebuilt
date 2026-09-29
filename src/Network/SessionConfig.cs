using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class SessionConfig
{
    public int[][] PeerSlots { get; }
    public int LocalPeer { get; }
    public int InputDelay { get; }
    public int MaxPrediction { get; }
    public int HistoryFrames { get; }
    public byte[] Fingerprint { get; }
    public int PlayerCount => PeerSlots.Sum(x => x.Length);

    public SessionConfig(
        int[][] peerSlots,
        int localPeer,
        string contentHash,
        World initialWorld,
        int inputDelay = 2,
        int maxPrediction = 24,
        int historyFrames = 360
    )
    {
        if (
            peerSlots.Length is < 1 or > 8
            || localPeer < 0
            || localPeer >= peerSlots.Length
            || peerSlots.Any(x => x.Length == 0)
        )
        {
            throw new ArgumentException("Invalid peer roster");
        }

        var slots = peerSlots.SelectMany(x => x).Order().ToArray();
        if (slots.Length is < 2 or > 8 || !slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
        {
            throw new ArgumentException("Roster must own every player slot exactly once");
        }

        if (inputDelay is < 0 or > 12 || maxPrediction < 2 || historyFrames < maxPrediction + inputDelay + 64)
        {
            throw new ArgumentException("Invalid rollback window");
        }

        PeerSlots = peerSlots.Select(x => x.ToArray()).ToArray();
        LocalPeer = localPeer;
        InputDelay = inputDelay;
        MaxPrediction = maxPrediction;
        HistoryFrames = historyFrames;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("FrogSmashersRebuilt.Protocol.2");
        writer.Write(NetworkBuild.ContentFingerprint(contentHash));
        writer.Write(inputDelay);
        writer.Write(peerSlots.Length);
        foreach (var peer in peerSlots)
        {
            writer.Write(peer.Length);
            foreach (var slot in peer)
            {
                writer.Write(slot);
            }
        }

        writer.Write(initialWorld.Capture());
        writer.Flush();
        Fingerprint = SHA256.HashData(stream.ToArray());
    }
}
