using System.Reflection;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyModifiers(string? captureDirectory)
    {
        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        var effects = (EffectSystem)
            typeof(Renderer).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        var characters = (CharacterPresentation[])
            typeof(Renderer)
                .GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(renderer)!;
        var map = new MapData
        {
            Id = "modifier-visuals",
            OrthoSize = 18,
            Spawns = [new() { X = -15 }, new() { X = 15 }],
        };
        var world = new World(map, new GameRules(modifiers: new() { FlyTumble = true, ChargeTongues = true }));
        var player = world.Players[0];
        player.Alive = true;
        player.X = -10;
        player.Y = 4;
        player.Mode = CharacterMode.Tongue;
        player.TonguePhase = TonguePhase.Charging;
        player.TongueY = -1;
        world.Fly.Active = true;
        world.Fly.X = 10;
        world.Fly.Y = 8;
        world.Fly.Phase = FlyPhase.Tumbling;
        world.Fly.HitsTaken = 4;
        world.Fly.LastHitBy = 0;
        world.Fly.VX = 40;
        world.Fly.VY = 10;
        world.Fly.TimeSinceHit = Fixed.FromDecimal(.3m);
        ulong hash = world.HashState();
        renderer.Reset();
        renderer.Update(1 / 60f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        Check(
            characters[0].Sprite == assets.Frame("tongue", 0) && !characters[0].TongueVisible,
            "Tongue charging holds the cheeks-puffed frame without showing a tongue"
        );
        foreach (string name in new[] { "FaderTrail", "HitParticle", "LineParticle", "SmokeRing", "SmokeRingBack" })
            Check(effects.Active.Any(effect => effect.Name == name), "Fly inherits volleyball flight effect: " + name);
        Check(
            effects.Active.Where(effect => effect.Owner == 9).All(effect => effect.Sprite == "fly-tumble"),
            "The fly and its silhouettes use the wingless tumble sprite"
        );
        renderer.Update(.2f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        Check(characters[0].Sprite == assets.Frame("tongue", 0), "The charge pose does not advance while held");
        var flyPose = new FlyPresentation();
        flyPose.Update(assets, world.Fly, .1f);
        float flightAngle = MathF.Atan2(10, 40) - MathF.PI / 2;
        Check(Math.Abs(flyPose.Rotation - flightAngle) < .001f, "The tumbling fly faces its velocity");
        flyPose.Update(assets, world.Fly, 1);
        Check(Math.Abs(flyPose.Rotation - flightAngle) < .001f, "Time alone does not spin the fly");
        Check(world.HashState() == hash, "Modifier presentation does not change simulation state");
        if (captureDirectory != null)
            SaveFrame(Path.Combine(captureDirectory, "fly-tumble-and-tongue-charge.png"));

        player.Mode = CharacterMode.Bouncing;
        player.TonguePhase = TonguePhase.Extending;
        player.TongueParried = true;
        player.TongueDistance = 8;
        player.TongueX = 1;
        player.TongueY = 0;
        player.HitstopTicks = 10;
        player.HitstopScale = 0;
        renderer.Update(.01f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        Check(
            characters[0].TongueVisible && characters[0].TongueOffset.X > 7,
            "The tongue stays extended while its owner is frozen by a parry"
        );
        player.TongueParried = false;
        player.HitstopTicks = 0;
        renderer.Update(.01f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        Check(!characters[0].TongueVisible, "The tongue disappears when parry hitstop ends");

        renderer.Reset();
        world.Fly.Phase = FlyPhase.Tumbling;
        world.Fly.HitstopTicks = World.TickRate / 2;
        var hit = new SimulationEvent(
            world.TickNumber,
            0,
            SimulationEventKind.FlyHit,
            -1,
            0,
            world.Fly.X,
            world.Fly.Y,
            20,
            ComboHits: 1,
            HitKind: HitKind.FlyExplosion,
            VelocityX: 20,
            HitstopSeconds: Fixed.FromDecimal(.5m),
            HitEffectX: world.Fly.X,
            HitEffectY: world.Fly.Y
        );
        renderer.Consume([hit], world);
        renderer.Update(.1f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        foreach (string name in new[] { "HitEffect", "HitStar", "HitStarPowerHit" })
            Check(
                effects.Active.Any(effect => effect.Name == name && Math.Abs(effect.Life - .5f) < .001f),
                "Fly explosion uses half-second high-hitstun effect: " + name
            );
        if (captureDirectory != null)
            SaveFrame(Path.Combine(captureDirectory, "fly-explosion-hitstun.png"));
        renderer.Update(.39f);
        renderer.DrawWorld(world, null, 1, showGameplayUi: false);
        Check(
            effects.Active.Any(effect => effect.Name == "HitStarPowerHit" && !effect.Dead),
            "Explosion stars remain visible until the half-second hitstun ends"
        );
        renderer.Rewind(world.TickNumber);
        Check(effects.Active.All(effect => effect.Tick < world.TickNumber), "Fly effects rewind with rollback");
        renderer.Reset();
        return
        [
            "Charged tongue pose, wingless fly trails, half-second explosion stars and rollback cleanup render correctly",
        ];
    }
}
