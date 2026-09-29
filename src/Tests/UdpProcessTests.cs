using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class UdpProcessTests
{
    public static void LocalhostProcesses()
    {
        using var reserve = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        reserve.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reserve.LocalEndPoint!).Port;
        reserve.Close();
        string process =
            Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate diagnostics executable");
        bool frameworkHosted = Path.GetFileNameWithoutExtension(process)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string executable = frameworkHosted
            ? (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? process)
            : process;
        string dll = typeof(Program).Assembly.Location;
        var children = new List<Process>();
        try
        {
            foreach (string role in new[] { "host", "join" })
            {
                var start = new ProcessStartInfo(executable)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                if (frameworkHosted)
                {
                    start.ArgumentList.Add(dll);
                }

                start.ArgumentList.Add("--udp-peer");
                start.ArgumentList.Add(role);
                start.ArgumentList.Add(port.ToString());
                children.Add(Process.Start(start)!);
            }

            var outputs = children.Select(p => p.StandardOutput.ReadToEndAsync()).ToArray();
            foreach (var child in children)
            {
                if (!child.WaitForExit(30000))
                {
                    child.Kill(true);
                    throw new Exception("UDP subprocess timed out");
                }

                Check(child.ExitCode == 0, $"UDP subprocess failed: {child.StandardError.ReadToEnd()}");
            }

            var values = outputs.Select(t => t.GetAwaiter().GetResult().Trim()).ToArray();
            Check(values[0] == values[1], $"Separate UDP processes disagreed: {string.Join(" / ", values)}");
            Console.WriteLine($"Separate-process UDP: {values[0]}");
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited)
                {
                    child.Kill(true);
                }

                child.Dispose();
            }
        }
    }

    public static int UdpPeer(string[] args)
    {
        using IGameLobby lobby =
            args[1] == "host"
                ? UdpLobby.Host(int.Parse(args[2]), 2, 2, "test", "{}")
                : UdpLobby.Join("127.0.0.1", int.Parse(args[2]), 2, "test");
        var timer = Stopwatch.StartNew();
        while (!lobby.Ready && timer.Elapsed.TotalSeconds < 10)
        {
            lobby.Poll();
            if (lobby.Error != null)
            {
                throw new Exception(lobby.Error);
            }

            Thread.Sleep(1);
        }

        if (!lobby.Ready)
        {
            throw new Exception("UDP lobby did not start");
        }

        var w = MakeWorld(4);
        using var session = new NetworkSession(
            w,
            new SessionConfig(lobby.PeerSlots, lobby.LocalPeer, "test", w),
            lobby.CreateTransport()
        );
        while (timer.Elapsed.TotalSeconds < 15 && (w.TickNumber < 480 || session.ConfirmedFrame < 479))
        {
            session.Poll();
            if (session.Error != null)
            {
                throw new Exception(session.Error);
            }

            if (w.TickNumber < 480)
            {
                session.TryAdvance(session.LocalSlots.Select(s => Input(w.TickNumber, s)).ToArray());
            }

            Thread.Sleep(1);
        }

        if (w.TickNumber != 480 || session.ConfirmedFrame != 479)
        {
            throw new Exception("UDP session stalled");
        }

        long end = timer.ElapsedMilliseconds + 300;
        while (timer.ElapsedMilliseconds < end)
        {
            session.Poll();
            Thread.Sleep(1);
        }

        Console.WriteLine($"480 confirmed ticks, hash {w.HashState():x16}");
        return 0;
    }
}
