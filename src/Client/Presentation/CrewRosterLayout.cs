using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class CrewRosterLayout
{
    private readonly Vector2[] positions = new Vector2[8];
    private bool initialized;
    public IReadOnlyList<Vector2> Positions => positions;

    public void Reset() => initialized = false;

    public void Update(World world, bool selecting, float elapsed)
    {
        int[] teams = world.Rules.Teams.Take(world.Players.Length).Distinct().Order().ToArray();
        for (int side = 0; side < teams.Length; side++)
        {
            int team = teams[side];
            int active = world.Match.TeamSelections[team];
            var bench = Enumerable
                .Range(0, world.Players.Length)
                .Where(slot => world.Rules.Teams[slot] == team && slot != active)
                .ToArray();
            float spacing = selecting ? Math.Min(96, 504f / Math.Max(1, bench.Length - 1)) : 34;
            for (int row = 0; row < bench.Length; row++)
            {
                float x = selecting ? 140 : 30;
                if (side == 1)
                    x = Renderer.Width - x;
                float y = selecting ? 408 + (row - (bench.Length - 1) / 2f) * spacing : 24 + row * spacing;
                Move(bench[row], new Vector2(x, y));
            }
            if (active >= 0)
            {
                float x = selecting ? 460 : 560;
                Move(active, new Vector2(side == 0 ? x : Renderer.Width - x, selecting ? 360 : 34));
            }
        }
        initialized = true;

        void Move(int slot, Vector2 target)
        {
            positions[slot] = initialized ? Vector2.Lerp(positions[slot], target, 1 - MathF.Exp(-6 * elapsed)) : target;
        }
    }
}
