using System.Text;
using FrogSmashers.Network;
using Steamworks;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void PrivateLobbyAdmission()
    {
        var ordinary = new LobbyAdmissionPolicy(LobbyPrivacy.Private);
        Check(ordinary.Secret == "" && ordinary.Allows(2, ""), "Ordinary private lobbies require only their ID");
        var policy = new LobbyAdmissionPolicy(LobbyPrivacy.PrivateCode);
        string original = policy.Secret;
        Check(
            LobbyAdmissionPolicy.CanShare(original, policy.CodeFingerprint)
                && !LobbyAdmissionPolicy.CanShare("", policy.CodeFingerprint)
                && !LobbyAdmissionPolicy.CanShare(new string('0', 64), policy.CodeFingerprint),
            "Only a known valid secret enables guest sharing; invitation grants do not disclose it"
        );
        Check(
            original.Length == 64 && original != new LobbyAdmissionPolicy(LobbyPrivacy.PrivateCode).Secret,
            "Code lobbies generate independent 256-bit secrets"
        );
        Check(
            !policy.Allows(2, "") && !policy.Allows(2, new string('0', 64)) && !policy.Allows(0, original),
            "A code lobby requires the secret or a grant to a verified peer identity"
        );
        Check(
            SteamLobby.Visibility(LobbyPrivacy.PrivateCode) == ELobbyType.k_ELobbyTypePrivate
                && SteamLobby.Visibility(LobbyPrivacy.Private) == ELobbyType.k_ELobbyTypePrivate
                && SteamLobby.Visibility(LobbyPrivacy.Public) == ELobbyType.k_ELobbyTypePublic
                && SteamLobby.Visibility(LobbyPrivacy.Friends) == ELobbyType.k_ELobbyTypeFriendsOnly,
            "Steam visibility is selected independently of room policies"
        );
        policy.Grant(3);
        Check(policy.Allows(3, "") && !policy.Allows(2, ""), "A grant authorizes only its specific Steam identity");
        policy.Revoke(3);
        Check(!policy.Allows(3, ""), "A failed targeted invitation can revoke its new grant");
        Check(!policy.Invite(3, () => false) && !policy.Allows(3, ""), "A failed Steam send rolls back a new grant");
        Check(policy.Invite(3, () => policy.Allows(3, "")), "The grant exists before Steam sends the invitation");
        Check(
            !policy.Invite(3, () => false) && policy.Allows(3, ""),
            "A failed resend preserves an earlier successful grant"
        );
        policy.Revoke(3);
        string code = LobbyAddress.SteamCode(123, original);
        Check(
            LobbyAddress.TrySteam(code, out ulong codeId, out string secret) && codeId == 123 && secret == original,
            "Private codes decode into separate lobby and admission credentials"
        );
        Check(
            LobbyAddress.SteamCode(123) == "123",
            "Normal private sharing copies a game code without a Steam launch link"
        );
        foreach (
            string invalid in new[]
            {
                "frog:0:" + original,
                "frog:123:bad",
                code + ":extra",
                "frog:123:" + new string('Z', 64),
            }
        )
            Check(!LobbyAddress.TrySteam(invalid, out _, out _), "Malformed secret envelopes are rejected");

        using var rig = new Rig();
        var host = rig.Add("1", [new(0, Spawned: true)], admission: policy);
        policy.Grant(8);
        var denied = rig.Add("2", [new(0, Peer: 8)]);
        denied.AllowError = true;
        rig.Steps(30);
        Check(
            denied.Lobby.Error == "LOBBY ACCESS DENIED"
                && host.Lobby.PeerIds.SequenceEqual(new[] { 0 })
                && host.Lobby.Roster.Count == 1
                && denied.Lobby.LobbySession == null,
            "Missing credentials cannot reserve a room, learn peer addresses, or receive simulation state"
        );
        Check(
            rig.Sent.Where(packet => packet.Destination == "2")
                .All(packet =>
                    MeshLobby.TryDecodeControl(packet.Data, out var control)
                    && control!.Kind == MeshLobby.ControlKind.Error
                ),
            "Only the rejection reaches an unauthenticated guest"
        );
        denied.Active = false;

        foreach (bool spectating in new[] { false, true })
        {
            host.Wire.Incoming.Enqueue(
                new(
                    "9",
                    MeshLobby.EncodeControl(
                        new()
                        {
                            Kind = MeshLobby.ControlKind.Hello,
                            Hash = "mesh-fixture",
                            Nonce = Guid.NewGuid().ToString("N"),
                            Spectating = spectating,
                            Players = [],
                        }
                    )
                )
            );
            rig.Steps(1);
            Check(
                MeshLobby.TryDecodeControl(rig.Sent.Last(packet => packet.Destination == "9").Data, out var response)
                    && response!.Notice == "LOBBY ACCESS DENIED",
                "Empty-party and spectator requests pass through admission before participation validation"
            );
        }
        byte[] valid = MeshLobby.EncodeControl(
            new() { Kind = MeshLobby.ControlKind.Hello, Nonce = Guid.NewGuid().ToString("N") }
        );
        string forged = Encoding.UTF8.GetString(valid.AsSpan(5)).Insert(1, "\"Invited\":true,");
        byte[] forgedPacket = [.. valid.AsSpan(0, 5), .. Encoding.UTF8.GetBytes(forged)];
        Check(
            !MeshLobby.TryDecodeControl(forgedPacket, out _),
            "The obsolete guest invitation claim is not a protocol field"
        );

        var admitted = rig.Add("3", [new(0, Spawned: true)], admissionSecret: original);
        rig.WaitFor(() => rig.Ready, "A valid code did not complete admission");
        Check(
            rig.Sent.Where(packet => packet.Source == "1")
                .All(packet => !Encoding.UTF8.GetString(packet.Data).Contains(original)),
            "The host never distributes its secret in rosters, peer introductions or snapshots"
        );
        policy.Grant(4);
        var invited = rig.Add("4", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "A host grant did not admit the verified identity without a code");
        var pending = rig.Add("5", [new(0)], attach: false, admissionSecret: original);
        rig.WaitFor(() => pending.Lobby.Connected, "Pending admission did not reach the host");
        pending.AllowError = true;
        policy.Rotate();
        Check(
            !LobbyAdmissionPolicy.CanShare(original, policy.CodeFingerprint),
            "Rotation disables stale guest sharing without distributing the replacement secret"
        );
        host.Lobby.RevokePendingAdmissions();
        rig.Steps(30);
        Check(
            pending.Lobby.Error == "LOBBY CODE CHANGED" && !host.Lobby.PeerIds.Contains(pending.Lobby.LocalPeer),
            "Rotation removes pending admissions before they receive simulation access"
        );
        pending.Active = false;
        Check(
            !policy.Allows(4, "") && !policy.Allows(6, original) && policy.Allows(6, policy.Secret),
            "Rotation invalidates old codes and explicit grants"
        );
        rig.WaitFor(() => rig.Ready, "Rotation interrupted existing participants");
        rig.AssertConfirmedStates();
        Check(
            admitted.Lobby.Error == null && invited.Lobby.Error == null,
            "Already connected players keep playing after rotation"
        );
        var stale = rig.Add("6", [new(0)], admissionSecret: original);
        stale.AllowError = true;
        rig.Steps(30);
        Check(stale.Lobby.Error == "LOBBY ACCESS DENIED", "A stale code cannot open a new connection");
    }
}
