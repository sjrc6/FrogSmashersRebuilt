using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed partial class LobbyController
{
    public LobbyPresentation Presentation { get; } = new();
    private long feedbackSound;
    public string? ProgressText =>
        Online?.LocalRequestPending == true ? "REQUESTING SLOT CHANGE..."
        : Online != null
        && (Online.Transitioning || Presentation.Pending || Network?.State == GGCS.SessionState.Synchronizing)
            ? "SYNCING LOBBY..."
        : null;

    private void UpdatePresentation()
    {
        var proposed = new LobbyRoster();
        var source = Online?.Roster ?? Roster;
        proposed.Replace(source.Slots, source.Spectators);
        if (Online is { IsHost: false, LocalRequestPending: true } online)
        {
            if (online.PendingLocalSpectating)
                proposed.SetSpectating(LocalPeer, true, online.Access);
            else
                proposed.SetPlayers(LocalPeer, online.PendingLocalPlayers, online.Access);
        }
        Presentation.Update(Roster, proposed);
        game.Renderer.SetLobbyPreviews(Presentation.Roster, TeamMode);
    }

    private void JoinFeedback(int device)
    {
        UpdatePresentation();
        int room = Presentation
            .Roster.Slots.ToList()
            .FindIndex(slot => slot.Player is { } player && player.Peer == LocalPeer && player.Id == device);
        if (room < 0 || World == null || !Presentation.AnnounceJoin(LocalPeer, device))
            return;
        var color = PlayerPalette.Lobby(
            Presentation.Roster.Slots.Select(slot => slot.Player).ToArray(),
            room,
            TeamMode
        );
        game.Renderer.LobbyColorEffect(World.Map, room, color, -1);
        var position = game.Renderer.LobbyPreviewPosition(World.Map, room);
        game.Audio.PlayAt("CharacterSpawn", --feedbackSound, .3f, new Vector3(position, 0));
    }

    private LobbyPlayer? EventPlayer(SimulationEvent item) =>
        item.Player >= 0 ? Simulation?.Membership.Rooms[item.Player] : null;

    private bool LocalFeedbackEvent(SimulationEvent item) =>
        item.Kind is SimulationEventKind.LobbyPreview or SimulationEventKind.Spawn
        && EventPlayer(item) is { Cpu: false } player
        && player.Peer == LocalPeer;
}
