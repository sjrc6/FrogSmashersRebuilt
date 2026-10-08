using System.Collections;
using System.Reflection;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyBunker(string? captureDirectory)
    {
        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        var field = typeof(Renderer).GetField("bunkerEffects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        BunkerEffects? State() => (BunkerEffects?)field.GetValue(renderer);
        int mapIndex = assets.Data.Maps.FindIndex(map => map.Id == "7Showdown");
        var world = new World(assets.Data, new GameRules(mapOrder: [mapIndex]));
        ulong simulation = world.HashState();
        using var audio = new Audio(assets, true);
        var shots = (IList)
            typeof(Audio).GetField("shots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audio)!;
        int explosions = 0;
        void Explode()
        {
            explosions++;
            audio.PlayBackgroundExplosion();
        }

        renderer.BackgroundExplosion += Explode;
        bool shake = renderer.ShakeEnabled;
        try
        {
            renderer.Reset();
            renderer.ShakeEnabled = false;
            audio.UpdateFlights(world, 0);
            renderer.UpdateStageEffects(world, 0);
            renderer.Update(0);
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            var dim = ReadFrame();
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "bunker-dim.png"));
            renderer.Update(1 / 60f);
            renderer.UpdateStageEffects(world, 1 / 60f);
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            var flash = ReadFrame();
            Check(explosions == 1 && shots.Count == 1, "The first bunker explosion plays one sound");
            var sound = (SoundEffectInstance)shots[0]!.GetType().GetProperty("Instance")!.GetValue(shots[0])!;
            Check(
                sound.State == SoundState.Playing && Math.Abs(sound.Volume - .25f * audio.Volume) < .001f,
                "The original explosion group and source volume reach the audio backend"
            );
            Check(!dim.SequenceEqual(flash), "The light overlay flashes in the rendered arena");
            Check(
                renderer.Camera.Position == Vector2.Zero,
                "Disabling screen shake leaves the camera steady during explosions"
            );
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "bunker-flash.png"));

            audio.SetPaused(true);
            renderer.Update(0);
            renderer.UpdateStageEffects(world, 0);
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            Check(
                flash.SequenceEqual(ReadFrame()) && explosions == 1 && sound.State == SoundState.Paused,
                "Local pause freezes rendered light and pauses explosion audio"
            );
            audio.SetPaused(false);
            renderer.Rewind(0);
            renderer.UpdateStageEffects(world, 0);
            Check(explosions == 1, "Rollback does not replay cosmetic explosion audio");

            for (int frame = 0; frame < 60 * 60 && !State()!.Dust.Any(); frame++)
            {
                renderer.Update(1 / 60f);
                renderer.UpdateStageEffects(world, 1 / 60f);
            }
            Check(State()!.Dust.Any(), "Strong bunker explosions spawn visible particle emitters");
            for (int frame = 0; frame < 90; frame++)
            {
                renderer.Update(1 / 60f);
                renderer.UpdateStageEffects(world, 1 / 60f);
            }
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            var withDust = ReadFrame();
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "bunker-dust.png"));
            var dust = (IList)
                typeof(BunkerEffects)
                    .GetField("dust", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(State())!;
            dust.Clear();
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            Check(!withDust.SequenceEqual(ReadFrame()), "Dust particles contribute pixels to the actual stage render");
            Check(world.HashState() == simulation, "Stage effects leave simulation state unchanged");

            var oldState = State();
            world.Match.RoundNumber++;
            renderer.UpdateStageEffects(world, 0);
            Check(State() != oldState && !State()!.Dust.Any(), "Starting a new round clears the old burst and timer");
            renderer.ShakeEnabled = true;
            renderer.Update(1 / 60f);
            renderer.UpdateStageEffects(world, 1 / 60f);
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            Check(renderer.Camera.Position.Length() > .01f, "An enabled bunker shake moves the rendered camera");
            int beforeScores = explosions;
            world.Match.Phase = MatchPhase.RoundScores;
            audio.UpdateFlights(world, 0);
            renderer.UpdateStageEffects(world, 10);
            Check(
                State() == null && explosions == beforeScores && shots.Count == 0,
                "Score screens clear dust and stop arena explosions"
            );
            world.Match.Phase = MatchPhase.Playing;
            renderer.UpdateStageEffects(world, 1 / 60f);
            renderer.UpdateStageEffects(null, 10);
            Check(State() == null, "Leaving gameplay clears the bunker presentation");
            renderer.UpdateStageEffects(new World(assets.Data, new GameRules(mapOrder: [0])), 10);
            Check(State() == null, "Other stages cannot inherit bunker effects");
        }
        finally
        {
            renderer.BackgroundExplosion -= Explode;
            renderer.ShakeEnabled = shake;
            renderer.Reset();
        }
        return ["Bunker light, dust, audio, shake toggle, pause, rollback and scene lifecycle checks passed"];
    }
}
