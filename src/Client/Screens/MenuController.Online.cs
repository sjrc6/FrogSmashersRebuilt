using System.Net.Sockets;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private readonly List<MenuEntry> browserEntries = new();
    private string? browserNotice;

    private void CreateOnlineLobby()
    {
        Creation.Apply(game.Setup.Lobby.Roster);
        AllowLan = Creation.Lan;
        game.BeginLobby(Creation.Lan ? "udp" : "steam", true);
    }

    private void StartBrowser()
    {
        browserEntries.Clear();
        browserNotice = null;
        try
        {
            int party = game.Lobby.LocalPlayers.Count(player => !player.Cpu);
            game.Online.Browse(Screen == GameScreen.BrowseSteam, party);
        }
        catch (Exception exception) when (exception is InvalidOperationException or SocketException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            game.Toasts.Show(Screen == GameScreen.BrowseSteam ? "STEAM UNAVAILABLE" : "LAN SEARCH UNAVAILABLE");
        }
        UpdateBrowser();
    }

    private void UpdateBrowser()
    {
        var browser = game.Online.Browser;
        browser?.Poll();
        if (browser?.Error is { } notice && notice != browserNotice)
        {
            browserNotice = notice;
            game.Toasts.Show(notice);
        }
        var lobbies = browser?.Results ?? [];
        browserEntries.Clear();
        foreach (var lobby in lobbies)
            browserEntries.Add(
                new(
                    "lobby-" + lobby.Id,
                    lobby.Name,
                    () => game.BeginLobby(lobby.Target, false),
                    Value: $"{lobby.Players}/{lobby.Capacity}",
                    ValueSample: "8/8"
                )
            );
        if (lobbies.Count == 0)
        {
            string status = browser?.Searching == true ? "SEARCHING..." : "NO LOBBIES FOUND";
            browserEntries.Add(new("status", status, DisabledReason: status));
        }
        browserEntries.Add(
            new(
                "refresh",
                "REFRESH",
                () =>
                {
                    if (browser == null)
                        StartBrowser();
                    else
                    {
                        browserNotice = null;
                        browser.Refresh();
                        UpdateBrowser();
                    }
                }
            )
        );
        browserEntries.Add(BackRow());
    }

    private string? ReadClipboard()
    {
        try
        {
            string text = Clipboard.ReadText();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
            game.Toasts.Show("CLIPBOARD EMPTY");
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine(exception.Message);
            game.Toasts.Show("CANNOT READ CLIPBOARD");
        }
        return null;
    }

    private void JoinClipboardLobby()
    {
        string? text = ReadClipboard();
        if (text == null)
            return;
        if (LobbyAddress.TrySteam(text, out _, out _))
            game.BeginLobby(text, false);
        else
            game.Toasts.Show("NO LOBBY CODE IN CLIPBOARD");
    }

    private void JoinClipboardAddress()
    {
        string? text = ReadClipboard();
        if (text == null)
            return;
        if (!LobbyAddress.TryUdp(text, game.Options.Port, out string host, out int port))
        {
            game.Toasts.Show("NO ADDRESS IN CLIPBOARD");
            return;
        }
        JoinAddress = $"{host}:{port}";
        game.BeginLobby("udp:" + JoinAddress, false);
    }
}
