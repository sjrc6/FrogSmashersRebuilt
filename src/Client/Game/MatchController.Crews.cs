using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed partial class MatchController
{
    private readonly Dictionary<int, InputFrame> selectionInputs = new();

    public int CelebrationSkipDevice =>
        !ReplayPlayback && World?.Match.Phase == MatchPhase.RoundFinished
            ? PlayerViews.ElementAtOrDefault(World.Match.Winner)?.Device ?? -1
            : -1;

    public bool TrySkipCelebration(int device)
    {
        int winnerDevice = CelebrationSkipDevice;
        if (winnerDevice < 0)
            return false;
        bool matchingDevice = winnerDevice == device || winnerDevice < 2 && device is >= 0 and < 2;
        if (!matchingDevice)
            return false;
        commands[winnerDevice] = new(MatchCommandKind.SkipCelebration);
        return true;
    }

    private void UpdateCrewSelection()
    {
        var world = World!;
        if (world.Match.Phase is not (MatchPhase.Selecting or MatchPhase.ChoosingCrews))
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
            int direction = input.X != previous.X ? input.X : 0;
            selectionInputs[device] = input;
            if (world.Match.Phase == MatchPhase.ChoosingCrews)
            {
                if (direction != 0)
                    commands[device] = new(MatchCommandKind.ChooseCrew, (byte)(direction < 0 ? 0 : 1));
                else if ((pressed & InputButtons.Jump) != 0)
                    commands[device] = new(MatchCommandKind.ToggleReady);
                continue;
            }
            int team = world.Match.Team(world.Rules, slot);
            int inward = team == 0 ? 1 : -1;
            int selected = world.Match.TeamSelections[team];
            if (selected == slot)
            {
                if (direction == -inward && world.Match.CanBackOut(world.Rules, slot))
                    commands[device] = new(MatchCommandKind.BackOutFighter);
                else if ((pressed & InputButtons.Jump) != 0)
                    commands[device] = new(MatchCommandKind.ToggleReady);
            }
            else if (selected < 0 && direction == inward)
                commands[device] = new(MatchCommandKind.SelectFighter, (byte)slot);
        }
    }
}
