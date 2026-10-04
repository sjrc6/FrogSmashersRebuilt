using System.Net.Sockets;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private const int LobbiesPerPage = 6;
    private readonly List<(string Key, MenuEntry Entry)> browserEntries = new();
    private int browserPage;
    private string? browserNotice;

    private void CreateOnlineLobby()
    {
        Creation.Apply(game.Setup.Lobby.Roster);
        AllowLan = Creation.Lan;
        game.BeginLobby(Creation.Lan ? "udp" : "steam", true);
    }

    private void StartBrowser()
    {
        browserPage = 0;
        browserEntries.Clear();
        browserNotice = null;
        try
        {
            int party = Math.Max(1, game.Lobby.LocalPlayers.Count(player => !player.Cpu));
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
        string? selectedKey = browserEntries.ElementAtOrDefault(Selected).Key;
        var lobbies = browser?.Results ?? [];
        int pages = Math.Max(1, (lobbies.Count + LobbiesPerPage - 1) / LobbiesPerPage);
        browserPage = Math.Clamp(browserPage, 0, pages - 1);
        browserEntries.Clear();
        foreach (var lobby in lobbies.Skip(browserPage * LobbiesPerPage).Take(LobbiesPerPage))
        {
            browserEntries.Add(
                (
                    lobby.Id,
                    new(
                        lobby.Name,
                        () => game.BeginLobby(lobby.Target, false),
                        Value: $"{lobby.Players}/{lobby.Capacity}"
                    )
                )
            );
        }
        if (lobbies.Count == 0)
        {
            string status = browser?.Searching == true ? "SEARCHING..." : "NO LOBBIES FOUND";
            browserEntries.Add(("status", new(status, DisabledReason: status)));
        }
        if (pages > 1)
            browserEntries.Add(
                (
                    "page",
                    new(
                        "PAGE",
                        Value: $"{browserPage + 1}/{pages}",
                        Change: amount =>
                        {
                            browserPage = Wrap(browserPage + amount, pages);
                            UpdateBrowser();
                        }
                    )
                )
            );
        browserEntries.Add(
            (
                "refresh",
                new(
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
            )
        );
        browserEntries.Add(("back", new("BACK", Back)));
        int previous = browserEntries.FindIndex(row => row.Key == selectedKey);
        Selected =
            previous >= 0 ? previous
            : selectedKey is null or "status" ? 0
            : browserEntries.FindIndex(row => row.Key == "refresh");
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
        if (LobbyAddress.TrySteam(text, out ulong id))
            game.BeginLobby("steam:" + id, false);
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
