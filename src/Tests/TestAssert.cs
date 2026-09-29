namespace FrogSmashers.Tests;

internal static class TestAssert
{
    public static int Count { get; private set; }

    public static void Check(bool success, string message)
    {
        Count++;
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }
}
