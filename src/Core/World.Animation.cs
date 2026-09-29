namespace FrogSmashers.Core;

public sealed partial class World
{
    private static readonly Fixed AnimationFrameDuration = Fixed.FromDecimal(.04m);

    private void UpdateAnimation(PlayerState player)
    {
        string animation = CharacterAnimation.Select(player);
        if (animation != player.AnimationKey)
        {
            player.AnimationKey = animation;
            player.AnimationTime = 0;
            return;
        }

        long oldFrame = player.AnimationTime.Raw / AnimationFrameDuration.Raw;
        player.AnimationTime += player.LocalDelta;
        long newFrame = player.AnimationTime.Raw / AnimationFrameDuration.Raw;
        if (animation == "run" && newFrame != oldFrame && (newFrame & 1) == 1)
        {
            Emit(SimulationEventKind.Footstep, player);
        }
    }
}
