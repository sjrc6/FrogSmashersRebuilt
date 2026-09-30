using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public void CaptureScorePresentation(string directory)
    {
        foreach (int playerCount in new[] { 4, 5, 8 })
            CaptureScoreLayout(Path.Combine(directory, $"{playerCount}-players"), playerCount);
    }

    private void CaptureScoreLayout(string directory, int playerCount)
    {
        Directory.CreateDirectory(directory);
        renderer.Reset();
        var world = new World(
            assets.Data,
            new GameRules
            {
                PlayerCount = playerCount,
                WinScore = 10,
                MapOrder = [1],
            }
        );
        var inputs = new InputFrame[playerCount];
        for (int slot = 0; slot < playerCount; slot++)
        {
            var player = world.Players[slot];
            player.SpawnTicks = 0;
            player.Alive = true;
            player.X = -12 + slot * 8;
            player.Y = 0;
        }

        void Kill(int slot, int hits)
        {
            var player = world.Players[slot];
            player.Alive = true;
            player.X = Fixed.FromDecimal(world.Map.KillBounds.Right + 1);
            player.Y = 0;
            player.LastHitBy = 0;
            player.HitsTaken = hits;
            world.Tick(inputs);
            renderer.Consume(world.Events, world);
        }

        void DrawFrames(int count, string name)
        {
            for (int i = 0; i < count; i++)
            {
                renderer.Update(1 / 60f);
                renderer.DrawWorld(world, null, 1);
            }

            SaveFrame(Path.Combine(directory, name + ".png"));
        }

        Kill(1, 3);
        DrawFrames(13, "signed-award");
        Kill(2, 7);
        Kill(3, 1);
        DrawFrames(13, "winner-after-late-death");
        renderer.Reset();
    }

    public string[] VerifyScorePresentation()
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException(name);
            }

            checks.Add(name);
        }

        void Close(float actual, float expected, string name) => Check(Math.Abs(actual - expected) < .00001f, name);
        var color = PlayerColors[2];
        Check(EffectAnimation.ScoreFlash(.025f, color) == color, "score flash begins with player color");
        Check(EffectAnimation.ScoreFlash(.075f, color) == Color.White, "score flash changes to white after50ms");
        Check(EffectAnimation.ScoreFlash(.125f, color) == Color.Black, "score flash changes to black after100ms");
        Check(
            EffectAnimation.SideScoreFlash(.05f, color) == color
                && EffectAnimation.SideScoreFlash(.15f, color) == Color.White
                && EffectAnimation.SideScoreFlash(.25f, color) == Color.Black,
            "KO text uses separate300ms flash cycle"
        );
        Close(EffectAnimation.OverheadScoreScale(2), 1.5f, "overhead award starts at150percent scale");
        Close(EffectAnimation.OverheadScoreScale(1.625f), 1.25f, "overhead award shrinks through source interval");
        Close(EffectAnimation.OverheadScoreScale(1.5f), 1, "overhead award returns to normal after500ms");
        var clock = new SpriteAnimation { Frame = 2, Counter = .2f };
        clock.Step(0, .05f, 10, false);
        Check(clock.Frame == 2 && clock.Counter == .2f, "paused sprite clock cannot consume accumulated time");
        renderer.Reset();
        var world = new World(assets.Data, new GameRules { PlayerCount = 2 });
        SimulationEvent Death(long tick, int killed, int scorer, int hits, bool award) =>
            new(tick, 0, SimulationEventKind.Death, killed, scorer, 40, 0, hits, AwardedScore: award);
        renderer.Consume([Death(10, 1, 0, 3, true)], world);
        Check(
            renderer.Scores.Messages.Single().Text == "+3" && renderer.Scores.OverheadMessage(0)?.Text == "+3",
            "scored kill displays signed award in HUD and overhead"
        );
        renderer.Consume([Death(11, 0, -1, 0, false)], world);
        Check(
            renderer.Scores.OverheadMessage(0) == null && renderer.Scores.Messages.Single().Text == "+3",
            "character death clears overhead while preserving HUD award"
        );
        renderer.Rewind(11);
        Check(
            renderer.Scores.OverheadMessage(0)?.Text == "+3",
            "rollback restores popup after predicted death is removed"
        );
        renderer.Consume([new SimulationEvent(12, 0, SimulationEventKind.Spawn, 0, -1, 0, 0, 0)], world);
        Check(renderer.Scores.OverheadMessage(0) == null, "respawn does not inherit old character score popup");
        renderer.Consume([new SimulationEvent(13, 0, SimulationEventKind.RoundWin, 0, -1, 0, 0, 0)], world);
        renderer.Consume([Death(14, 1, 0, 1, false)], world);
        Check(
            renderer.Scores.Messages.Last().Text == "WINNER ! ! !"
                && renderer.Scores.OverheadMessage(0)?.Overhead == "WIN!",
            "post-victory death cannot replace winner text with award"
        );
        renderer.Update(2.1f);
        renderer.Update(.001f);
        Check(
            renderer.Scores.Messages.Count == 1 && renderer.Scores.Messages[0].Text == "WINNER ! ! !",
            "ordinary award expires after two seconds"
        );
        renderer.Update(0);
        Close(renderer.Scores.Messages[0].Age, 2.101f, "pause preserves score flash lifetime");
        renderer.Update(3);
        Check(
            renderer.Scores.Messages.Count == 1 && renderer.Scores.OverheadMessage(0) == null,
            "overhead clears on expiry while HUD preserves the source final Update"
        );
        renderer.Update(.001f);
        Check(
            renderer.Scores.Messages.Count == 0 && renderer.Scores.OverheadMessage(0) == null,
            "winner popup expires after five seconds"
        );
        renderer.Reset();
        renderer.Update(1);
        renderer.Consume([Death(20, 1, 0, 3, true)], world, .4f);
        var plume = renderer.Effects.Active.Single(e => e.Name == "SideScorePlum");
        Check(
            plume.Points == 3 && plume.Text == "+3" && plume.CameraRelative,
            "aged KO plume retains assigned points text and camera parent"
        );
        Check(
            renderer.Effects.Active.Count(e => e.Name == "HitParticle") > 0,
            "aged KO plume emits source particles during reconstruction"
        );
        Check(
            plume.TextColorFloat != renderer.ColorFor(world, 0).ToVector4(),
            "aged KO text reconstructs its flash before first draw"
        );
        renderer.Reset();
        return checks.ToArray();
    }
}
