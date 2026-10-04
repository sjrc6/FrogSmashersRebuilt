namespace FrogSmashers.Network;

internal sealed class SpectatorPlayback
{
    private const int BufferFrames = FrogSmashers.Core.World.TickRate / 20;
    private const int CatchUpFrames = BufferFrames + FrogSmashers.Core.World.TickRate / 50;
    private const int FlushMilliseconds = 100;
    private int receivedFrames = -1;
    private long lastInputAt;
    private bool buffering = true;

    public double FrameDurationMultiplier(int bufferedFrames) => bufferedFrames > CatchUpFrames ? 0.5 : 1;

    public bool Ready(int frame, int bufferedFrames, long now, bool drain)
    {
        int received = frame + bufferedFrames;
        if (received != receivedFrames)
        {
            receivedFrames = received;
            lastInputAt = now;
        }
        if (bufferedFrames == 0)
        {
            buffering = true;
            return false;
        }
        if (buffering && !drain && bufferedFrames < BufferFrames && now - lastInputAt < FlushMilliseconds)
            return false;
        buffering = false;
        return true;
    }
}
