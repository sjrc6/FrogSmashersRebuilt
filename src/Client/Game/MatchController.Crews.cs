using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed partial class MatchController
{
    private readonly Dictionary<int, int> fighterChoices = new();
    private readonly Dictionary<int, InputFrame> selectionInputs = new();
    public IReadOnlyDictionary<int, int> FighterChoices => fighterChoices;

    private void UpdateCrewSelection()
    {
        var world = World!;
        if (world.Match.Phase != MatchPhase.Selecting)
        {
            fighterChoices.Clear();
            selectionInputs.Clear();
            return;
        }
        int[] slots =
            Network == null
                ? Enumerable.Range(0, seats.Length).ToArray()
                : Network.LocalSlots.Select(Network.PlayerSlot).Where(slot => slot >= 0).ToArray();
        for (int index = 0; index < seats.Length; index++)
        {
            int device = seats[index].Device;
            if (device < 0)
                continue;
            int slot = slots[index];
            int team = world.Rules.Teams[slot];
            int locked = world.Match.TeamSelections[team];
            if (locked >= 0 && world.Match.Players[locked].Participation == Participation.Active)
                continue;
            int[] candidates = Enumerable
                .Range(0, world.Players.Length)
                .Where(target =>
                    world.Rules.Teams[target] == team
                    && world.Match.Players[target].Participation == Participation.Waiting
                )
                .ToArray();
            if (candidates.Length == 0)
                continue;
            var input = controls.Read(device, consume: false);
            if (!fighterChoices.TryGetValue(slot, out int choice) || !candidates.Contains(choice))
            {
                choice = candidates[0];
                selectionInputs[device] = input;
            }
            var previous = selectionInputs.GetValueOrDefault(device);
            int direction = input.X != 0 ? input.X : -input.Y;
            if (input.X == previous.X && input.Y == previous.Y)
                direction = 0;
            int selected = Array.IndexOf(candidates, choice);
            selected = (selected + Math.Sign(direction) + candidates.Length) % candidates.Length;
            fighterChoices[slot] = candidates[selected];
            var pressed = input.Buttons & ~previous.Buttons;
            if ((pressed & (InputButtons.Jump | InputButtons.Attack)) != 0)
                QueueFighterSelection(device, candidates[selected]);
            selectionInputs[device] = input;
        }
    }
}
