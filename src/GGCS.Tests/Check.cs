namespace GGCS.Tests;

internal static class Check
{
    public static int Count { get; private set; }

    public static void True(bool condition, string message)
    {
        Count++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        Count++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
    }
}
