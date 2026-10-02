using System.Runtime.InteropServices;

namespace FrogSmashers.Client;

internal sealed class ShutdownSignal : IDisposable
{
    private readonly ManualResetEventSlim completed = new();
    private readonly List<PosixSignalRegistration> signals = new();
    private readonly ConsoleHandler? consoleHandler;
    private int requested;
    public bool Requested => Volatile.Read(ref requested) != 0;

    public ShutdownSignal()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        if (OperatingSystem.IsWindows())
        {
            consoleHandler = OnConsoleSignal;
            SetConsoleCtrlHandler(consoleHandler, true);
        }
        else
        {
            foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
                signals.Add(
                    PosixSignalRegistration.Create(
                        signal,
                        context =>
                        {
                            context.Cancel = true;
                            Request();
                        }
                    )
                );
        }
    }

    public void Request() => Interlocked.Exchange(ref requested, 1);

    public void Complete() => completed.Set();

    private bool OnConsoleSignal(uint signal)
    {
        if (signal is not (0 or 1 or 2 or 5 or 6))
            return false;
        Request();
        if (signal >= 2)
            completed.Wait(TimeSpan.FromSeconds(2));
        return true;
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        Request();
        completed.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Complete();
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        if (consoleHandler != null)
            SetConsoleCtrlHandler(consoleHandler, false);
        foreach (var signal in signals)
            signal.Dispose();
    }

    private delegate bool ConsoleHandler(uint signal);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
