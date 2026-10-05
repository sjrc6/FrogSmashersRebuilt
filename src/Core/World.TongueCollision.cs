using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private bool CanTongueHit(PlayerState attacker, PlayerState target) =>
        !Rules.Lobby && target != attacker && target.Alive && (!Rules.UsesTeams || target.Team != attacker.Team);

    private void CheckTongueContact(PlayerState player, FixedVector priorTip)
    {
        bool downward = player.TongueY < 0;
        var tip = player.TongueTip;
        bool skippedWall =
            Rules.Modifiers.PhysicsFixes
            && !TouchesTerrain(tip, FromDecimal(.5m), downward)
            && collisionMaps[Match.CurrentMapIndex].SweepTongue(priorTip, tip, downward, out _);
        if (!skippedWall)
        {
            CheckTongueEndpoint(player);
            return;
        }
        player.TonguePhase =
            player.TongueDistance > tuning.MinimumTongueDistance
                ? TonguePhase.AttachedToTerrain
                : TonguePhase.Retracting;
        if (player.TonguePhase == TonguePhase.AttachedToTerrain)
            Emit(SimulationEventKind.TongueLatch, player, position: tip);
    }
}
