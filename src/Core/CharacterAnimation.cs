namespace FrogSmashers.Core;

internal static class CharacterAnimation
{
    private static readonly Fixed RunThreshold = Fixed.FromDecimal(.1m);

    public static string Select(PlayerState player)
    {
        return player.Mode switch
        {
            CharacterMode.Attacking => Attack(player),
            CharacterMode.Tongue => Tongue(player),
            CharacterMode.Bouncing => Bounce(player),
            _ => Movement(player),
        };
    }

    private static string Attack(PlayerState player)
    {
        string animation = player.AttackPhase switch
        {
            AttackPhase.Charging => "attackCharge",
            AttackPhase.Swing => "attack",
            _ => "attackRecover",
        };
        if (player.AttackY > 0)
        {
            animation += player.AttackX == Fixed.Zero ? "Up" : "DiagUp";
        }
        else if (player.AttackY < 0)
        {
            animation += player.AttackX == Fixed.Zero ? "Down" : "DownForward";
        }

        return animation;
    }

    private static string Tongue(PlayerState player)
    {
        if (player.TonguePhase == TonguePhase.Burping)
        {
            return "tongueBurp";
        }

        if (player.TonguePhase is TonguePhase.Stunned or TonguePhase.RetractingHitEnemyTongue)
        {
            return "tongueRetractStunned";
        }

        if (player.TongueY < 0 && !player.OnGround)
        {
            return player.VY > 0 ? "tongueDownMovingUp" : "tongueDownMovingDown";
        }

        return "tongue";
    }

    private static string Bounce(PlayerState player)
    {
        if (player.OnGround && player.HasReachedApex)
        {
            return "skid";
        }

        if (player.HasBounceDodged)
        {
            return "bounceDodge";
        }

        return player.HasReachedApex ? "bounceRecover" : "bounce";
    }

    private static string Movement(PlayerState player)
    {
        if (player.WallSliding)
        {
            return "wallSlide";
        }

        if (!player.OnGround)
        {
            return player.VY > 0 ? "jump" : "fall";
        }

        return Fixed.Abs(player.VX) > RunThreshold ? "run" : "idle";
    }
}
