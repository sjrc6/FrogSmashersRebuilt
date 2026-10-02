namespace GGCS.Tests;

internal static class ParticipationTests
{
    public static void Run()
    {
        var clock = new TestClock();
        var wire = new SimulatedNetwork(clock)
        {
            Latency = 200,
            Jitter = 12,
            Loss = .02,
        };
        Player[] players = [new(0, 0), new(1, 1)];
        var options = new SessionOptions
        {
            FramesPerSecond = 120,
            MaxPredictionFrames = 30,
            HistoryFrames = 360,
            SynchronizationRoundTrips = 1,
        };
        var topology = new SessionParticipation<int>(0, 1, input => (uint)input);
        var hostGame = new TestGame();
        var host = new P2PSession<int, ulong>(
            91,
            0,
            players,
            hostGame,
            new IntCodec(),
            wire.Endpoint(0),
            options,
            clock,
            participation: topology,
            connectedPeers: []
        );
        for (int frame = 0; frame < 180; frame++)
        {
            clock.NowMilliseconds += 8;
            Check.Equal(AdvanceStatus.Advanced, host.AdvanceFrame([1]), "Unoccupied streams cannot stall the lobby");
        }
        int start = host.ConfirmedFrame;
        Check.True(
            host.TryGetConfirmedState(start, out var state),
            "A running session can seed admission from history"
        );
        var guestGame = new TestGame { Value = state.State };
        host.ConnectPeer(1, start);
        var guest = new P2PSession<int, ulong>(
            91,
            1,
            players,
            guestGame,
            new IntCodec(),
            wire.Endpoint(1),
            options,
            clock,
            participation: topology,
            initialFrame: start,
            connectedPeers: [0]
        );
        int blocked = 0;
        for (int frame = 0; frame < 400; frame++)
        {
            clock.NowMilliseconds += 8;
            blocked += host.AdvanceFrame([1]) == AdvanceStatus.Advanced ? 0 : 1;
            guest.AdvanceFrame([guest.CurrentFrame * 17]);
            Healthy(host);
            Healthy(guest);
        }
        Check.Equal(0, blocked, "Background handshaking and catch-up must not pause existing players");
        for (int frame = 0; frame < 600; frame++)
        {
            clock.NowMilliseconds += 8;
            host.AdvanceFrame([3]);
            guest.AdvanceFrame([guest.CurrentFrame * 17]);
            Healthy(host);
            Healthy(guest);
        }
        int confirmed = Math.Min(host.ConfirmedFrame, guest.ConfirmedFrame) + 1;
        Check.True(confirmed > start + 500, "A newly activated stream advances and confirms");
        Check.Equal(
            hostGame.States[confirmed],
            guestGame.States[confirmed],
            "Activation repairs predictions into identical state"
        );
        for (int frame = 0; frame < 120; frame++)
        {
            clock.NowMilliseconds += 8;
            host.AdvanceFrame([1]);
            guest.AdvanceFrame([0]);
        }
        int before = host.CurrentFrame;
        wire.Block = (_, _, _) => true;
        for (int frame = 0; frame < 120; frame++)
        {
            clock.NowMilliseconds += 8;
            host.AdvanceFrame([1]);
        }
        Check.Equal(
            before + 120,
            host.CurrentFrame,
            "An inactive participant no longer contributes prediction pressure"
        );
        Console.WriteLine(
            "Live participation: background admission, activation rollback and deactivation at 400 ms RTT passed"
        );
    }

    private static void Healthy(P2PSession<int, ulong> session)
    {
        while (session.TryGetEvent(out var item))
            Check.True(
                item.Kind is not (SessionEventKind.ProtocolError or SessionEventKind.DesyncDetected),
                item.ToString()
            );
    }
}
