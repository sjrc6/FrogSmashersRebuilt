using System.Diagnostics;

namespace GGCS.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var timer = Stopwatch.StartNew();
            CoreTests.Run();
            ProtocolTests.Run();
            ReferenceOracleTests.Run(Check.True);
            SessionTests.Run();
            TimingTests.Run();
            ReviewTests.Run();
            ProtocolIntegrationTests.Run();
            FrogWorldTests.Run();
            if (!args.Contains("--no-sockets"))
                UdpTests.Run();
            Console.WriteLine($"GGCS PASS: {Check.Count:N0} checks in {timer.Elapsed.TotalSeconds:F2}s");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GGCS FAIL: {exception}");
            return 1;
        }
    }
}
