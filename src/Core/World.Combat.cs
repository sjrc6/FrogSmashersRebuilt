using static FrogSmashers.Core.CollisionQueries;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private static FixedVector GetAimDirection(PlayerState player, InputFrame input)
    {
        var direction = new FixedVector(player.Facing, 0);
        if (input.Y > 0 || input.Y < 0 && !player.OnGround)
        {
            direction = new(input.X, input.Y);
        }

        return direction;
    }

    private Fixed GetAttackCharge(PlayerState player) =>
        Fixed.Clamp(player.AttackCharge / tuning.AttackChargeTime, 0, 1);

    private void UpdateAttack(PlayerState player, InputFrame input)
    {
        if (player.AttackPhase == AttackPhase.Charging)
        {
            var direction = GetAimDirection(player, input);
            player.AttackX = direction.X;
            player.AttackY = direction.Y;
            player.AttackCharge += player.LocalDelta;
        }

        if (!input.Attack && player.PreviousInput.Attack && player.AttackPhase == AttackPhase.Charging)
        {
            player.AttackPhase = AttackPhase.Swing;
            player.AttackTimeLeft = tuning.AttackTime;
            Emit(
                SimulationEventKind.Swing,
                player,
                strength: GetAttackCharge(player),
                power: GetAttackCharge(player) + (player.HasFly ? FromDecimal(1.5m) : Fixed.Zero)
            );
        }
        else if (player.AttackPhase == AttackPhase.Swing)
        {
            player.AttackTimeLeft -= player.LocalDelta;
            if (player.AttackTimeLeft <= 0)
            {
                var direction = new FixedVector(player.AttackX, player.AttackY).Normalized;
                var radius = player.AttackY < 0 ? FromDecimal(1.75m) : FromDecimal(1.25m);
                var range =
                    tuning.AttackRange
                    + (GetAttackCharge(player) > FromDecimal(.5m) ? GetAttackCharge(player) : Fixed.Zero);
                foreach (var other in Players)
                {
                    if (
                        other != player
                        && other.Alive
                        && (!Rules.UsesTeams || player.Team != other.Team)
                        && CapsuleTouchesPlayer(player.Center, player.Center + direction * range, radius, other)
                    )
                    {
                        Hit(
                            other,
                            player,
                            new(player.AttackX, player.AttackY),
                            GetAttackCharge(player) + (player.HasFly ? FromDecimal(1.5m) : Fixed.Zero),
                            false
                        );
                    }
                }

                player.AttackPhase = AttackPhase.Recovering;
                player.AttackRecoverTimeLeft = tuning.AttackRecoverTime;
            }
        }
        else if (player.AttackPhase == AttackPhase.Recovering)
        {
            player.AttackRecoverTimeLeft -= player.LocalDelta;
            if (player.AttackRecoverTimeLeft < 0)
            {
                player.AttackPhase = AttackPhase.Idle;
                player.Mode = CharacterMode.Normal;
            }
        }
    }

    private void StartTongue(PlayerState player, InputFrame input)
    {
        if (player.Mode == CharacterMode.Bouncing)
        {
            if (!player.CanBounceTongue)
            {
                return;
            }

            player.WasBouncingBeforeTongue = true;
            player.CanBounceTongue = false;
            player.HasBounceDodged = true;
        }
        else
        {
            player.WasBouncingBeforeTongue = false;
        }

        player.Mode = CharacterMode.Tongue;
        player.TongueDistance = 0;
        player.TonguePhase = TonguePhase.Extending;
        player.TongueDelayLeft = tuning.TongueDelay;
        var direction = GetAimDirection(player, input);
        if (input.X != 0)
        {
            int horizontal = input.Strafe ? player.Facing : input.X;
            direction = new(horizontal, direction.Y);
        }

        direction = direction.Normalized;
        player.TongueX = direction.X;
        player.TongueY = direction.Y;
        Emit(SimulationEventKind.TongueLaunch, player);
    }

    private void UpdateTongue(PlayerState player, FixedVector priorOrigin)
    {
        var deltaTime = player.LocalDelta;
        if (player.TongueDelayLeft > 0)
        {
            player.TongueDelayLeft -= deltaTime;
            return;
        }

        var direction = new FixedVector(player.TongueX, player.TongueY);
        if (player.TonguePhase == TonguePhase.Extending)
        {
            var priorTip = priorOrigin + direction * player.TongueDistance;
            player.TongueDistance += tuning.TongueSpeed * deltaTime;
            if (player.TongueX * player.VX < 0)
            {
                player.TongueDistance += Fixed.Abs(player.VX) * deltaTime;
            }

            if (player.TongueY * player.VY < 0)
            {
                player.TongueDistance += Fixed.Abs(player.VY) * deltaTime;
            }

            if (Rules.Modifiers.PhysicsFixes)
            {
                player.TongueDistance = Fixed.Min(player.TongueDistance, tuning.TongueRange);
                SweepExtendingTongue(player, priorTip);
            }
            else
                CheckTongueEndpoint(player);

            bool atRangeLimit = Rules.Modifiers.PhysicsFixes
                ? player.TongueDistance >= tuning.TongueRange
                : player.TongueDistance > tuning.TongueRange;
            if (player.TonguePhase == TonguePhase.Extending && atRangeLimit)
            {
                player.TonguePhase = TonguePhase.Retracting;
            }
        }
        else if (player.TonguePhase == TonguePhase.AttachedToTerrain)
        {
            player.TongueDistance -= tuning.TongueRetractSpeedLatched * deltaTime;
            if (player.TongueDistance <= 0)
            {
                player.Mode = player.WasBouncingBeforeTongue ? CharacterMode.Bouncing : CharacterMode.Normal;
                if (player.WasBouncingBeforeTongue)
                {
                    player.BounceGravityRestore = 0;
                }
                else
                {
                    player.JumpGraceLeft = tuning.JumpGraceTime;
                }
            }
        }
        else if (player.TonguePhase == TonguePhase.Stunned || player.TonguePhase == TonguePhase.Burping)
        {
            player.Mode = CharacterMode.Normal;
        }
        else
        {
            player.TongueDistance -=
                (
                    player.TonguePhase == TonguePhase.RetractingHitEnemy
                        ? tuning.TongueSpeed
                        : tuning.TongueRetractSpeedMissed
                ) * deltaTime;
            if (player.TonguePhase == TonguePhase.RetractingHitFly)
            {
                if (Fly.Owner != player.Slot || !Fly.Active)
                {
                    player.TonguePhase = TonguePhase.Retracting;
                }
                else
                {
                    Fly.X = player.TongueTip.X;
                    Fly.Y = player.TongueTip.Y;
                }
            }

            if (player.TongueDistance <= 0)
            {
                if (player.TonguePhase == TonguePhase.RetractingHitFly && Fly.Owner == player.Slot)
                {
                    if (player.WasBouncingBeforeTongue)
                    {
                        StopBouncing(player);
                    }

                    player.TonguePhase = TonguePhase.Burping;
                    player.TongueDelayLeft = FromDecimal(.65m);
                    player.HasFly = true;
                    ReleaseFly();
                    Fly.IngestedBy = player.Slot;
                    Fly.Active = false;
                    Emit(SimulationEventKind.Burp, player, position: player.TongueTip);
                }
                else if (player.TonguePhase == TonguePhase.RetractingHitEnemyTongue && !player.WasBouncingBeforeTongue)
                {
                    player.TonguePhase = TonguePhase.Stunned;
                    player.TongueDelayLeft = FromDecimal(.65m);
                }
                else
                {
                    player.Mode = player.WasBouncingBeforeTongue ? CharacterMode.Bouncing : CharacterMode.Normal;
                }
            }
        }
    }

    private void CheckTongueEndpoint(PlayerState player)
    {
        var direction = new FixedVector(player.TongueX, player.TongueY);
        var tip = player.TongueTip;
        bool fly =
            Fly.Active
            && CircleTouchesBox(
                tip,
                FromDecimal(.5m),
                Fly.X - FromDecimal(.75m),
                Fly.Y - FromDecimal(.75m),
                Fly.X + FromDecimal(.75m),
                Fly.Y + FromDecimal(.75m)
            );
        if (fly)
        {
            if (TryClaimFly(player))
            {
                player.TonguePhase = TonguePhase.RetractingHitFly;
                Emit(SimulationEventKind.TongueLatch, player, position: tip);
            }
        }
        else if (TouchesTerrain(tip, FromDecimal(.5m), player.TongueY < 0))
        {
            if (player.TongueDistance > tuning.MinimumTongueDistance)
            {
                player.TonguePhase = TonguePhase.AttachedToTerrain;
                Emit(SimulationEventKind.TongueLatch, player, position: tip);
            }
        }
        else if (player.TongueDistance > tuning.MinimumTongueDistance)
        {
            bool hitTongue = false;
            foreach (var other in Players)
            {
                if (
                    other != player
                    && other.Alive
                    && other.Mode == CharacterMode.Tongue
                    && other.TonguePhase != TonguePhase.Stunned
                    && other.TonguePhase != TonguePhase.Burping
                    && (other.TongueTip - tip).LengthSquared <= FromDecimal(2.25m)
                )
                {
                    player.TonguePhase = TonguePhase.RetractingHitEnemyTongue;
                    Emit(SimulationEventKind.TongueHit, player, other.Slot, position: tip);
                    hitTongue = true;
                }
            }

            if (!hitTongue)
            {
                foreach (var other in Players)
                {
                    if (
                        other != player
                        && other.Alive
                        && (!Rules.UsesTeams || other.Team != player.Team)
                        && CircleTouchesBox(tip, 1, other.X - 1, other.Y, other.X + 1, other.Y + 2)
                    )
                    {
                        Hit(other, player, -direction, 0, true);
                        player.TonguePhase = TonguePhase.RetractingHitEnemy;
                        Emit(SimulationEventKind.TongueHit, player, other.Slot, position: tip);
                    }
                }
            }
        }
    }

    private void ResetHit(PlayerState victim, int attacker, FixedVector direction)
    {
        if (Fly.Owner == victim.Slot)
        {
            ReleaseFly();
        }

        if (direction.Y < -FromDecimal(.1m))
        {
            victim.WasHitDownwards = true;
        }

        victim.HasReachedApex = false;
        victim.LastHitBy = attacker;
        victim.CanBounceDodge = false;
        victim.HasBounceDodged = false;
        victim.CanBounceTongue = false;
        victim.HasBounceTongued = false;
        victim.Mode = CharacterMode.Bouncing;
        victim.AttackPhase = AttackPhase.Idle;
        victim.SkidRecoverLeft = FromDecimal(.5m);
        victim.TimeSinceHit = 0;
        victim.OnGround = false;
        victim.WallSliding = false;
        victim.StateStartTick = TickNumber;
        victim.AnimationTime = 0;
    }

    private void Hit(PlayerState victim, PlayerState attacker, FixedVector direction, Fixed power, bool tongue)
    {
        if (!tongue)
        {
            victim.HitsTaken++;
            if (victim.HasFly)
            {
                victim.HasFly = false;
                Fly.IngestedBy = -1;
                Fly.Active = true;
                Fly.X = victim.Center.X;
                Fly.Y = victim.Center.Y;
                ReleaseFly();
            }
        }

        ResetHit(victim, attacker.Slot, direction);
        if (direction.Y == Fixed.Zero)
        {
            direction = new(direction.X, tongue ? FromDecimal(.1m) : FromDecimal(.33m));
        }

        var totalPower = tongue ? (Fixed)25 : 10 + victim.HitsTaken * 10 + power * 30;
        SetVelocity(victim, direction.Normalized * totalPower);
        ApplyHitstop(victim, tongue ? FromDecimal(.75m) : victim.HitsTaken + power, 0);
        ApplyHitstop(attacker, tongue ? FromDecimal(.5m) : victim.HitsTaken + power, 0);
        if (!tongue)
        {
            ApplyNearbyHitstop(victim, victim.HitsTaken + power);
        }

        Emit(
            SimulationEventKind.Hit,
            victim,
            attacker.Slot,
            totalPower,
            power,
            victim.HitsTaken,
            tongue ? HitKind.Tongue : HitKind.Bat,
            victim.Velocity,
            hitstopSeconds: (Fixed)victim.HitstopTicks / TickRate
        );
    }

    private void ApplyHitstop(PlayerState player, Fixed duration, Fixed scale)
    {
        player.HitstopScale = player.HitstopTicks > 0 ? Fixed.Min(player.HitstopScale, scale) : scale;
        long durationRaw = (duration * FromDecimal(.175m) * TickRate).Raw;
        player.HitstopTicks = Math.Max(player.HitstopTicks, (int)((durationRaw + Fixed.Unit - 1) / Fixed.Unit));
    }

    private void ApplyNearbyHitstop(PlayerState victim, Fixed duration)
    {
        foreach (var other in Players)
        {
            var distance = (other.Position - victim.Position).LengthSquared;
            if (other.Alive && distance < 225)
            {
                ApplyHitstop(other, duration, distance / 225);
            }
        }
    }
}
