using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed partial class LobbyController(FrogGame game)
{
    private const double TickSeconds = 1.0 / World.TickRate;
    private readonly Dictionary<int, RollbackInput> commands = new();
    private string receivedSettings = "";
    private string? lastNotice;
    public MatchOptions? HostOptions { get; private set; }
    private double accumulator;
    private long rollbackCount;
    private long lastAudioTick = -1;
    private long lastVisualTick = -1;
    private IRollbackSession? presentedSession;
    private readonly LobbyRoster displayedRoster = new();
    public bool UsesTeams =>
        IsHost ? game.Setup.Preferences.Format != MatchFormat.Ffa : HostOptions?.Rules.UsesTeams == true;
    public LobbySimulation? Simulation { get; private set; }
    public World? World => Simulation?.World;
    public World? PreviousWorld { get; private set; }
    public IRollbackSession? Network => Online?.LobbySession;
    public float Interpolation => (float)Math.Clamp(accumulator / TickSeconds, 0, 1);
    public IGameLobby? Online => game.Online.Lobby;
    public bool IsHost => Online == null || Online.IsHost;
    public int LocalPeer => Online?.LocalPeer ?? 0;
    public LobbyRoster Roster
    {
        get
        {
            var policy = Online?.Roster ?? game.Setup.Lobby.Roster;
            displayedRoster.Replace(policy.Slots, policy.Spectators);
            if (Simulation != null && (Online == null || Network != null))
                displayedRoster.ApplyMembership(Simulation.Membership);
            return displayedRoster;
        }
    }

    public SlotType GetSlotType(int room) => Online?.GetSlotType(room) ?? game.Setup.Lobby.Roster.Slots[room].Type;

    public bool SlotEditPending(int room) => Online?.IsSlotEditPending(room) == true;

    public LobbyPlayer[] LocalPlayers => Roster.Players(Math.Max(0, LocalPeer));
    public bool RosterUpdating =>
        Online?.Transitioning == true || Online?.LocalRequestPending == true || Presentation.Pending;

    public void Open()
    {
        game.Audio.StopTitleMusic();
        var rules = new GameRules(lobby: true, playerCount: 8, mapOrder: [0]);
        var data = game.Assets.Data;
        var world = new World([data.PresentationScenes["Lobby"]], rules, 1, data.CharacterParameters);
        PreviousWorld = new World([data.PresentationScenes["Lobby"]], rules, 1, data.CharacterParameters);
        Simulation = new LobbySimulation(world, Online?.Roster ?? game.Setup.Lobby.Roster);
        Online?.SetRollbackSettings(game.Settings.Rollback);
        Online?.AttachSimulation(Simulation);
        commands.Clear();
        Presentation.Clear();
        accumulator = 0;
        receivedSettings = "";
        lastNotice = null;
        presentedSession = null;
        rollbackCount = 0;
        lastAudioTick = lastVisualTick = -1;
        game.Controls.ClearPendingEdges();
        game.Renderer.Reset();
        game.Audio.Reset();
        UpdatePresentation();
    }

    public void RememberParty()
    {
        var local = LocalPlayers.Select(player => player with { Peer = 0 }).ToArray();
        var roster = game.Setup.Lobby.Roster;
        roster.Reset();
        roster.SetPlayers(0, local);
    }

    public void Close()
    {
        game.Setup.ResetPreferences();
        game.Setup.Lobby.Roster.Reset();
        Simulation = null;
        PreviousWorld = null;
        commands.Clear();
        Presentation.Clear();
        accumulator = 0;
        receivedSettings = "";
        HostOptions = null;
        presentedSession = null;
        game.Renderer.SetLobbyPreviews(game.Setup.Lobby.Roster);
    }

    private bool SetPlayers(LobbyPlayer[] players) =>
        Online != null ? Online.SetPlayers(players) : EditLocal(roster => roster.SetPlayers(0, players));

    private bool EditLocal(Func<LobbyRoster, bool> edit)
    {
        var requested = new LobbyRoster(SlotType.Local);
        requested.Replace(Roster.Slots, Roster.Spectators);
        if (!edit(requested))
            return false;
        if (Simulation != null)
            Simulation.ApplyRoster(requested);
        if (Simulation != null)
            requested.ApplyMembership(Simulation.Membership);
        game.Setup.Lobby.Roster.Replace(requested.Slots, requested.Spectators);
        return true;
    }

    public void JoinOrSpawn(int device)
    {
        if (Roster.Spectator(LocalPeer) != null)
        {
            if (RosterUpdating)
                return;
            var spectator = Roster.Spectator(LocalPeer)!;
            if (
                SetPlayers([
                    .. LocalPlayers.Where(player => player.Cpu),
                    spectator with
                    {
                        Id = device,
                        Spawned = false,
                    },
                ])
            )
                JoinFeedback(device);
            else
                game.Toasts.Show("NO OPEN SLOTS");
            return;
        }
        var players = Online?.PendingLocalPlayers.ToArray() ?? LocalPlayers;
        if (players.Any(player => player.Id == device))
        {
            var player = LocalPlayers.FirstOrDefault(player => player.Id == device);
            if (player is { Spawned: false })
                QueueCommand(device, new(default, (byte)LobbyInputActions.Spawn));
            return;
        }
        var used = Roster
            .Slots.Select(slot => slot.Player?.Color)
            .Concat(players.Select(player => (int?)player.Color))
            .ToHashSet();
        var available = Enumerable.Range(0, 8).Where(value => !used.Contains(value)).ToArray();
        int color = available.Length == 0 ? -1 : available[Random.Shared.Next(available.Length)];
        if (color < 0 || !SetPlayers([.. players, new LobbyPlayer(device, Math.Max(0, LocalPeer), color, color)]))
            game.Toasts.Show(RosterUpdating ? "LOBBY UPDATING" : "NO OPEN SLOTS");
        else
            JoinFeedback(device);
    }

    public void ChooseColor(int device)
    {
        if (!LocalPlayers.Any(player => player.Id == device && !player.Spawned))
            return;
        QueueCommand(device, UsesTeams ? new(default, TeamStep: 1) : new(default, ColorStep: 1));
    }

    private void QueueCommand(int device, RollbackInput input)
    {
        var previous = commands.GetValueOrDefault(device);
        commands[device] = new(
            default,
            (byte)(previous.Actions | input.Actions),
            input.ColorStep == 0 ? previous.ColorStep : input.ColorStep,
            input.TeamStep == 0 ? previous.TeamStep : input.TeamStep
        );
    }

    public void BackOut(int device)
    {
        Remove(LocalPeer, device);
    }

    public bool CanChooseAgain(int room) =>
        World != null
        && Roster.Slots[room].Player is { Spawned: true, Cpu: false } player
        && player.Peer == LocalPeer
        && LobbySimulation.OnStartingPlatform(World, room);

    public bool TryChooseAgain(int device)
    {
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            if (Roster.Slots[room].Player?.Id != device || !CanChooseAgain(room))
                continue;
            QueueCommand(device, new(default, (byte)LobbyInputActions.SelectColor));
            return true;
        }
        return false;
    }

    public bool Edit(int room, SlotType type)
    {
        if (type == SlotType.Cpu && game.Setup.Preferences.Format == MatchFormat.Crews)
            return false;
        return Online != null ? Online.EditSlot(room, type) : EditLocal(roster => roster.Edit(room, type));
    }

    public void RemoveCpus()
    {
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
            if (GetSlotType(room) == SlotType.Cpu || Roster.Slots[room].Player is { Cpu: true })
                Edit(room, Online == null ? SlotType.Local : SlotType.Open);
    }

    public void ChangeCpu(int room, bool team, int direction = 1)
    {
        bool changed;
        if (Online != null)
            changed = Online.ChangeCpu(room, team, direction);
        else
        {
            var roster = new LobbyRoster(SlotType.Local);
            roster.Replace(Roster.Slots, Roster.Spectators);
            changed = roster.ChangeCpu(room, team, direction);
            if (changed)
            {
                Simulation!.ApplyCpuCommand(
                    LobbyCpuCommand.FromRoster(Simulation.CpuRevision + 1, (byte)(1 << room), roster)
                );
                roster.ApplyMembership(Simulation.Membership);
                game.Setup.Lobby.Roster.Replace(roster.Slots, roster.Spectators);
                UpdatePresentation();
            }
        }
        if (!changed && !team && Roster.Slots.Select(slot => slot.Player?.Color).OfType<int>().Distinct().Count() == 8)
            game.Toasts.Show("ALL COLORS USED");
    }

    public void ApplySlotType(SlotType type)
    {
        if (type == SlotType.Cpu && game.Setup.Preferences.Format == MatchFormat.Crews)
            return;
        if (Online != null)
            Online.ApplySlotType(type);
        else
            EditLocal(roster =>
            {
                roster.ApplySlotType(type);
                return true;
            });
    }

    public void Update(double elapsed)
    {
        if (Simulation == null)
            Open();
        if (Online?.Error != null || Network?.Error != null)
        {
            RememberParty();
            game.Fail(Online?.Error ?? Network!.Error!);
            return;
        }
        if (Online?.Notice != lastNotice)
        {
            lastNotice = Online?.Notice;
            if (!string.IsNullOrEmpty(lastNotice))
                game.Toasts.Show(lastNotice);
        }
        if (Online?.Ready == true)
        {
            RememberParty();
            game.StartNetwork();
            return;
        }
        if (Online is { Connected: false })
            return;
        if (!IsHost && Online!.MatchSettingsJson != receivedSettings)
        {
            receivedSettings = Online.MatchSettingsJson;
            HostOptions =
                JsonSerializer.Deserialize<MatchOptions>(receivedSettings)
                ?? throw new InvalidDataException("Invalid host match settings");
        }
        if (Online != null && Network == null)
        {
            accumulator = 0;
            UpdatePresentation();
            return;
        }
        if (game.Menus.LocalLobbyPaused)
        {
            UpdatePresentation();
            return;
        }
        UpdateSessionPresentation();
        Reconcile();
        accumulator += elapsed;
        for (int step = 0; step < 30; step++)
        {
            double duration = TickSeconds * (Network?.FrameDurationMultiplier ?? 1);
            if (accumulator + 1e-9 < duration)
                break;
            bool advanced = Network == null ? AdvanceLocal() : AdvanceNetwork();
            if (!advanced)
            {
                accumulator = Math.Min(accumulator, duration);
                break;
            }
            accumulator -= duration;
        }
        UpdatePresentation();
        game.Audio.UpdateFlights(World!, (float)elapsed);
    }

    private bool AdvanceLocal()
    {
        PreviousWorld!.Restore(World!.Capture());
        int[] handles = Enumerable.Range(0, Simulation!.InputSources.Count).ToArray();
        Simulation.Tick(ReadInputs(handles));
        ConsumeInputs(handles);
        game.Setup.Lobby.Roster.ApplyMembership(Simulation.Membership);
        PresentVisuals(Simulation.Events);
        PresentAudio(Simulation.Events);
        Presentation.Confirm(World.TickNumber - 1);
        return true;
    }

    private bool AdvanceNetwork()
    {
        var network = Network!;
        bool advanced = network.TryAdvance(ReadInputs(network.LocalSlots));
        if (network.LocalInputSubmitted)
            ConsumeInputs(network.AcceptedLocalSlots);
        bool corrected = Reconcile();
        if (advanced && !corrected)
            PreviousWorld!.Restore(network.PreviousSnapshot);
        return advanced;
    }

    private RollbackInput[] ReadInputs(IReadOnlyList<int> handles)
    {
        var result = new RollbackInput[handles.Count];
        for (int index = 0; index < handles.Count; index++)
        {
            var source = Simulation!.InputSources[handles[index]];
            if (source.HostCommand)
            {
                result[index] = new(default, Cpu: Online?.HostCommand ?? default);
                continue;
            }
            var player = Simulation.Membership.Rooms[source.Room]!;
            var input = commands.GetValueOrDefault(player.Id);
            if (player.Spawned && game.Menus.Screen == GameScreen.Seats)
                input = input with { Gameplay = game.Controls.Read(player.Id, consume: false) };
            result[index] = input;
        }
        return result;
    }

    private void ConsumeInputs(IReadOnlyList<int> handles)
    {
        foreach (int handle in handles)
        {
            var source = Simulation!.InputSources[handle];
            if (source.HostCommand)
                continue;
            var player = Simulation.Membership.Rooms[source.Room]!;
            commands.Remove(player.Id);
            game.Controls.Read(player.Id);
        }
    }

    private void UpdateSessionPresentation()
    {
        var devices = LocalPlayers.Select(player => player.Id).ToHashSet();
        foreach (int device in commands.Keys.Where(device => !devices.Contains(device)).ToArray())
            commands.Remove(device);
        if (ReferenceEquals(Network, presentedSession))
            return;
        presentedSession = Network;
        rollbackCount = 0;
        lastAudioTick = lastVisualTick = World!.TickNumber - 1;
        PreviousWorld!.Restore(World.Capture());
        game.Renderer.Rewind(World.TickNumber);
    }

    private bool Reconcile()
    {
        var network = Network;
        if (network == null)
            return false;
        bool corrected = network.RollbackCount != rollbackCount;
        if (corrected)
        {
            rollbackCount = network.RollbackCount;
            game.Renderer.Rewind(network.LastRollbackFromFrame);
            lastVisualTick = Math.Min(lastVisualTick, network.LastRollbackFromFrame - 1);
            PreviousWorld!.Restore(network.PreviousSnapshot);
        }
        PresentVisuals(network.EventsSince(lastVisualTick + 1));
        lastVisualTick = World!.TickNumber - 1;
        PresentAudio(network.EventsSince(lastAudioTick + 1).Where(item => item.Tick <= network.ConfirmedFrame));
        lastAudioTick = network.ConfirmedFrame;
        Presentation.Confirm(network.ConfirmedFrame);
        return corrected;
    }

    private void PresentVisuals(IEnumerable<SimulationEvent> events)
    {
        game.Renderer.SetLobbyPreviews(Roster, UsesTeams);
        foreach (var item in events)
        {
            if (!Presentation.ShowEvent(item, EventPlayer(item)))
                continue;
            float age = (float)Math.Max(0, (World!.TickNumber - 1 - item.Tick) * TickSeconds);
            if (item.Kind == SimulationEventKind.LobbyPreview)
                game.Renderer.LobbyColorEffect(
                    World!.Map,
                    item.Player,
                    PlayerPalette.Lobby(Simulation!.Membership.Rooms, item.Player, UsesTeams),
                    item.Tick,
                    age
                );
            else
                game.Renderer.Consume([item], World!, age);
            if (Network != null && LocalFeedbackEvent(item))
                PresentAudio([item]);
        }
    }

    private void PresentAudio(IEnumerable<SimulationEvent> events)
    {
        foreach (var item in events)
        {
            if (!Presentation.ShowEvent(item, EventPlayer(item)))
                continue;
            if (LocalFeedbackEvent(item) && !Presentation.PlaySound(item))
                continue;
            if (item.Kind == SimulationEventKind.LobbyPreview)
            {
                var position = game.Renderer.LobbyPreviewPosition(World!.Map, item.Player);
                game.Audio.PlayAt("CharacterSpawn", item.Id << 3, .3f, new Vector3(position, 0));
            }
            else
                game.Audio.PlayEvents([item]);
        }
    }
}
