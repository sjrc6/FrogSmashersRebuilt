using System.Text.Json;
using FrogSmashers.Client;
using FrogSmashers.Network;
using static FrogSmashers.Client.Ipv6FirewallCheck;

internal static class Ipv6FirewallTests
{
    public static void Run(Action<bool, string> check)
    {
        Profile[] active = [new(2, true, false, true)];
        Rule[] defaults = [new(7, 1, true, true), new(7, 2, true, true), new(7, 4, true, true)];
        check(Evaluate(active, []) == true, "Missing IPv6 rules trigger the advisory");
        check(Evaluate(active, defaults) == false, "Default discovery allowances pass");
        foreach (var rule in defaults)
            check(Evaluate(active, defaults.Except([rule]).ToArray()) == true, "Each discovery type is required");
        check(Evaluate(active, [new(7, 7, true, true)]) == false, "An equivalent custom rule covers all types");
        check(Evaluate(active, [new(4, 7, true, true)]) == true, "Rules on an inactive profile cannot cover discovery");
        check(Evaluate([new(2, false, true, true)], []) == false, "Disabled firewall does not trigger the advisory");
        check(Evaluate([new(2, true, true, true)], defaults) == true, "Block-all policy overrides allow rules");
        check(Evaluate(active, [.. defaults, new(2, 2, false, true)]) == true, "Broad explicit blocks override allows");
        check(Evaluate(active, [new(2, 7, true, false)]) == null, "Scoped custom rules leave coverage uncertain");
        check(
            Evaluate(active, [new(2, 3, true, false)]) == true,
            "Scoped rules cannot explain a completely absent type"
        );
        check(
            Evaluate([new(2, true, false, false)], []) == false,
            "Default inbound allow does not require extra rules"
        );
        check(Evaluate([], defaults) == null, "No readable active profile is inconclusive");
        check(
            Evaluate([.. active, new(4, true, false, true)], [new(2, 7, true, true)]) == true,
            "Every active profile must permit discovery"
        );
        check(
            TypeMask("134:0;135:*;136:0") == 7 && TypeMask("134, 135, 136") == 7,
            "ICMP rules match exact types and the discovery code"
        );
        check(
            TypeMask("*") == 7 && TypeMask("134:1;135:3;136:5;1134:0") == 0,
            "Wildcard rules work and unrelated codes/types cannot conceal missing discovery rules"
        );
        if (!OperatingSystem.IsWindows())
            check(Start().GetAwaiter().GetResult() == null, "Non-Windows platforms skip the Windows firewall check");
        NoticeFiles(check);

        var settings = new ClientSettings { SteamTransport = SteamTransport.Legacy };
        check(
            ClientSettings.Parse(JsonSerializer.Serialize(settings)).SteamTransport == SteamTransport.Legacy,
            "Host's preferred Steam API persists"
        );
        check(
            ClientSettings.Parse("{\"SteamTransport\":999}").SteamTransport == SteamTransport.Sockets,
            "Invalid Steam preference returns to sockets"
        );
    }

    private static void NoticeFiles(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "frog-ipv6-notice-" + Guid.NewGuid().ToString("N"));
        string docs = Path.Combine(directory, "docs");
        string docsPath = Path.Combine(docs, "IPv6-issues.txt");
        string rootPath = Path.Combine(directory, "IPv6-issues.txt");
        const string notice = "DISMISS_NOTICE = false;\nFirewall repair instructions\n";
        Directory.CreateDirectory(docs);
        try
        {
            File.WriteAllText(docsPath, notice);
            foreach (bool? result in new bool?[] { false, null })
            {
                check(UpdateNotice(directory, result) == result, "A clear or inconclusive check preserves its result");
                check(File.Exists(docsPath) && !File.Exists(rootPath), "No detected issue leaves the notice in docs");
            }

            check(UpdateNotice(directory, true) == true, "A detected issue displays the notice");
            check(
                !File.Exists(docsPath) && File.ReadAllText(rootPath) == notice,
                "An issue moves the notice beside the game"
            );
            check(
                UpdateNotice(directory, true) == true && File.Exists(rootPath),
                "Repeated issues keep the existing notice"
            );

            foreach (bool? result in new bool?[] { false, null })
            {
                UpdateNotice(directory, result);
                check(
                    !File.Exists(rootPath) && File.ReadAllText(docsPath) == notice,
                    "A cleared or inconclusive check returns the notice to docs"
                );
                UpdateNotice(directory, true);
            }

            string dismissed = notice.Replace("false", "true");
            File.WriteAllText(rootPath, dismissed);
            File.WriteAllText(docsPath, notice);
            check(
                UpdateNotice(directory, true) == false,
                "Dismissing the root notice hides the warning despite a detected issue"
            );
            check(
                !File.Exists(rootPath) && File.ReadAllText(docsPath) == dismissed,
                "Returning the notice preserves user edits over a fresh docs copy"
            );
            check(
                UpdateNotice(directory, true) == false && !File.Exists(rootPath),
                "Dismissal persists from docs on later checks"
            );

            foreach (string setting in new[] { "DISMISS_NOTICE=true", "  DISMISS_NOTICE = TRUE;  " })
            {
                File.WriteAllText(docsPath, setting);
                check(
                    UpdateNotice(directory, true) == false && File.Exists(docsPath),
                    "Dismissal accepts whitespace and an optional semicolon"
                );
            }
            foreach (
                string setting in new[]
                {
                    "DISMISS_NOTICE = false;",
                    "DISMISS_NOTICE = invalid;",
                    "Instructions mention DISMISS_NOTICE = true;",
                }
            )
            {
                File.WriteAllText(docsPath, setting);
                check(
                    UpdateNotice(directory, true) == true && File.Exists(rootPath),
                    "Only a true dismissal setting hides the warning"
                );
                UpdateNotice(directory, false);
            }
            File.Delete(docsPath);
            check(UpdateNotice(directory, true) == true, "A missing notice does not prevent the firewall warning");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
