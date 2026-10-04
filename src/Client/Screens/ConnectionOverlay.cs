using FrogSmashers.Network;
using GGCS;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed class ConnectionOverlay(FrogGame game)
{
    private const int Width = 500;
    private const int Padding = 28;
    public bool Visible =>
        (game.IsActive || game.Options.Offscreen)
        && game.Controls.KeysNow.IsKeyDown(Keys.Tab)
        && (game.Menus.Screen == GameScreen.Seats || game.Menus.Screen == GameScreen.Playing && !game.Match.Paused);

    private IRollbackSession? Session => game.Menus.ShowingLobby ? game.Lobby.Network : game.Match.Network;

    public IReadOnlyList<PlayerRow> Rows()
    {
        var session = Session;
        var players = new List<PlayerRow>();
        if (game.Menus.ShowingLobby && game.Lobby.Simulation is { } lobby)
        {
            for (int room = 0; room < lobby.Membership.Rooms.Count; room++)
            {
                if (lobby.Membership.Rooms[room] is not { } player)
                    continue;
                int handle = player.Cpu ? -1 : Array.IndexOf(lobby.InputRooms.ToArray(), room);
                var color = PlayerPalette.Lobby(lobby.Membership.Rooms, room, game.Lobby.UsesTeams);
                players.Add(Row(handle, room, player.Peer, color, player.Cpu, session));
            }
        }
        else if (game.Match.World is { } world)
        {
            for (int slot = 0; slot < world.Players.Length; slot++)
            {
                bool cpu = world.Rules.CpuPlayers[slot];
                int handle = cpu ? -1 : session?.InputHandle(slot) ?? slot;
                int peer = handle >= 0 ? session?.PlayerPeer(handle) ?? 0 : 0;
                players.Add(Row(handle, slot, peer, PlayerPalette.For(world, slot), cpu, session));
            }
        }
        return players;
    }

    private PlayerRow Row(int handle, int slot, int peer, Color color, bool cpu, IRollbackSession? session)
    {
        if (cpu)
            return new(-1, $"CPU {slot + 1}", color, null, 0, 0);
        bool local = peer == (session?.LocalPeer ?? 0);
        PeerNetworkStats? stats = session
            ?.PeerStats.Where(value => value.PeerId == peer)
            .Select(value => (PeerNetworkStats?)value)
            .FirstOrDefault();
        int? ping =
            local ? 0
            : stats is { State: SessionState.Running } measured ? (int)Math.Round(measured.RoundTripMilliseconds)
            : null;
        int donation =
            session == null ? 0
            : local ? session.RollbackSettings.Donation
            : stats?.DonationFrames ?? game.Online.Lobby?.PeerRollbackSettings.GetValueOrDefault(peer)?.Donation ?? 0;
        string label = $"PLAYER {slot + 1}";
        if (local && !game.Match.ReplayPlayback)
            label += " (YOU)";
        return new(handle, label, color, ping, session?.PredictionForPlayer(handle) ?? 0, donation);
    }

    public string? InputWaitMessage()
    {
        if (!game.Menus.ShowingLobby && !game.Menus.ShowingMatch)
            return null;
        var session = Session;
        if (session == null)
            return null;
        var waiting = session.WaitingForInputPlayers;
        if (waiting.Count == 0)
            return session.WaitingForHostInputs ? "WAITING FOR INPUTS:\nHOST" : null;
        var rows = Rows();
        var players = rows.Where(row => waiting.Contains(row.Handle)).Select(row => row.Label).ToList();
        if (
            session.HostCommandHandle >= 0
            && waiting.Contains(session.HostCommandHandle)
            && !rows.Any(row => row.Handle >= 0 && session.PlayerPeer(row.Handle) == 0 && waiting.Contains(row.Handle))
        )
            players.Add("HOST");
        if (players.Count > 0)
            return "WAITING FOR INPUTS:\n" + string.Join(", ", players);
        return session.WaitingForHostInputs ? "WAITING FOR INPUTS:\nHOST" : null;
    }

    public void DrawInputWait()
    {
        if (InputWaitMessage() is not { } message)
            return;
        game.Renderer.EndUi();
        game.Renderer.BeginUi();
        string text = game.Assets.Font.Wrap(message, 960);
        var size = game.Assets.Font.Measure(text);
        var panel = new Rectangle((int)(Renderer.Width - size.X) / 2 - 12, 8, (int)size.X + 24, (int)size.Y + 24);
        game.Renderer.Panel(panel, Color.Black);
        game.Renderer.Text(text, Renderer.Width / 2, panel.Top + 12, center: true);
    }

    public void Draw()
    {
        if (!Visible)
            return;
        game.Renderer.EndUi();
        game.Renderer.BeginUi();
        var rows = Rows();
        var font = game.Assets.Font;
        int line = (int)MathF.Ceiling(font.Measure("PLAYERS").Y);
        int rowHeight = line * 2 + 16;
        int headingHeight = line + 24;
        int footerHeight = line * 2 + 24;
        int height = Padding * 2 + headingHeight + Math.Max(1, rows.Count) * rowHeight + footerHeight;
        var panel = new Rectangle((Renderer.Width - Width) / 2, (Renderer.Height - height) / 2, Width, height);
        game.Renderer.MenuPanel(panel);
        int left = panel.Left + Padding;
        int right = panel.Right - Padding;
        int y = panel.Top + Padding;
        game.Renderer.Text("PLAYERS", Renderer.Width / 2, y, center: true);
        Separator(left, right, y + line + 10);
        y += headingHeight;
        foreach (var row in rows)
        {
            game.Renderer.Panel(new(left, y, 12, line), row.Color);
            game.Renderer.Text(row.Label, left + 22, y);
            string ping = row.Ping is { } milliseconds ? $"{milliseconds} MS" : "-- MS";
            game.Renderer.Text(ping, right - font.Measure(ping).X, y, PingColor(row.Ping));
            game.Renderer.Text($"PRED: {row.Prediction}", left + 22, y + line + 6);
            string donation = $"DONATION: {row.Donation}";
            game.Renderer.Text(donation, right - font.Measure(donation).X, y + line + 6);
            y += rowHeight;
        }
        if (rows.Count == 0)
        {
            game.Renderer.Text("NO PLAYERS", Renderer.Width / 2, y, center: true);
            y += rowHeight;
        }
        Separator(left, right, y);
        var session = Session;
        int prediction = session?.PredictionDepth ?? 0;
        int limit = session?.MaxPrediction ?? RollbackPreferences.PredictionFrames;
        int extra = session?.ExtraDelayFrames ?? 0;
        int delay = session == null ? 0 : session.EffectiveDelayFrames - extra;
        game.Renderer.Text($"PRED: {prediction}/{limit}", Renderer.Width / 2, y + 14, center: true);
        game.Renderer.Text($"DELAY: {delay}+{extra}", Renderer.Width / 2, y + line + 20, center: true);
    }

    private void Separator(int left, int right, int y)
    {
        var pixels = game.Assets.Font.PixelScale(1);
        int alignedY = (int)(MathF.Round(y / pixels.Y) * pixels.Y);
        game.Renderer.Panel(new(left, alignedY, right - left, Math.Max(1, (int)pixels.Y)), Color.White);
    }

    internal static Color PingColor(int? ping) =>
        ping switch
        {
            null => Color.Gray,
            < 150 => new(163, 206, 39),
            < 250 => new(247, 224, 90),
            _ => new(192, 32, 45),
        };

    internal readonly record struct PlayerRow(
        int Handle,
        string Label,
        Color Color,
        int? Ping,
        int Prediction,
        int Donation
    );
}
