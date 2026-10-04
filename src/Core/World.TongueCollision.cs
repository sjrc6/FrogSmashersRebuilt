using static FrogSmashers.Core.CollisionQueries;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private enum TongueContact
    {
        None,
        Terrain,
        Fly,
        Tongue,
        Player,
    }

    private void SweepExtendingTongue(PlayerState player, FixedVector start)
    {
        var terrain = collisionMaps[Match.CurrentMapIndex];
        var end = player.TongueTip;
        var direction = new FixedVector(player.TongueX, player.TongueY);
        var movement = end - start;
        var kind = TongueContact.None;
        int target = -1;
        Fixed first = 1;
        var point = end;
        void Take(Fixed fraction, TongueContact contact, int slot, FixedVector position)
        {
            if (kind != TongueContact.None && fraction >= first)
                return;
            first = fraction;
            kind = contact;
            target = slot;
            point = position;
        }

        bool downward = player.TongueY < 0;
        if (terrain.SweepTongue(player.TongueOrigin, end, downward, out var distance))
        {
            var obstruction = player.TongueOrigin + (end - player.TongueOrigin) * distance;
            var fraction =
                movement.LengthSquared == Fixed.Zero
                    ? Fixed.Zero
                    : Fixed.Clamp(FixedVector.Dot(obstruction - start, movement) / movement.LengthSquared, 0, 1);
            Take(fraction, TongueContact.Terrain, -1, obstruction);
        }
        if (terrain.SweepTongue(start, end, downward, out var time))
            Take(time, TongueContact.Terrain, -1, start + movement * time);

        var flySize = FromDecimal(.75m);
        if (
            Fly.Active
            && (Fly.Owner < 0 || Fly.Owner == player.Slot)
            && SweepCircleBox(
                start,
                end,
                FromDecimal(.5m),
                Fly.X - flySize,
                Fly.Y - flySize,
                Fly.X + flySize,
                Fly.Y + flySize,
                out time
            )
        )
            Take(time, TongueContact.Fly, -1, start + movement * time);

        var startDistance = FixedVector.Dot(start - player.TongueOrigin, direction);
        var endDistance = player.TongueDistance;
        Fixed actorStartTime = 0;
        var minimum = tuning.MinimumTongueDistance + FromDecimal(.001m);
        if (startDistance < minimum && endDistance >= minimum)
            actorStartTime = Fixed.Clamp((minimum - startDistance) / (endDistance - startDistance), 0, 1);
        var actorStart = start + movement * actorStartTime;
        foreach (var other in Players)
        {
            if (other == player || !other.Alive || endDistance < minimum)
                continue;
            if (
                other.Mode == CharacterMode.Tongue
                && other.TonguePhase is not (TonguePhase.Stunned or TonguePhase.Burping)
                && SweepCircle(actorStart, end, other.TongueTip, FromDecimal(1.5m), out time)
            )
            {
                time = actorStartTime + (1 - actorStartTime) * time;
                Take(time, TongueContact.Tongue, other.Slot, start + movement * time);
            }
        }
        foreach (var other in Players)
        {
            if (
                other == player
                || !other.Alive
                || endDistance < minimum
                || Rules.UsesTeams && other.Team == player.Team
            )
                continue;
            if (SweepCircleBox(actorStart, end, 1, other.X - 1, other.Y, other.X + 1, other.Y + 2, out time))
            {
                time = actorStartTime + (1 - actorStartTime) * time;
                Take(time, TongueContact.Player, other.Slot, start + movement * time);
            }
        }

        if (kind == TongueContact.None)
            return;
        player.TongueDistance = Fixed.Clamp(
            FixedVector.Dot(point - player.TongueOrigin, direction),
            0,
            player.TongueDistance
        );
        switch (kind)
        {
            case TongueContact.Terrain:
                player.TonguePhase =
                    player.TongueDistance > tuning.MinimumTongueDistance
                        ? TonguePhase.AttachedToTerrain
                        : TonguePhase.Retracting;
                if (player.TonguePhase == TonguePhase.AttachedToTerrain)
                    Emit(SimulationEventKind.TongueLatch, player, position: player.TongueTip);
                break;
            case TongueContact.Fly:
                if (TryClaimFly(player))
                {
                    player.TonguePhase = TonguePhase.RetractingHitFly;
                    Emit(SimulationEventKind.TongueLatch, player, position: player.TongueTip);
                }
                break;
            case TongueContact.Tongue:
                player.TonguePhase = TonguePhase.RetractingHitEnemyTongue;
                Emit(SimulationEventKind.TongueHit, player, target, position: player.TongueTip);
                break;
            case TongueContact.Player:
                Hit(Players[target], player, -direction, 0, true);
                player.TonguePhase = TonguePhase.RetractingHitEnemy;
                Emit(SimulationEventKind.TongueHit, player, target, position: player.TongueTip);
                break;
        }
    }
}
