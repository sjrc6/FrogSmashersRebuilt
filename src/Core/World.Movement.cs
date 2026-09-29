using static FrogSmashers.Core.CollisionQueries;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private void UpdateNormalInput(PlayerState player, InputFrame input)
    {
        var deltaTime = player.LocalDelta;
        if (input.X != 0 && player.Mode == CharacterMode.Normal)
        {
            if (!input.Strafe)
            {
                player.Facing = input.X;
            }

            if (player.OnGround && player.VX * input.X < 0)
            {
                player.VX = 0;
                Emit(SimulationEventKind.Turn, player);
            }

            player.VX += input.X * (player.OnGround ? tuning.RunAccel : tuning.AirAccel) * deltaTime;
            player.VX =
                input.X > 0 ? Fixed.Min(player.VX, tuning.MaxRunSpeed) : Fixed.Max(player.VX, -tuning.MaxRunSpeed);
        }
        else if (player.OnGround)
        {
            player.VX = Fixed.MoveTowards(player.VX, 0, tuning.RunAccel * deltaTime);
        }

        if (input.Attack)
        {
            if (player.Mode == CharacterMode.Normal)
            {
                player.Mode = CharacterMode.Attacking;
                player.AttackPhase = AttackPhase.Charging;
                player.AttackCharge = 0;
                Emit(SimulationEventKind.Charge, player);
            }

            if (player.AttackPhase == AttackPhase.Charging && input.X != 0 && !input.Strafe)
            {
                player.Facing = input.X;
            }
        }

        if (input.Tongue && !player.PreviousInput.Tongue && player.Mode == CharacterMode.Normal)
        {
            StartTongue(player, input);
        }

        if (
            input.Jump
            && input.Y >= 0
            && (player.JumpCooldownLeft <= 0 || (!player.PreviousInput.Jump && player.Mode != CharacterMode.Attacking))
        )
        {
            if (player.OnGround || player.WallSliding)
            {
                player.VY = tuning.JumpVel;
                player.GravityGraceLeft = tuning.GravityGraceTime;
                if (player.WallSliding)
                {
                    player.VX = -player.WallSlideSide * tuning.MaxRunSpeed;
                }

                Emit(SimulationEventKind.Jump, player, surfaceSide: player.WallSliding ? player.WallSlideSide : -2);
            }
            else if (player.JumpGraceLeft > 0)
            {
                player.VY = tuning.JumpVel;
            }
        }
    }

    private void UpdateBounceInput(PlayerState player, InputFrame input)
    {
        if ((input.X > 0 && player.VX < tuning.MaxRunSpeed / 2) || (input.X < 0 && player.VX > -tuning.MaxRunSpeed / 2))
        {
            player.VX += input.X * tuning.BounceAccel * player.LocalDelta;
        }

        if (player.CanBounceDodge && input.Jump && !player.OnGround)
        {
            player.CanBounceDodge = false;
            player.HasBounceDodged = true;
            var direction = new FixedVector(input.X, 1 + input.Y);
            if (direction.LengthSquared == Fixed.Zero)
            {
                direction = new(0, 1);
            }

            SetVelocity(player, direction.Normalized * tuning.BounceDodgePower);
            player.BounceGravityRestore = 0;
            Emit(SimulationEventKind.Dodge, player);
        }

        if (input.Tongue && !player.PreviousInput.Tongue)
        {
            StartTongue(player, input);
        }
    }

    private void UpdateVelocity(PlayerState player, InputFrame input)
    {
        var deltaTime = player.LocalDelta;
        if (player.Mode == CharacterMode.Tongue && player.TonguePhase == TonguePhase.AttachedToTerrain)
        {
            SetVelocity(player, new FixedVector(player.TongueX, player.TongueY) * tuning.TongueRetractSpeedLatched);
        }
        else if (
            player.Mode == CharacterMode.Bouncing
            || player.Mode == CharacterMode.Tongue && player.WasBouncingBeforeTongue
        )
        {
            player.TimeSinceHit += deltaTime;
            var gravity = tuning.BounceGravityMin;
            if (player.TimeSinceHit > tuning.BounceGravityRestoreDelay)
            {
                player.BounceGravityRestore += deltaTime;
                gravity = Fixed.Lerp(
                    gravity,
                    tuning.BounceGravityMax,
                    player.BounceGravityRestore / tuning.BounceGravityRestoreTime
                );
            }

            if (player.VY >= 0 && player.VY - gravity * deltaTime < 0)
            {
                player.HasReachedApex = true;
                if (!player.HasBounceDodged)
                {
                    player.CanBounceDodge = true;
                }

                if (!player.HasBounceTongued)
                {
                    player.CanBounceTongue = true;
                }
            }

            if (player.TimeSinceHit > 1)
            {
                if (!player.HasBounceDodged)
                {
                    player.CanBounceDodge = true;
                }

                if (!player.HasBounceTongued)
                {
                    player.CanBounceTongue = true;
                }
            }

            if (player.VY > tuning.MaxFallSpeed)
            {
                player.VY -= gravity * deltaTime;
            }

            if (
                Rules.CharactersBounceEachOther
                && !Rules.TeamMode
                && !player.HasBounceDodged
                && player.HitsTaken >= 1
                && !player.OnGround
                && (!player.HasReachedApex || !Rules.OnlyBounceBeforeRecover)
                && deltaTime > 0
            )
            {
                foreach (var other in Players)
                {
                    if (
                        other != player
                        && other.Alive
                        && other.Mode != CharacterMode.Bouncing
                        && other.Slot != player.LastHitBy
                        && !(other.Mode == CharacterMode.Attacking && other.AttackPhase == AttackPhase.Swing)
                        && CircleTouchesBox(
                            player.Center,
                            FromDecimal(.5m),
                            other.X - 1,
                            other.Y,
                            other.X + 1,
                            other.Y + 2
                        )
                    )
                    {
                        HitByBouncingPlayer(other, player);
                        if (Rules.WeirdBounceTrajectories)
                        {
                            SetVelocity(
                                player,
                                (player.Position - other.Position).Normalized
                                    * player.Velocity.Length
                                    * FromDecimal(.75m)
                            );
                        }
                    }
                }
            }

            if (player.OnGround && player.HasReachedApex)
            {
                if (player.Mode == CharacterMode.Tongue)
                {
                    player.Mode = CharacterMode.Bouncing;
                }

                player.VX = Fixed.MoveTowards(player.VX, 0, tuning.SkidAccel * deltaTime);
                if (Fixed.Abs(player.VX) < tuning.MaxRunSpeed)
                {
                    player.SkidRecoverLeft -= deltaTime;
                    if (player.SkidRecoverLeft <= 0)
                    {
                        StopBouncing(player);
                    }
                }
            }
        }
        else
        {
            var gravity = tuning.Gravity;
            if (input.Jump && player.VY <= tuning.GravityGraceThreshold && player.GravityGraceLeft > 0)
            {
                gravity *= 1 - player.GravityGraceLeft / tuning.GravityGraceTime;
            }

            player.VY = Fixed.Max(
                player.VY - gravity * deltaTime,
                (player.WallSliding ? tuning.MaxFallSpeedWallSlide : tuning.MaxFallSpeed)
            );
        }

        if (
            player.Mode != CharacterMode.Attacking
            && player.Mode != CharacterMode.Tongue
            && player.VX != Fixed.Zero
            && !input.Strafe
        )
        {
            player.Facing = player.VX > 0 ? 1 : -1;
        }

        if (player.Mode == CharacterMode.Tongue && player.TongueX != Fixed.Zero)
        {
            player.Facing = player.TongueX > 0 ? 1 : -1;
        }

        player.JumpCooldownLeft -= deltaTime;
    }

    private static void SetVelocity(PlayerState player, FixedVector velocity)
    {
        player.VX = velocity.X;
        player.VY = velocity.Y;
    }

    private static void StopBouncing(PlayerState player)
    {
        player.Mode = CharacterMode.Normal;
        player.WasHitDownwards = false;
        player.HitsTaken = 0;
        player.LastHitBy = -1;
    }
}
