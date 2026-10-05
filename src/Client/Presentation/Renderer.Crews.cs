using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    public void DrawCrewSelection(World world, IReadOnlyDictionary<int, int> choices)
    {
        compositor.BeginScene(Color.Black);
        compositor.ComposeScene();
        BeginUi();
        Text("CHOOSE YOUR NEXT FIGHTER", Width / 2, 120, scale: 2, center: true);
        var teams = world.Rules.Teams.Take(world.Players.Length).Distinct().Order().ToArray();
        for (int column = 0; column < teams.Length; column++)
        {
            int team = teams[column];
            int x = Width / 2 + (column == 0 ? -260 : 260);
            Text($"TEAM {team + 1}", x, 220, TeamColor(team), 2, center: true);
            int row = 0;
            for (int slot = 0; slot < world.Players.Length; slot++)
            {
                if (world.Rules.Teams[slot] != team)
                    continue;
                var progress = world.Match.Players[slot];
                bool selected = world.Match.TeamSelections[team] == slot;
                bool focused = choices.Values.Contains(slot);
                string state = progress.Stocks == 0 ? "OUT" : $"{progress.Stocks} LIVES";
                string marker =
                    selected ? "READY "
                    : focused ? "> "
                    : "";
                var color =
                    progress.Stocks == 0 ? Color.Gray
                    : selected ? Color.LimeGreen
                    : Color.White;
                Text($"{marker}P{slot + 1}  {state}", x, 295 + row++ * 60, color, 2, center: true);
            }
        }
        Text("MOVE TO CHOOSE - JUMP OR ATTACK TO CONFIRM", Width / 2, 620, center: true);
        Text("ANY TEAMMATE CAN CHOOSE - SURVIVORS KEEP THEIR LIVES", Width / 2, 655, Color.Gray, center: true);
        EndUi();
    }
}
