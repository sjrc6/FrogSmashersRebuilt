using static FrogSmashers.Core.CollisionQueries;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private bool Ray(FixedVector origin, bool horizontal, Fixed distance, bool includeOneWay, out Fixed allowed)
    {
        return collisionMaps[CurrentMapIndex]
            .Raycast(origin, horizontal, distance, includeOneWay, Rules.PreservePlatformEmbedding, out allowed);
    }

    private void MoveAndCollide(PlayerState player, InputFrame input)
    {
        var deltaTime = player.LocalDelta;
        if (deltaTime == Fixed.Zero)
        {
            return;
        }

        bool wasGround = player.OnGround;
        bool wasWall = player.WallSliding;
        player.OnGround = false;
        player.WallSliding = false;
        Fixed dx = player.VX * deltaTime;
        Fixed dy = player.VY * deltaTime;
        bool hit = false;
        if (dy < 0)
        {
            bool oneWay = !(
                input.Y < 0 && input.Jump || player.Mode == CharacterMode.Bouncing && player.WasHitDownwards
            );
            if (Ray(new(player.X - FromDecimal(.98m), player.Y), false, dy, oneWay, out var a))
            {
                dy = a;
                hit = true;
            }

            if (
                Ray(new(player.X + FromDecimal(.98m), player.Y), false, player.VY * deltaTime, oneWay, out a)
                && (!hit || a > dy)
            )
            {
                dy = a;
                hit = true;
            }

            if (hit)
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
                {
                    player.VY = 0;
                }
            }
        }
        else if (dy > 0)
        {
            if (Ray(new(player.X + FromDecimal(.98m), player.Y + 2), false, dy, false, out var a))
            {
                dy = a;
                hit = true;
            }

            if (
                Ray(new(player.X - FromDecimal(.98m), player.Y + 2), false, player.VY * deltaTime, false, out a)
                && (!hit || a < dy)
            )
            {
                dy = a;
                hit = true;
            }

            if (hit)
            {
                player.JumpGraceLeft = 0;
                if (player.Mode == CharacterMode.Bouncing)
                {
                    Bounce(player, 2);
                    player.VY = -player.VY;
                }
                else
                {
                    player.VY = 0;
                }
            }
        }

        hit = false;
        bool footHit = false;
        if (dx != Fixed.Zero)
        {
            int direction = dx > 0 ? 1 : -1;
            if (Ray(new(player.X + direction, player.Y + FromDecimal(.05m)), true, dx, false, out var a))
            {
                dx = a;
                hit = true;
                footHit = true;
            }

            if (
                Ray(new(player.X + direction, player.Y + FromDecimal(1.96m)), true, player.VX * deltaTime, false, out a)
                && (!hit || Fixed.Abs(a) < Fixed.Abs(dx))
            )
            {
                dx = a;
                hit = true;
            }

            if (hit)
            {
                if (player.Mode == CharacterMode.Bouncing)
                {
                    Bounce(player, direction);
                    player.VX = -player.VX;
                }
                else
                {
                    player.VX = 0;
                    if (footHit && input.X == direction && !player.OnGround && player.VY < 0)
                    {
                        player.WallSliding = true;
                        player.WallSlideSide = direction;
                    }
                }
            }
        }

        player.X += dx;
        player.Y += dy;
        if (player.OnGround && !wasGround || player.WallSliding && !wasWall)
        {
            Emit(SimulationEventKind.Land, player, surfaceSide: player.WallSliding ? player.WallSlideSide : -2);
            player.JumpCooldownLeft = FromDecimal(.1m);
        }

        if (player.OnGround)
        {
            player.JumpGraceLeft = tuning.JumpGraceTime;
        }
        else if (player.WallSliding)
        {
            player.JumpGraceLeft = tuning.JumpGraceTime * FromDecimal(.66m);
        }
        else
        {
            player.JumpGraceLeft -= deltaTime;
            if (player.VY <= tuning.GravityGraceThreshold)
            {
                player.GravityGraceLeft = Fixed.Max(0, player.GravityGraceLeft - deltaTime);
            }
        }
    }

    private void Bounce(PlayerState player, int side)
    {
        if (player.TimeSinceHit > FromDecimal(.35m))
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

        if (Fixed.Abs(side is -2 or 2 ? player.VY : player.VX) > 5)
        {
            Emit(SimulationEventKind.Bounce, player, velocity: player.Velocity, surfaceSide: side);
        }

        if (player.Mode == CharacterMode.Tongue)
        {
            player.Mode = CharacterMode.Bouncing;
        }

        player.WasHitDownwards = false;
    }

    private bool TouchesTerrain(FixedVector point, Fixed radius, bool includeOneWay)
    {
        return collisionMaps[CurrentMapIndex].TouchesCircle(point, radius, includeOneWay);
    }

    private static bool CapsuleTouchesPlayer(FixedVector a, FixedVector b, Fixed radius, PlayerState player)
    {
        var left = player.X - 1;
        var right = player.X + 1;
        var bottom = player.Y;
        var top = player.Y + 2;
        if (
            SegmentBox(a, b, left, bottom, right, top)
            || CircleTouchesBox(a, radius, left, bottom, right, top)
            || CircleTouchesBox(b, radius, left, bottom, right, top)
        )
        {
            return true;
        }

        var r2 = radius * radius;
        return PointSegmentDistanceSquared(new(left, bottom), a, b) <= r2
            || PointSegmentDistanceSquared(new(left, top), a, b) <= r2
            || PointSegmentDistanceSquared(new(right, bottom), a, b) <= r2
            || PointSegmentDistanceSquared(new(right, top), a, b) <= r2;
    }
}
