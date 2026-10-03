using System.Collections;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FrogSmashers.Client;

internal static class Ipv6FirewallCheck
{
    internal const string Warning = "potential IPv6 Firewall issues detected, see IPv6-issues.txt";

    private const string NoticeFileName = "IPv6-issues.txt";

    internal readonly record struct Profile(int Id, bool Enabled, bool BlockAll, bool DefaultBlock);

    internal readonly record struct Rule(int Profiles, int Types, bool Allow, bool Unrestricted);

    private const int RequiredTypes = 7;

    public static Task<bool?> Start() => Task.Run(() => UpdateNotice(AppContext.BaseDirectory, Check()));

    internal static bool? UpdateNotice(string gameDirectory, bool? issueDetected)
    {
        string rootPath = Path.Combine(gameDirectory, NoticeFileName);
        string docsPath = Path.Combine(gameDirectory, "docs", NoticeFileName);
        bool dismissed = false;
        try
        {
            string noticePath = File.Exists(rootPath) ? rootPath : docsPath;
            if (File.Exists(noticePath))
            {
                foreach (string line in File.ReadLines(noticePath))
                {
                    string[] setting = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (setting.Length == 2 && setting[0] == "DISMISS_NOTICE")
                    {
                        bool.TryParse(setting[1].TrimEnd(';').Trim(), out dismissed);
                        break;
                    }
                }
            }

            if (issueDetected == true && !dismissed)
            {
                if (!File.Exists(rootPath) && File.Exists(docsPath))
                    File.Move(docsPath, rootPath);
            }
            else if (File.Exists(rootPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(docsPath)!);
                File.Move(rootPath, docsPath, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("IPv6 firewall notice unavailable: " + exception.Message);
        }
        return dismissed ? false : issueDetected;
    }

    private static bool? Check()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            bool ipv6 = NetworkInterface
                .GetAllNetworkInterfaces()
                .Any(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && adapter.Supports(NetworkInterfaceComponent.IPv6)
                    && adapter
                        .GetIPProperties()
                        .UnicastAddresses.Any(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                );
            return ipv6 ? ReadWindowsPolicy() : null;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("IPv6 firewall check unavailable: " + exception.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool? ReadWindowsPolicy()
    {
        object policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
        object? collection = null;
        try
        {
            int active = Read<int>(policy, "CurrentProfileTypes");
            var profiles = new List<Profile>();
            foreach (int id in new[] { 1, 2, 4 })
                if ((active & id) != 0)
                    profiles.Add(
                        new(
                            id,
                            Read<bool>(policy, "FirewallEnabled", id),
                            Read<bool>(policy, "BlockAllInboundTraffic", id),
                            Read<int>(policy, "DefaultInboundAction", id) == 0
                        )
                    );

            var rules = new List<Rule>();
            collection = Read<object>(policy, "Rules");
            foreach (object rule in (IEnumerable)collection)
            {
                try
                {
                    if (!Read<bool>(rule, "Enabled") || Read<int>(rule, "Direction") != 1)
                        continue;
                    int protocol = Read<int>(rule, "Protocol");
                    if (protocol is not (58 or 256))
                        continue;
                    int types = protocol == 256 ? RequiredTypes : TypeMask(Read<string>(rule, "IcmpTypesAndCodes"));
                    if (types == 0)
                        continue;
                    string application = Read<string>(rule, "ApplicationName");
                    string remote = Read<string>(rule, "RemoteAddresses");
                    object? interfaces = Read<object?>(rule, "Interfaces");
                    bool unrestricted =
                        (Any(application) || application.Equals("System", StringComparison.OrdinalIgnoreCase))
                        && Any(Read<string>(rule, "ServiceName"))
                        && Any(Read<string>(rule, "LocalAddresses"))
                        && (Any(remote) || (types == 1 && remote is "fe80::/64" or "fe80::/10"))
                        && Read<string>(rule, "InterfaceTypes").Equals("All", StringComparison.OrdinalIgnoreCase)
                        && (interfaces == null || interfaces is Array { Length: 0 });
                    rules.Add(new(Read<int>(rule, "Profiles"), types, Read<int>(rule, "Action") == 1, unrestricted));
                }
                finally
                {
                    Marshal.ReleaseComObject(rule);
                }
            }
            return Evaluate(profiles, rules);
        }
        finally
        {
            if (collection != null)
                Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(policy);
        }
    }

    private static T Read<T>(object target, string property, params object[] arguments) =>
        (T)target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, arguments)!;

    private static bool Any(string? value) => string.IsNullOrEmpty(value) || value == "*";

    internal static int TypeMask(string? specification)
    {
        if (specification?.Trim() == "*")
            return RequiredTypes;
        int mask = 0;
        foreach (string entry in (specification ?? "").Split([';', ','], StringSplitOptions.TrimEntries))
        {
            string[] pair = entry.Split(':', StringSplitOptions.TrimEntries);
            if (pair.Length > 2 || (pair.Length == 2 && pair[1] is not ("*" or "0")))
                continue;
            if (int.TryParse(pair[0], out int type) && type is >= 134 and <= 136)
                mask |= 1 << (type - 134);
        }
        return mask;
    }

    internal static bool? Evaluate(IReadOnlyList<Profile> profiles, IReadOnlyList<Rule> rules)
    {
        bool uncertain = profiles.Count == 0;
        foreach (var profile in profiles)
        {
            if (!profile.Enabled)
                continue;
            if (profile.BlockAll)
                return true;
            int allowed = profile.DefaultBlock ? 0 : RequiredTypes;
            int possible = 0;
            foreach (var rule in rules)
            {
                if ((rule.Profiles & profile.Id) == 0)
                    continue;
                if (!rule.Allow && rule.Unrestricted && rule.Types != 0)
                    return true;
                if (!rule.Allow)
                    continue;
                possible |= rule.Types;
                if (rule.Unrestricted)
                    allowed |= rule.Types;
            }
            if ((allowed | possible) != RequiredTypes)
                return true;
            uncertain |= allowed != RequiredTypes;
        }
        return uncertain ? null : false;
    }
}
