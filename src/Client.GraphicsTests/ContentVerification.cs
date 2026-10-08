using FrogSmashers.Core;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyContent(string? captureDirectory)
    {
        var textures = assets
            .Data.Sprites.Values.Select(sprite => sprite.Path)
            .Concat(assets.Data.Materials.Values.SelectMany(material => material.Textures.Values))
            .Concat(assets.Data.Maps.SelectMany(map => map.ParticleEmitters).Select(emitter => emitter.TexturePath))
            .Concat(
                assets
                    .Data.Maps.Where(map => map.BunkerEffects != null)
                    .Select(map => map.BunkerEffects!.Dust.TexturePath)
            )
            .Where(path => path.Length > 0)
            .Distinct();
        int loaded = 0;
        foreach (string path in textures)
        {
            var texture = assets.Texture(path);
            if (texture.Width <= 0 || texture.Height <= 0)
            {
                throw new InvalidDataException("Invalid compiled texture: " + path);
            }
            loaded++;
        }
        var checks = new List<string> { $"{loaded} compiled textures load through ContentManager" };
        for (int index = 0; index < assets.Data.Maps.Count; index++)
        {
            renderer.Reset();
            var world = new World(assets.Data, new GameRules(playerCount: 8, mapOrder: [index]));
            renderer.Update(1 / 60f);
            renderer.DrawWorld(world, null, 1);
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, $"map-{index}.png"));
            checks.Add("map renders compiled textures, shaders and emitters: " + world.Map.Id);
        }
        renderer.Reset();
        return checks.ToArray();
    }
}
