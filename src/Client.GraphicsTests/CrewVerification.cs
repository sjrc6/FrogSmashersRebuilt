using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

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
        void ChooseTeams(World target)
        {
            target.Advance(
                Enumerable
                    .Range(0, target.Players.Length)
                    .Select(slot => new MatchInput(
                        default,
                        new(MatchCommandKind.ChooseCrew, (byte)target.Rules.Teams[slot])
                    ))
                    .ToArray()
            );
            target.Advance(
                Enumerable
                    .Range(0, target.Players.Length)
                    .Select(slot => new MatchInput(default, new(MatchCommandKind.ToggleReady)))
                    .ToArray()
            );
        }
        void DrawPicking(string name)
        {
            for (int frame = 0; frame < 90; frame++)
            {
                renderer.Update(1 / 60f);
                renderer.DrawCrewPicking(world, players, controls);
            }
            if (directory != null)
                SaveFrame(Path.Combine(directory, name + ".png"));
        }
        renderer.Reset();
        DrawPicking("choose-crews-unassigned");
        for (int slot = 0; slot < 8; slot++)
            world.Match.ApplyCommand(world.Rules, slot, new(MatchCommandKind.ChooseCrew, (byte)(slot / 4)));
        DrawPicking("choose-crews-assigned");
        world.Match.ApplyCommand(world.Rules, 0, new(MatchCommandKind.ToggleReady));
        DrawPicking("choose-crews-ready");
        for (int slot = 0; slot < 8; slot++)
            world.Match.ApplyCommand(world.Rules, slot, new(MatchCommandKind.ChooseCrew, 0));
        DrawPicking("choose-crews-one-side");
        for (int slot = 0; slot < 8; slot++)
            world.Match.Players[slot].Ready = false;
        ChooseTeams(world);
        Check(world.Match.Phase == MatchPhase.Selecting, "Readying the chosen crews opens fighter selection");
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
        ChooseTeams(uneven);
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

        KeyboardState keyboard = default;
        var pad = new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.KeyboardSource = () => keyboard;
        controls.MouseSource = () => default;
        controls.GamePadSource = index => index == 0 ? pad : default;
        controls.Poll();
        controls.KeyboardBindings(0).Left = Keys.J;
        controls.KeyboardBindings(0).Right = Keys.L;
        controls.ControllerBindings(0).Left = Buttons.LeftShoulder;
        controls.ControllerBindings(0).Right = Buttons.RightShoulder;
        match.StartLocal(
            new MatchOptions(new GameRules(format: MatchFormat.Crews, scoring: ScoringMode.Stocks, teams: [0, 1])),
            [new LocalSeat(0), new LocalSeat(2)]
        );
        var selection = match.World!;
        ChooseTeams(selection);
        void Step(Keys[]? keys = null, Buttons buttons = Buttons.None)
        {
            keyboard = new KeyboardState(keys ?? []);
            pad = new(Vector2.Zero, Vector2.Zero, 0, 0, buttons);
            controls.Poll();
            match.Update(.01);
        }
        Step();
        Step([Keys.J], Buttons.RightShoulder);
        Check(selection.Match.TeamSelections.Take(2).All(slot => slot == -1), "Outward directions cannot join");
        Step([Keys.L], Buttons.LeftShoulder);
        Check(
            selection.Match.TeamSelections.Take(2).SequenceEqual([0, 1]),
            "Remapped keyboard and controller directions toward the center select their own fighters"
        );
        Step([Keys.L], Buttons.LeftShoulder);
        Step([Keys.U], Buttons.X);
        Check(
            selection.Match.TeamSelections.Take(2).SequenceEqual([0, 1]),
            "Holding inward or pressing Attack leaves selected fighters in place"
        );
        Step([Keys.T]);
        Check(selection.Match.Players[0].Ready, "Jump still readies the selected fighter");
        Step([Keys.J], Buttons.RightShoulder);
        Check(
            selection.Match.TeamSelections.Take(2).All(slot => slot == -1) && !selection.Match.Players[0].Ready,
            "Outward directions back out both fighters and clear readiness"
        );
        Step([Keys.L], Buttons.LeftShoulder);
        selection.Match.Players[0].Participation = Participation.Active;
        Step([Keys.J], Buttons.RightShoulder);
        Check(
            selection.Match.TeamSelections[0] == 0 && selection.Match.TeamSelections[1] == -1,
            "The locked survivor cannot back out with an outward direction"
        );
        Step([], Buttons.LeftShoulder);
        Step([Keys.T], Buttons.A);
        Check(selection.Match.Phase == MatchPhase.Playing, "Both selected fighters ready with Jump to start");
        return checks.ToArray();
    }
}
