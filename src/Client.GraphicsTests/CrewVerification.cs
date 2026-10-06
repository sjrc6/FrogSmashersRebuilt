using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyCrews(string? directory)
    {
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var world = new World(
            assets.Data,
            new GameRules(
                playerCount: 8,
                format: MatchFormat.Crews,
                scoring: ScoringMode.Stocks,
                teams: [0, 0, 0, 0, 1, 1, 1, 1],
                mapOrder: [1]
            )
        );
        var players = Enumerable
            .Range(0, 8)
            .Select(slot => new MatchPlayerView(new LocalSeat(slot).Label, slot))
            .ToArray();
        var controls = new Controls(new ClientSettings());
        var layout = new CrewRosterLayout();
        layout.Update(world, true, 0);
        var side = layout.Positions.ToArray();
        Check(
            side.Take(4).All(position => position.X < Width / 2)
                && side.Skip(4).All(position => position.X > Width / 2),
            "Crews stack each team on its own side"
        );
        renderer.Reset();
        void Draw(int frames, string name, bool selecting = true)
        {
            for (int i = 0; i < frames; i++)
            {
                renderer.Update(1 / 60f);
                if (selecting)
                    renderer.DrawCrewSelection(world, players, controls);
                else
                {
                    renderer.DrawWorld(world, null, 1);
                    renderer.DrawMatchOverlay(world, players, controls);
                }
            }
            if (directory != null)
                SaveFrame(Path.Combine(directory, name + ".png"));
        }
        Draw(1, "choose-fighters");
        world.Match.ApplyCommand(world.Rules, 1, new(MatchCommandKind.SelectFighter, 1));
        world.Match.ApplyCommand(world.Rules, 5, new(MatchCommandKind.SelectFighter, 5));
        layout.Update(world, true, 1 / 60f);
        Check(
            layout.Positions[1].X > side[1].X
                && layout.Positions[1].X < 460
                && layout.Positions[5].X < side[5].X
                && layout.Positions[5].X > 820,
            "Selected fighters animate inward without teleporting"
        );
        Draw(6, "fighters-moving");
        Draw(90, "fighters-selected");
        world.Match.ApplyCommand(world.Rules, 1, new(MatchCommandKind.ToggleReady));
        Draw(1, "one-ready");
        world.Match.ApplyCommand(world.Rules, 5, new(MatchCommandKind.BackOutFighter));
        Draw(6, "fighter-backing-out");
        Draw(90, "fighter-returned");
        world.Match.ApplyCommand(world.Rules, 5, new(MatchCommandKind.SelectFighter, 5));
        world.Match.ApplyCommand(world.Rules, 5, new(MatchCommandKind.ToggleReady));
        world.Advance(new MatchInput[8]);
        for (int tick = 0; tick < 170; tick++)
            world.Advance(new MatchInput[8]);
        layout.Update(world, false, 3);
        Check(
            layout.Positions[0].X < 50
                && layout.Positions[4].X > Width - 50
                && layout.Positions[0].Y < 40
                && layout.Positions[4].Y < 40
                && layout.Positions[1].Y < 40
                && layout.Positions[5].Y < 40
                && layout.Positions[2].Y - layout.Positions[0].Y < 40,
            "Gameplay moves benches to the top corners and active fighters to the top center"
        );
        Draw(90, "gameplay-roster", false);
        world.Players[5].X = Fixed.FromDecimal(world.Map.KillBounds.Right + 10);
        world.Players[5].HitsTaken = world.Match.Players[5].Stocks;
        world.Advance(new MatchInput[8]);
        renderer.Consume(world.Events, world);
        Check(
            world.Match.Phase == MatchPhase.RoundFinished && world.Match.Winner == 1,
            "Eliminating a fighter starts a bout celebration even with benched opponents remaining"
        );
        world.Match.PhaseTicks = world.Rules.RoundFinishTicks - 2 * World.TickRate;
        Draw(30, "winner-skip", false);
        Check(
            renderer.Effects.Active.Any(effect => effect.Name == "Confetti."),
            "The intermediate crew winner receives the confetti celebration"
        );
        Check(renderer.Camera.HalfHeight < world.Map.OrthoSize, "A bout celebration zooms in on its winner");
        var skip = new MatchInput[8];
        skip[1] = new(default, new(MatchCommandKind.SkipCelebration));
        world.Advance(skip);
        renderer.Consume(world.Events, world);
        Draw(90, "next-bout-selection");
        world.Match.ApplyCommand(world.Rules, 4, new(MatchCommandKind.SelectFighter, 4));
        world.Match.ApplyCommand(world.Rules, 1, new(MatchCommandKind.ToggleReady));
        world.Match.ApplyCommand(world.Rules, 4, new(MatchCommandKind.ToggleReady));
        world.Advance(new MatchInput[8]);
        renderer.Consume(world.Events, world);
        Draw(90, "next-bout-gameplay", false);
        Check(
            world.Match.Phase == MatchPhase.Playing
                && world.Match.Winner == -1
                && renderer.Camera.HalfHeight == world.Map.OrthoSize
                && renderer.Effects.Active.All(effect => effect.Name != "Confetti."),
            "The next bout on the same map restores the arena camera and clears celebration effects"
        );

        var uneven = new World(
            assets.Data,
            new GameRules(
                playerCount: 8,
                format: MatchFormat.Crews,
                scoring: ScoringMode.Stocks,
                teams: [0, 0, 0, 0, 0, 0, 0, 1]
            )
        );
        layout.Reset();
        layout.Update(uneven, true, 0);
        Check(
            layout.Positions.All(position =>
                position.X >= 30 && position.X <= Width - 30 && position.Y >= 100 && position.Y <= Height - 50
            ),
            "A seven-player bench fits inside the selection screen"
        );
        renderer.Reset();
        renderer.DrawCrewSelection(uneven, players, controls);
        if (directory != null)
            SaveFrame(Path.Combine(directory, "uneven-teams.png"));
        renderer.Reset();
        using var audio = new Audio(assets, enabled: false);
        using var match = new MatchController(assets.Data, renderer, audio, controls, null);
        var options = new MatchOptions(new GameRules(scoring: ScoringMode.Stocks), 123);
        match.StartLocal(options, [new LocalSeat(0), new LocalSeat(1)]);
        match.World!.Match.WinRound(match.World.Rules, 1);
        Check(
            !match.TrySkipCelebration(2) && match.TrySkipCelebration(0),
            "The second keyboard winner can skip with the shared keyboard Start input; other devices cannot"
        );
        match.Update(.01);
        Check(match.World.Match.Phase == MatchPhase.RoundScores, "Victory skip is submitted as a match input");
        match.StartLocal(options, [new LocalSeat(0), new LocalSeat(2)]);
        match.World!.Match.WinRound(match.World.Rules, 1);
        Check(
            !match.TrySkipCelebration(0) && !match.TrySkipCelebration(3) && match.TrySkipCelebration(2),
            "Only the winning controller can skip its celebration"
        );
        return checks.ToArray();
    }
}
