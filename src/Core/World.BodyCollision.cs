using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private void MoveBody(PlayerState player, InputFrame input)
    {
        var terrain = collisionMaps[Match.CurrentMapIndex];
        var remaining = player.Velocity * player.LocalDelta;
        bool oneWay = !(input.Y < 0 && input.Jump || player.Mode == CharacterMode.Bouncing && player.WasHitDownwards);
        for (int attempt = 0; attempt < 3 && remaining != default; attempt++)
        {
            if (!terrain.SweepBody(player.Position, remaining, oneWay, out var contact))
            {
                player.X += remaining.X;
                player.Y += remaining.Y;
                break;
            }
            var normal = contact.Normal;
            player.X += remaining.X * contact.Fraction;
            player.Y += remaining.Y * contact.Fraction;
            remaining *= 1 - contact.Fraction;
            if (normal.Y != Fixed.Zero)
            {
                player.Y = contact.Surface;
                remaining = new(remaining.X, 0);
                if (normal.Y > 0)
                {
                    player.OnGround = true;
                    if (
                        player.Mode == CharacterMode.Bouncing && !player.HasReachedApex
                        || player.Mode == CharacterMode.Tongue && player.WasBouncingBeforeTongue
                    )
                    {
                        Bounce(player, -2);
                        player.VY *= -FromDecimal(.5m);
                    }
                    else
                        player.VY = 0;
                }
                else
                {
                    player.JumpGraceLeft = 0;
                    if (player.Mode == CharacterMode.Bouncing)
                    {
                        Bounce(player, 2);
                        player.VY = -player.VY;
                    }
                    else
                        player.VY = 0;
                }
            }
            else
            {
                player.X = contact.Surface;
                remaining = new(0, remaining.Y);
                if (player.Mode == CharacterMode.Bouncing)
                {
                    Bounce(player, normal.X < 0 ? 1 : -1);
                    player.VX = -player.VX;
                }
                else
                    player.VX = 0;
            }
        }

        if (
            input.X != 0
            && player.Mode != CharacterMode.Bouncing
            && !player.OnGround
            && player.VY < 0
            && terrain.TouchesWall(player.Position, input.X)
        )
        {
            player.WallSliding = true;
            player.WallSlideSide = input.X;
        }
    }

    private void BounceOffPlayers(PlayerState bouncer)
    {
        if (
            !Rules.BodyBouncingEnabled
            || bouncer.Mode != CharacterMode.Bouncing
            || bouncer.HasBounceDodged
            || bouncer.HitsTaken < 1
            || bouncer.OnGround
            || bouncer.LocalDelta == Fixed.Zero
            || Rules.Modifiers.BounceBeforeRecoveryOnly && bouncer.HasReachedApex
        )
            return;

        foreach (var victim in Players)
        {
            if (
                victim == bouncer
                || !victim.Alive
                || victim.Mode == CharacterMode.Bouncing
                || victim.Slot == bouncer.LastHitBy
                || victim.Mode == CharacterMode.Attacking && victim.AttackPhase == AttackPhase.Swing
                || !CollisionQueries.CircleTouchesBox(
                    bouncer.Center,
                    FromDecimal(.5m),
                    victim.X - 1,
                    victim.Y,
                    victim.X + 1,
                    victim.Y + 2
                )
            )
                continue;

            var velocity = bouncer.Velocity * FromDecimal(.75m);
            victim.HitsTaken++;
            ResetHit(victim, bouncer.LastHitBy, velocity);
            if (velocity.Y == Fixed.Zero)
                velocity = new(velocity.X, FromDecimal(.33m));
            SetVelocity(victim, velocity);
            ApplyHitstop(victim, bouncer.HitsTaken, 0);
            ApplyHitstop(bouncer, bouncer.HitsTaken, 0);
            ApplyNearbyHitstop(victim, bouncer.HitsTaken);
            Emit(
                SimulationEventKind.Hit,
                victim,
                bouncer.Slot,
                Fixed.Sqrt(velocity.LengthSquared),
                comboHits: victim.HitsTaken,
                hitKind: HitKind.Body,
                velocity: velocity,
                position: (victim.Center + bouncer.Center) / 2,
                hitstopSeconds: (Fixed)victim.HitstopTicks / TickRate
            );
            if (Rules.Modifiers.RedirectBounces)
                SetVelocity(
                    bouncer,
                    (bouncer.Position - victim.Position).Normalized
                        * Fixed.Sqrt(bouncer.Velocity.LengthSquared)
                        * FromDecimal(.75m)
                );
            break;
        }
    }
}
