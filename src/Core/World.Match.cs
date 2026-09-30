using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private void StartRound()
    {
        Phase = MatchPhase.Playing;
        Winner = -1;
        PhaseTicks = 0;
        Fly = new FlyState { SpawnTicks = RandomRange(15 * TickRate, 45 * TickRate) };
        for (int i = 0; i < Players.Length; i++)
        {
            var prior = Players[i];
            Players[i] = new PlayerState
            {
                Slot = i,
                Team = prior.Team,
                ColorIndex = prior.ColorIndex,
                RoundWins = prior.RoundWins,
                Eliminated = prior.Eliminated,
                SpawnTicks = 60 + 24 * i,
            };
        }
    }

    private int ChooseMap(int round)
    {
        if (IsShowdown)
        {
            for (int i = 0; i < maps.Count; i++)
            {
                if (
                    maps[i].Id.Contains("showdown", StringComparison.OrdinalIgnoreCase)
                    || maps[i].Name.Contains("showdown", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return i;
                }
            }
        }

        if (Rules.MapOrder.Length > 0)
        {
            return Rules.MapOrder[round % Rules.MapOrder.Length];
        }

        var regular = Enumerable
            .Range(0, maps.Count)
            .Where(i =>
                !maps[i].Id.Contains("showdown", StringComparison.OrdinalIgnoreCase)
                && !maps[i].Name.Contains("showdown", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
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
        int score = player.Score;
        int wins = player.RoundWins;
        var fresh = new PlayerState
        {
            Slot = slot,
            Team = team,
            ColorIndex = player.ColorIndex,
            Score = score,
            RoundWins = wins,
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
        if (player.Eliminated == !active)
        {
            return;
        }

        Players[slot] = new PlayerState
        {
            Slot = slot,
            Team = Rules.Teams[slot],
            ColorIndex = color,
            Eliminated = !active,
            SpawnTicks = 0,
        };
    }

    private bool IsWinner(PlayerState player) =>
        Winner >= 0 && (Rules.TeamMode ? player.Team == Players[Winner].Team : player.Slot == Winner);

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
            Fly.SpawnTicks = RandomRange(15 * TickRate, 45 * TickRate);
        }

        player.HasFly = false;
        Emit(
            SimulationEventKind.Death,
            player,
            player.LastHitBy,
            player.HitsTaken,
            awardedScore: !Rules.Lobby
                && Phase == MatchPhase.Playing
                && !IsShowdown
                && player.LastHitBy >= 0
                && player.LastHitBy < Players.Length
        );
        if (Rules.Lobby || Phase != MatchPhase.Playing)
        {
            return;
        }

        if (IsShowdown)
        {
            player.Eliminated = true;
            var survivors = Players.Where(x => !x.Eliminated).ToArray();
            if (
                survivors.Length == 1
                || survivors.Length > 0 && Rules.TeamMode && survivors.All(x => x.Team == survivors[0].Team)
            )
            {
                WinRound(survivors[0]);
            }
        }
        else if (player.LastHitBy >= 0 && player.LastHitBy < Players.Length)
        {
            var attacker = Players[player.LastHitBy];
            int points = Math.Max(1, player.HitsTaken);
            if (Rules.TeamMode)
            {
                foreach (var teammate in Players)
                {
                    if (teammate.Team == attacker.Team)
                    {
                        teammate.Score += points;
                    }
                }
            }
            else
            {
                attacker.Score += points;
            }

            if (attacker.Score >= TargetScore)
            {
                WinRound(attacker);
            }
        }
    }

    private void WinRound(PlayerState player)
    {
        Phase = MatchPhase.RoundFinished;
        Winner = player.Slot;
        PhaseTicks = Rules.RoundFinishTicks;
        foreach (var teammate in Players)
        {
            if (IsWinner(teammate))
            {
                teammate.RoundWins++;
            }
        }

        Emit(SimulationEventKind.RoundWin, player);
    }

    private void AdvanceRound()
    {
        if (IsShowdown)
        {
            Phase = MatchPhase.MatchFinished;
            Emit(SimulationEventKind.MatchWin, Players[Winner]);
            return;
        }

        if (RoundNumber >= Rules.MatchRounds)
        {
            int highest = Players.Max(player => player.RoundWins);
            var leaders = Players.Where(player => player.RoundWins == highest).ToArray();
            if (leaders.Length == 1 || Rules.TeamMode && leaders.All(player => player.Team == leaders[0].Team))
            {
                Winner = leaders[0].Slot;
                Phase = MatchPhase.MatchFinished;
                Emit(SimulationEventKind.MatchWin, leaders[0]);
                return;
            }

            IsShowdown = true;
            foreach (var player in Players)
            {
                player.Eliminated = player.RoundWins != highest;
            }
        }
        else
        {
            RoundNumber++;
        }

        CurrentMapIndex = ChooseMap(RoundNumber - 1);
        StartRound();
    }
}
