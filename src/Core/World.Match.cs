using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private void StartRound()
    {
        Match.StartRound();
        Fly = new FlyState { SpawnTicks = NextFlySpawnTicks() };
        for (int i = 0; i < Players.Length; i++)
        {
            var prior = Players[i];
            Players[i] = new PlayerState
            {
                Slot = i,
                Team = prior.Team,
                ColorIndex = prior.ColorIndex,
                SpawnTicks = TicksFromSeconds(.5m + .2m * i),
            };
        }
    }

    private int ChooseMap(int round)
    {
        if (Match.IsShowdown)
        {
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].Role == MapRole.Showdown)
                {
                    return i;
                }
            }
        }

        if (Rules.MapOrder.Length > 0)
        {
            return Rules.MapOrder[round % Rules.MapOrder.Length];
        }

        var regular = ArenaRotation.Available(maps, Rules.Modifiers);
        return regular.Length == 0 ? 0 : regular[round % regular.Length];
    }

    private void Spawn(PlayerState player)
    {
        Fixed best = -1;
        PointData point = new();
        foreach (var candidate in Map.Spawns)
        {
            Fixed closest = 1_000_000;
            var position = new FixedVector(FromDecimal(candidate.X), FromDecimal(candidate.Y));
            foreach (var other in Players)
            {
                if (other.Alive)
                {
                    closest = Fixed.Min(closest, (other.Position - position).LengthSquared);
                }
            }

            if (closest > best || closest == best && RandomRange(0, 2) == 0)
            {
                best = closest;
                point = candidate;
            }
        }

        if (Rules.Lobby)
        {
            point = Map.Spawns[player.Slot];
        }

        int slot = player.Slot;
        int team = player.Team;
        var fresh = new PlayerState
        {
            Slot = slot,
            Team = team,
            ColorIndex = player.ColorIndex,
            Alive = true,
            X = FromDecimal(point.X),
            Y = FromDecimal(point.Y),
            Facing = 1,
            SpawnTicks = TickRate,
            StateStartTick = TickNumber,
        };
        Players[slot] = fresh;
        Emit(SimulationEventKind.Spawn, fresh);
    }

    public void SetLobbySlot(int slot, bool active, int color)
    {
        if (!Rules.Lobby || slot < 0 || slot >= Players.Length || color is < 0 or > 7)
        {
            throw new ArgumentException("Invalid lobby player");
        }

        var player = Players[slot];
        player.ColorIndex = color;
        if ((Match.Players[slot].Participation == Participation.Active) == active)
        {
            return;
        }

        Match.Players[slot] = new PlayerProgress
        {
            Stocks = Rules.StartingStocks,
            Participation = active ? Participation.Active : Participation.Inactive,
        };
        Players[slot] = new PlayerState
        {
            Slot = slot,
            Team = Rules.Teams[slot],
            ColorIndex = color,
            SpawnTicks = 0,
        };
    }

    private bool IsWinner(PlayerState player) => Match.IsWinner(Rules, player.Slot);

    private void Kill(PlayerState player)
    {
        player.Alive = false;
        player.SpawnTicks = player.Y > FromDecimal(Map.KillBounds.Top) ? 3 * TickRate : TickRate;
        if (Fly.Owner == player.Slot)
        {
            ReleaseFly();
        }

        if (Fly.IngestedBy == player.Slot)
        {
            Fly.IngestedBy = -1;
            Fly.Active = false;
            Fly.SpawnTicks = NextFlySpawnTicks();
        }

        player.HasFly = false;
        Emit(
            SimulationEventKind.Death,
            player,
            player.LastHitBy,
            player.HitsTaken,
            scoreDelta: Match.DeathScore(Rules, player.LastHitBy, player.HitsTaken)
        );
        int winner = Match.RecordDeath(Rules, player.Slot, player.LastHitBy, player.HitsTaken);
        if (winner >= 0)
        {
            Match.WinRound(Rules, winner);
            Emit(SimulationEventKind.RoundWin, Players[winner]);
        }
    }

    private void AdvanceRound()
    {
        if (!Match.AdvanceRound(Rules))
        {
            Emit(SimulationEventKind.MatchWin, Players[Match.Winner]);
            return;
        }
        Match.CurrentMapIndex = ChooseMap(Match.RoundNumber - 1);
        StartRound();
    }
}
