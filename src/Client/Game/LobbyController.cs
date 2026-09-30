using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class LobbyController(FrogGame game)
{
    private const double TickSeconds = 1.0 / World.TickRate;
    private readonly (int Peer, int Id)?[] occupants = new (int, int)?[8];
    private readonly LobbyPlayer?[] previewPlayers = new LobbyPlayer?[8];
    private long previewSoundId = long.MinValue;
    private string receivedSettings = "";
    private bool onlineTeams;
    public bool TeamMode => IsHost ? game.Setup.Preferences.TeamMode : onlineTeams;
    private double accumulator;
    private double sendClock;
    public World? World { get; private set; }
    public World? PreviousWorld { get; private set; }
    public float Interpolation => (float)Math.Clamp(accumulator / (IsHost ? TickSeconds : 1.0 / 30), 0, 1);
    public IGameLobby? Online => game.Online.Lobby;
    public bool IsHost => Online == null || Online.IsHost;
    public int LocalPeer => Online?.LocalPeer ?? 0;
    public LobbyRoster Roster => Online is { Connected: true } ? Online.Roster : game.Setup.Lobby.Roster;
    public LobbyPlayer[] LocalPlayers => Roster.Players(Math.Max(0, LocalPeer));

    public void Open()
    {
        var rules = new GameRules
        {
            Lobby = true,
            PlayerCount = 8,
            MapOrder = [0],
        };
        var data = game.Assets.Data;
        World = new World([data.PresentationScenes["Lobby"]], rules, 1, data.CharacterParameters);
        PreviousWorld = new World([data.PresentationScenes["Lobby"]], rules, 1, data.CharacterParameters);
        Array.Clear(occupants);
        Array.Clear(previewPlayers);
        accumulator = sendClock = 0;
        game.Renderer.Reset();
        game.Audio.Reset();
    }

    public void RememberParty()
    {
        var local = LocalPlayers.Select(player => player with { Peer = 0 }).ToArray();
        var roster = game.Setup.Lobby.Roster;
        roster.Replace(Enumerable.Range(0, 8).Select(_ => new LobbySlot()).ToArray());
        roster.SetPlayers(0, local);
    }

    private bool SetPlayers(LobbyPlayer[] players) =>
        Online is { Connected: true } ? Online.SetPlayers(players) : game.Setup.Lobby.Roster.SetPlayers(0, players);

    public void JoinOrSpawn(int device)
    {
        var players = LocalPlayers;
        var player = players.FirstOrDefault(player => player.Id == device);
        if (player != null)
        {
            if (!player.Spawned)
                SetPlayers(
                    players.Select(value => value.Id == device ? value with { Spawned = true } : value).ToArray()
                );
            return;
        }
        var used = Roster.Slots.Select(slot => slot.Player?.Color).ToHashSet();
        var available = Enumerable.Range(0, 8).Where(value => !used.Contains(value)).ToArray();
        int color = available.Length == 0 ? -1 : available[Random.Shared.Next(available.Length)];
        if (
            color < 0
            || !SetPlayers([.. players, new LobbyPlayer(device, Math.Max(0, LocalPeer), players.Length % 2, color)])
        )
            game.Menus.Status = "NO OPEN SLOT";
    }

    public void Choose(int device, int horizontal, int vertical)
    {
        if (horizontal == 0 && vertical == 0)
            return;
        var players = LocalPlayers;
        var player = players.FirstOrDefault(player => player.Id == device && (!player.Spawned || player.Cpu));
        if (player == null)
            return;
        int color = player.Color;
        if (horizontal != 0)
        {
            var used = Roster.Slots.Select(slot => slot.Player?.Color).ToHashSet();
            var available = Enumerable.Range(0, 8).Where(value => !used.Contains(value)).ToArray();
            if (available.Length > 0)
                color = available[Random.Shared.Next(available.Length)];
        }
        var changed = player with { Color = color, Team = (player.Team + vertical + 8) % 8 };
        SetPlayers(players.Select(value => value.Id == device ? changed : value).ToArray());
    }

    public void BackOut(int device) => SetPlayers(LocalPlayers.Where(player => player.Id != device).ToArray());

    public void ChooseAgain(int device) =>
        SetPlayers(
            LocalPlayers.Select(player => player.Id == device ? player with { Spawned = false } : player).ToArray()
        );

    public bool Edit(int room, SlotType type, bool open, bool remove = false) =>
        Online != null ? Online.EditSlot(room, type, open, remove) : Roster.Edit(room, type, open, remove);

    public void Update(double elapsed)
    {
        if (World == null)
            Open();
        Online?.Poll();
        if (Online?.Error != null)
        {
            RememberParty();
            game.Fail(Online.Error);
            return;
        }
        if (Online?.Ready == true)
        {
            RememberParty();
            game.StartNetwork();
            return;
        }
        if (Online is { Connected: false })
            return;
        UpdatePreviews();
        if (!IsHost)
        {
            if (Online!.MatchSettingsJson != receivedSettings)
            {
                receivedSettings = Online.MatchSettingsJson;
                onlineTeams = JsonSerializer.Deserialize<MatchOptions>(receivedSettings)?.Rules.TeamMode ?? false;
            }
            sendClock += elapsed;
            if (sendClock >= 1.0 / 60)
            {
                Online!.SendLobbyInputs(ReadInputs());
                sendClock = 0;
            }
            if (Online!.TakeSnapshot() is { } snapshot)
            {
                PreviousWorld!.Restore(World!.Capture());
                World.Restore(snapshot);
                accumulator = 0;
            }
            else
                accumulator += elapsed;
            return;
        }
        accumulator += elapsed;
        for (int step = 0; step < 30 && accumulator + 1e-9 >= TickSeconds; step++)
        {
            for (int room = 0; room < 8; room++)
            {
                var player = Roster.Slots[room].Player;
                (int, int)? identity = player == null ? null : (player.Peer, player.Id);
                if (occupants[room] != identity)
                    World!.SetLobbySlot(room, false, player?.Color ?? room);
                occupants[room] = identity;
                World!.SetLobbySlot(room, player?.Spawned == true, player?.Color ?? room);
            }
            PreviousWorld!.Restore(World!.Capture());
            World.Tick(ReadInputs());
            game.Renderer.Consume(World.Events, World);
            game.Audio.PlayEvents(World.Events);
            if (World.TickNumber % 4 == 0)
                Online?.SendSnapshot(World.Capture());
            accumulator -= TickSeconds;
        }
        game.Audio.UpdateFlights(World!, (float)elapsed);
    }

    private void UpdatePreviews()
    {
        game.Renderer.SetLobbyPreviews(Roster);
        for (int room = 0; room < 8; room++)
        {
            var player = Roster.Slots[room].Player;
            var previous = previewPlayers[room];
            if (
                player is { Spawned: false }
                && (
                    previous == null
                    || previous.Spawned
                    || previous.Peer != player.Peer
                    || previous.Id != player.Id
                    || previous.Color != player.Color
                    || previous.Team != player.Team
                )
            )
            {
                game.Renderer.LobbyColorEffect(World!.Map, room, player.Color, World.TickNumber);
                var position = game.Renderer.LobbyPreviewPosition(World.Map, room);
                game.Audio.PlayAt("CharacterSpawn", previewSoundId++, .3f, new Vector3(position, 0));
            }
            previewPlayers[room] = player;
        }
    }

    private InputFrame[] ReadInputs()
    {
        var inputs = Online?.ReadLobbyInputs() ?? new InputFrame[8];
        for (int room = 0; room < 8; room++)
        {
            var player = Roster.Slots[room].Player;
            if (player == null || !player.Spawned || player.Peer != LocalPeer)
                continue;
            bool menuOwnsDevice =
                game.Menus.Screen != GameScreen.Seats
                && game.Menus.Owner is int owner
                && (owner == player.Id || owner < 2 && player.Id < 2);
            inputs[room] =
                menuOwnsDevice ? default
                : player.Cpu ? BotController.GetInput(World!, room)
                : game.Controls.Read(player.Id);
        }
        return inputs;
    }
}
