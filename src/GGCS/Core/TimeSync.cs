namespace GGCS.Core;

internal sealed class TimeSync
{
    internal const int WindowSize = 30;
    private readonly int[] localAdvantages = new int[WindowSize];
    private readonly int[] remoteAdvantages = new int[WindowSize];

    public int AverageFrameAdvantage
    {
        get
        {
            long localSum = 0;
            long remoteSum = 0;
            for (int i = 0; i < WindowSize; i++)
            {
                localSum += localAdvantages[i];
                remoteSum += remoteAdvantages[i];
            }
            return (int)((remoteSum - localSum) / (2 * WindowSize));
        }
    }

    public void Record(int frame, int localAdvantage, int remoteAdvantage)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame));
        int index = frame % WindowSize;
        localAdvantages[index] = localAdvantage;
        remoteAdvantages[index] = remoteAdvantage;
    }
}
