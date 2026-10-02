using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public readonly record struct LobbyRosterCommand(int Revision, ulong Rooms, ulong Details, ulong Spectators)
{
    public uint Players
    {
        get
        {
            uint result = 1;
            for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
            {
                int owner = (byte)(Rooms >> (room * 8));
                if (owner < 0xfe)
                    result |= 1u << (owner >> 4);
            }
            return result;
        }
    }

    public static LobbyRosterCommand From(int revision, LobbyMembership membership)
    {
        ulong rooms = 0,
            details = 0,
            spectators = 0;
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            var player = membership.Rooms[room];
            int owner =
                player == null ? 0xff
                : player.Cpu ? 0xfe
                : player.Peer * 16 + player.Id;
            rooms |= (ulong)owner << (room * 8);
            if (player != null)
                details |= (ulong)(player.Color | player.Team << 3 | (player.Spawned ? 64 : 0)) << (room * 8);
        }
        foreach (var spectator in membership.Spectators)
            spectators |= (ulong)(spectator.Id + 1) << (spectator.Peer * 4);
        return new(revision, rooms, details, spectators);
    }

    public LobbyMembership Membership(LobbyMembership previous)
    {
        var rooms = new LobbyPlayer?[LobbyRoster.MaxPlayers];
        for (int room = 0; room < rooms.Length; room++)
        {
            int owner = (byte)(Rooms >> (room * 8));
            int detail = (byte)(Details >> (room * 8));
            rooms[room] = owner switch
            {
                0xff => null,
                0xfe => new(10 + room, Color: detail & 7, Team: (detail >> 3) & 7, Spawned: true, Cpu: true),
                _ => new(owner & 15, owner >> 4, (detail >> 3) & 7, detail & 7, (detail & 64) != 0),
            };
        }
        var observers = new List<LobbyPlayer>();
        for (int peer = 0; peer < LobbyRoster.MaxPeers; peer++)
        {
            int device = (int)(Spectators >> (peer * 4)) & 15;
            if (device == 0)
                continue;
            var saved =
                previous.Players(peer).FirstOrDefault(player => player.Id == device - 1)
                ?? previous.Spectator(peer)
                ?? new LobbyPlayer(device - 1, peer);
            observers.Add(saved with { Id = device - 1, Spawned = false });
        }
        return new(rooms, observers);
    }

    public bool IsValid
    {
        get
        {
            if (Revision < 0 || Spectators >> 48 != 0)
                return false;
            if (Revision == 0)
                return Rooms == 0 && Details == 0 && Spectators == 0;
            Span<ushort> devices = stackalloc ushort[LobbyRoster.MaxPeers];
            devices.Clear();
            int colors = 0;
            for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
            {
                int owner = (byte)(Rooms >> (room * 8));
                int detail = (byte)(Details >> (room * 8));
                if ((detail & 128) != 0 || owner == 0xff && detail != 0)
                    return false;
                if (owner == 0xff)
                    continue;
                int color = 1 << (detail & 7);
                if ((colors & color) != 0)
                    return false;
                colors |= color;
                if (owner == 0xfe)
                    continue;
                int peer = owner >> 4,
                    device = owner & 15;
                if (peer >= LobbyRoster.MaxPeers || device >= 10 || (devices[peer] & (1 << device)) != 0)
                    return false;
                devices[peer] |= (ushort)(1 << device);
            }
            int observers = 0;
            for (int peer = 0; peer < LobbyRoster.MaxPeers; peer++)
            {
                int device = (int)(Spectators >> (peer * 4)) & 15;
                if (device == 0)
                    continue;
                if (device > 10 || devices[peer] != 0 || ++observers > LobbyRoster.MaxSpectators)
                    return false;
            }
            return true;
        }
    }
}

[InlineArray(10)]
public struct LobbyControls
{
    private RollbackInput first;
}

public struct LobbyFrame : IEquatable<LobbyFrame>
{
    public LobbyRosterCommand Membership;
    public LobbyCpuCommand Cpu;
    public LobbyControls Controls;

    public bool Equals(LobbyFrame other)
    {
        if (Membership != other.Membership || Cpu != other.Cpu)
            return false;
        for (int index = 0; index < 10; index++)
            if (Controls[index] != other.Controls[index])
                return false;
        return true;
    }

    public override bool Equals(object? other) => other is LobbyFrame frame && Equals(frame);

    public override int GetHashCode() => HashCode.Combine(Membership, Cpu);

    public static LobbyFrame Predict(LobbyFrame input, int age)
    {
        for (int device = 0; device < 10; device++)
            input.Controls[device] = new(input.Controls[device].Gameplay);
        return input;
    }
}

internal sealed class LobbyFrameCodec : IInputCodec<LobbyFrame>, ISpectatorInputCodec<LobbyFrame>
{
    private const int HeaderSize = 42;
    public int Size => HeaderSize + 70;
    int ISpectatorInputCodec<LobbyFrame>.Size => HeaderSize + 1 + LobbyRoster.MaxPlayers * 9;

    public void Encode(LobbyFrame input, Span<byte> destination)
    {
        WriteHeader(input, destination);
        for (int device = 0; device < 10; device++)
            WriteControl(input.Controls[device], destination.Slice(HeaderSize + device * 7, 7));
    }

    public LobbyFrame Decode(ReadOnlySpan<byte> source)
    {
        var input = ReadHeader(source);
        for (int device = 0; device < 10; device++)
            input.Controls[device] = ReadControl(source.Slice(HeaderSize + device * 7, 7));
        return input;
    }

    public void Encode(ReadOnlySpan<PlayerInput<LobbyFrame>> inputs, Span<byte> destination)
    {
        destination.Clear();
        WriteHeader(inputs[0].Input, destination);
        int count = 0;
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            int owner = (byte)(inputs[0].Input.Membership.Rooms >> (room * 8));
            if (owner >= 0xfe)
                continue;
            int peer = owner >> 4,
                device = owner & 15;
            var target = destination.Slice(HeaderSize + 1 + count++ * 9, 9);
            target[0] = (byte)peer;
            target[1] = (byte)device;
            WriteControl(inputs[peer].Input.Controls[device], target[2..]);
        }
        destination[HeaderSize] = (byte)count;
    }

    public void Decode(ReadOnlySpan<byte> source, Span<PlayerInput<LobbyFrame>> inputs)
    {
        inputs.Clear();
        inputs[0] = new(ReadHeader(source), InputStatus.Confirmed);
        int count = source[HeaderSize];
        if (count > LobbyRoster.MaxPlayers)
            throw new InvalidDataException("Invalid spectator party size");
        for (int item = 0; item < count; item++)
        {
            var data = source.Slice(HeaderSize + 1 + item * 9, 9);
            int peer = data[0],
                device = data[1];
            if (peer >= inputs.Length || device >= 10)
                throw new InvalidDataException("Invalid spectator input owner");
            var input = inputs[peer].Input;
            input.Controls[device] = ReadControl(data[2..]);
            inputs[peer] = new(input, InputStatus.Confirmed);
        }
    }

    private static void WriteHeader(LobbyFrame input, Span<byte> data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data, input.Membership.Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(data[4..], input.Membership.Rooms);
        BinaryPrimitives.WriteUInt64LittleEndian(data[12..], input.Membership.Details);
        BinaryPrimitives.WriteUInt64LittleEndian(data[20..], input.Membership.Spectators);
        BinaryPrimitives.WriteInt32LittleEndian(data[28..], input.Cpu.Revision);
        data[32] = input.Cpu.Rooms;
        data[33] = input.Cpu.Enabled;
        BinaryPrimitives.WriteUInt32LittleEndian(data[34..], input.Cpu.Colors);
        BinaryPrimitives.WriteUInt32LittleEndian(data[38..], input.Cpu.Teams);
    }

    private static LobbyFrame ReadHeader(ReadOnlySpan<byte> data)
    {
        var frame = new LobbyFrame
        {
            Membership = new(
                BinaryPrimitives.ReadInt32LittleEndian(data),
                BinaryPrimitives.ReadUInt64LittleEndian(data[4..]),
                BinaryPrimitives.ReadUInt64LittleEndian(data[12..]),
                BinaryPrimitives.ReadUInt64LittleEndian(data[20..])
            ),
            Cpu = new(
                BinaryPrimitives.ReadInt32LittleEndian(data[28..]),
                data[32],
                data[33],
                BinaryPrimitives.ReadUInt32LittleEndian(data[34..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[38..])
            ),
        };
        if (!frame.Membership.IsValid || !frame.Cpu.IsValid)
            throw new InvalidDataException("Invalid lobby frame");
        return frame;
    }

    private static void WriteControl(RollbackInput input, Span<byte> data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data, input.Gameplay.Packed);
        data[4] = input.Actions;
        data[5] = unchecked((byte)input.ColorStep);
        data[6] = unchecked((byte)input.TeamStep);
    }

    private static RollbackInput ReadControl(ReadOnlySpan<byte> data)
    {
        var input = new RollbackInput(
            InputFrame.FromPacked(BinaryPrimitives.ReadUInt32LittleEndian(data)),
            data[4],
            unchecked((sbyte)data[5]),
            unchecked((sbyte)data[6])
        );
        if ((input.Actions & ~3) != 0 || input.ColorStep is < -1 or > 1 || input.TeamStep is < -1 or > 1)
            throw new InvalidDataException("Invalid lobby control");
        return input;
    }
}
