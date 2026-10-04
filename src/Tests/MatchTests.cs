using FrogSmashers.Core;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class MatchTests
{
    public static void Scoring()
    {
        var score = CreateWorld(
            new()
            {
                WinScore = 2,
                MatchRounds = 1,
                RoundFinishTicks = 2,
                ScoreScreenTicks = 0,
            }
        );
        score.Players[1].X = 51;
        score.Players[1].LastHitBy = 0;
        score.Players[1].HitsTaken = 2;
        Step(score);
        Check(
            score.Players[0].Score == 2 && score.Winner == 0 && score.Phase == MatchPhase.RoundFinished,
            "knockout awards combo-sized points and wins the round"
        );
        Check(
            score.Events.Single(e => e.Kind == SimulationEventKind.Death).AwardedScore,
            "round-winning death retains its awarded-score flag after the phase changes"
        );
        var lateDeath = CreateWorld(new() { WinScore = 1, PlayerCount = 3 });
        lateDeath.Players[1].X = 51;
        lateDeath.Players[1].LastHitBy = 0;
        lateDeath.Players[2].X = 51;
        lateDeath.Players[2].LastHitBy = 1;
        Step(lateDeath);
        var deaths = lateDeath.Events.Where(e => e.Kind == SimulationEventKind.Death).ToArray();
        Check(
            deaths.Length == 2 && deaths[0].AwardedScore && !deaths[1].AwardedScore && lateDeath.Players[1].Score == 0,
            "death after victory in the same tick must not produce a score award or overwrite winner text"
        );
        var uncredited = CreateWorld();
        uncredited.Players[1].X = 51;
        Step(uncredited);
        Check(
            !uncredited.Events.Single(e => e.Kind == SimulationEventKind.Death).AwardedScore,
            "uncredited death emits no score award"
        );
        Step(score, count: 2);
        Check(
            score.Phase == MatchPhase.MatchFinished && score.Players[0].RoundWins == 1,
            "final round advances to match victory"
        );
        var teams = CreateWorld(
            new()
            {
                PlayerCount = 4,
                TeamMode = true,
                Teams = [0, 0, 1, 1],
                WinScore = 2,
            }
        );
        teams.Players[2].X = 51;
        teams.Players[2].LastHitBy = 0;
        teams.Players[2].HitsTaken = 2;
        Step(teams);
        Check(
            teams.Players[0].Score == 2
                && teams.Players[1].Score == 2
                && teams.Players[0].RoundWins == 1
                && teams.Players[1].RoundWins == 1,
            "team score and round wins are credited to every teammate"
        );
        var showdown = CreateWorld(
            new()
            {
                PlayerCount = 4,
                TeamMode = true,
                Teams = [0, 0, 1, 1],
                Showdown = true,
            }
        );
        showdown.Players[2].X = 51;
        showdown.Players[3].X = 51;
        Step(showdown);
        Check(
            showdown.Phase == MatchPhase.RoundFinished && showdown.Winner == 0 && showdown.Players[2].Eliminated,
            "team showdown ends when only one team survives"
        );
        Check(
            showdown.Events.Where(e => e.Kind == SimulationEventKind.Death).All(e => !e.AwardedScore),
            "showdown deaths emit no score awards"
        );
    }

    public static void RoundProgression()
    {
        var campaign = new World(
            new GameContent { Maps = [new() { Id = "first" }, new() { Id = "second" }, new() { Id = "Showdown" }] },
            new()
            {
                WinScore = 1,
                MatchRounds = 2,
                RoundFinishTicks = 1,
                ScoreScreenTicks = 0,
            },
            123
        );
        foreach (var player in campaign.Players)
        {
            player.Alive = true;
            player.X = 0;
            player.Y = 0;
        }

        campaign.Players[1].X = 51;
        campaign.Players[1].LastHitBy = 0;
        Step(campaign);
        Check(
            campaign.RoundNumber == 2 && campaign.Map.Id == "second" && campaign.Players[0].RoundWins == 1,
            "campaign rotates arenas while retaining round wins"
        );
        foreach (var player in campaign.Players)
        {
            player.Alive = true;
            player.X = 0;
            player.Y = 0;
        }

        campaign.Players[0].X = 51;
        campaign.Players[0].LastHitBy = 1;
        Step(campaign);
        Check(
            campaign.IsShowdown && campaign.Map.Id == "Showdown" && campaign.Players.All(p => !p.Eliminated),
            "tied final standings route to the showdown arena"
        );
        var scoreboard = CreateWorld(
            new()
            {
                WinScore = 1,
                MatchRounds = 1,
                RoundFinishTicks = 1,
            }
        );
        scoreboard.Players[1].X = 51;
        scoreboard.Players[1].LastHitBy = 0;
        Step(scoreboard);
        Check(
            scoreboard.Phase == MatchPhase.RoundScores
                && scoreboard.PhaseTicks == World.TickRate * 6
                && scoreboard.RoundNumber == 1
                && scoreboard.Winner == 0,
            "winner celebration enters the six-second standings screen before final victory"
        );
        var standingPosition = scoreboard.Players[0].Position;
        var standingAnimation = scoreboard.Players[0].AnimationTime;
        var standings = scoreboard.Capture();
        Step(scoreboard, new(1, 0, InputButtons.Jump | InputButtons.Attack), World.TickRate * 6 - 1);
        Check(
            scoreboard.Phase == MatchPhase.RoundScores
                && scoreboard.PhaseTicks == 1
                && scoreboard.Players[0].Position == standingPosition
                && scoreboard.Players[0].AnimationTime == standingAnimation,
            "standings advance network time while freezing actors and animation clocks"
        );
        var standingsHash = scoreboard.HashState();
        scoreboard.Restore(standings);
        Step(scoreboard, count: World.TickRate * 6 - 1);
        Check(
            scoreboard.HashState() == standingsHash,
            "standings countdown restores deterministically despite irrelevant player input"
        );
        Step(scoreboard);
        Check(
            scoreboard.Phase == MatchPhase.MatchFinished,
            "final standings remain visible for their full duration before match victory"
        );
    }
}
