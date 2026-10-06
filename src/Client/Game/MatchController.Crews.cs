using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed partial class MatchController
{
    private readonly Dictionary<int, InputFrame> selectionInputs = new();

    public bool TrySkipCelebration(int device)
    {
        if (ReplayPlayback || World?.Match.Phase != MatchPhase.RoundFinished)
            return false;
        var winner = PlayerViews.ElementAtOrDefault(World.Match.Winner);
        if (winner == null || winner.Device < 0)
            return false;
        bool matchingDevice = winner.Device == device || winner.Device < 2 && device < 2;
        if (!matchingDevice)
            return false;
        commands[winner.Device] = new(MatchCommandKind.SkipCelebration);
        return true;
    }

    private void UpdateCrewSelection()
    {
        var world = World!;
        if (world.Match.Phase != MatchPhase.Selecting)
        {
            selectionInputs.Clear();
            return;
        }
        for (int slot = 0; slot < PlayerViews.Count; slot++)
        {
            int device = PlayerViews[slot].Device;
            if (device < 0)
                continue;
            var input = controls.Read(device, consume: false);
            var previous = selectionInputs.GetValueOrDefault(device, input);
            var pressed = input.Buttons & ~previous.Buttons;
            selectionInputs[device] = input;
            int selected = world.Match.TeamSelections[world.Rules.Teams[slot]];
            if (selected == slot)
            {
                if ((pressed & InputButtons.Attack) != 0 && world.Match.CanBackOut(world.Rules, slot))
                    commands[device] = new(MatchCommandKind.BackOutFighter);
                else if ((pressed & InputButtons.Jump) != 0)
                    commands[device] = new(MatchCommandKind.ToggleReady);
            }
            else if (selected < 0 && (pressed & InputButtons.Attack) != 0)
                commands[device] = new(MatchCommandKind.SelectFighter, (byte)slot);
        }
    }
}
