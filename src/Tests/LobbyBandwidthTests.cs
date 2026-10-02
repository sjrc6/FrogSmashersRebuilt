using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void SteadyLobbyUsesCompactControls()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Bandwidth fixture did not synchronize");
        rig.Steps(240);
        rig.Sent.Clear();
        rig.Steps(600);
        Check(rig.Sent.All(packet => packet.Data[4] != 1), "An unchanged lobby kept sending JSON controls");
        var controls = rig.Sent.Where(packet => packet.Data[4] == 3).ToArray();
        Check(
            controls.Length > 0 && controls.All(packet => packet.Data.Length <= 28),
            "Routine lobby coordination exceeded its compact wire layout"
        );
        rig.AssertConfirmedStates();

        guest.Lobby.SetRollbackSettings(new() { Delay = 4, Donation = 2 });
        rig.WaitFor(
            () => host.Lobby.PeerRollbackSettings.GetValueOrDefault(guest.Lobby.LocalPeer)?.Delay == 4,
            "Changed preferences did not reach the host without periodic announcements"
        );
        rig.Steps(120);
        rig.Sent.Clear();
        rig.Steps(180);
        Check(rig.Sent.All(packet => packet.Data[4] != 1), "Acknowledged preferences kept generating JSON traffic");

        var third = rig.Add("third", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Compact mesh reports did not allow another admission");
        rig.Steps(120);
        rig.AssertConfirmedStates();
        Check(third.Lobby.Roster.Count == 3, "The compact heartbeat omitted an active peer");
    }

    private static void CompactControlValidation()
    {
        string nonce = Guid.NewGuid().ToString("N");
        foreach (
            var kind in new[]
            {
                MeshLobby.ControlKind.Heartbeat,
                MeshLobby.ControlKind.Mesh,
                MeshLobby.ControlKind.MeshAcknowledged,
            }
        )
        {
            var control = new MeshLobby.Control
            {
                Kind = kind,
                Nonce = nonce,
                Generation = 17,
                Peer = 11,
                Mesh = [0, 3, 11],
            };
            byte[] data = MeshLobby.EncodeControl(control);
            Check(MeshLobby.TryDecodeControl(data, out var decoded), "A compact control failed to decode");
            Check(decoded!.Kind == kind && decoded.Nonce == nonce, "A compact control changed its identity");
            if (kind == MeshLobby.ControlKind.Heartbeat)
                Check(
                    decoded.Generation == 17 && decoded.Mesh.SequenceEqual(control.Mesh),
                    "Heartbeat readiness mask lost peers"
                );
            else
                Check(decoded.Peer == 11, "Mesh probe lost its sender identity");
            for (int length = 0; length < data.Length; length++)
                Check(
                    !MeshLobby.TryDecodeControl(data.AsSpan(0, length), out _),
                    "Truncated compact control was accepted"
                );
            Check(!MeshLobby.TryDecodeControl([.. data, 0], out _), "Compact control accepted trailing data");
        }
        byte[] invalid = MeshLobby.EncodeControl(new() { Kind = MeshLobby.ControlKind.Heartbeat, Nonce = nonce });
        invalid[27] = 128;
        Check(!MeshLobby.TryDecodeControl(invalid, out _), "Heartbeat accepted a peer outside the roster bound");
        invalid = MeshLobby.EncodeControl(new() { Kind = MeshLobby.ControlKind.Mesh, Nonce = nonce });
        invalid[22] = 12;
        Check(!MeshLobby.TryDecodeControl(invalid, out _), "Mesh probe accepted a peer outside the roster bound");
    }
}
