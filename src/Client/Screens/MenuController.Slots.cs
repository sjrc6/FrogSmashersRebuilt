using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private readonly SlotEditGesture slotGesture = new();
    private SlotType? slotPreview;
    public SlotType SelectedSlotType => slotPreview ?? game.Lobby.Roster.Slots[SelectedSeat].Type;

    public static string SlotLabel(SlotType type) =>
        type switch
        {
            SlotType.Private => "PRIVATE",
            SlotType.Friend => "FRIEND",
            SlotType.Local => "LOCAL",
            SlotType.Closed => "CLOSED",
            SlotType.Cpu => "CPU",
            _ => "OPEN",
        };

    private void UpdateRoomSelection(MenuInput input)
    {
        if (input.Back)
        {
            slotGesture.Reset();
            slotPreview = null;
            Back();
            return;
        }
        int change = slotGesture.Update(input);
        if (change != 0)
            CycleSlot(change, preview: input.AcceptHeld);
        if (input.AcceptReleased && slotPreview is { } type)
        {
            game.Lobby.Edit(SelectedSeat, type);
            slotPreview = null;
        }
        if (!input.AcceptHeld && !input.AcceptReleased)
        {
            int cell = MenuLayout.RoomCell(SelectedSeat);
            int x = cell % 3,
                y = cell / 3;
            if (input.Horizontal != 0)
                do
                {
                    x = Math.Clamp(x + input.Horizontal, 0, 2);
                } while (y == 1 && x == 1);
            if (input.Vertical != 0)
                do
                {
                    y = Math.Clamp(y + input.Vertical, 0, 2);
                } while (y == 1 && x == 1);
            int target = y * 3 + x;
            SelectRoom(target > 4 ? target - 1 : target);
        }
        if (input.Remove)
            RemoveSelectedPlayer();
        if (input.ApplyAll)
            ApplySelectedType();
        if (Pointer() is not Point point)
            return;
        bool clicked = game.Controls.MousePressed || game.Controls.MouseRightPressed;
        if (game.Controls.MousePressed && MenuLayout.SlotBack.Contains(point))
        {
            Back();
            return;
        }
        if (game.Controls.MousePressed && MenuLayout.SlotAction(SelectedSeat, 2).Contains(point))
        {
            ApplySelectedType();
            return;
        }
        if (game.Controls.MousePressed && MenuLayout.SlotAction(SelectedSeat, 1).Contains(point))
        {
            RemoveSelectedPlayer();
            return;
        }
        if (!game.Controls.MouseMoved && !clicked)
            return;
        for (int room = 0; room < 8; room++)
            if (MenuLayout.Room(room).Contains(point))
            {
                if (input.AcceptHeld)
                    return;
                SelectRoom(room);
                if (clicked)
                    CycleSlot(game.Controls.MouseRightPressed ? -1 : 1);
                return;
            }
    }

    private void SelectRoom(int room)
    {
        menuSoundPending |= SelectedSeat != room;
        SelectedSeat = room;
    }

    private void CycleSlot(int direction, bool preview = false)
    {
        var slot = game.Lobby.Roster.Slots[SelectedSeat];
        if (slot.Player is { Cpu: false })
        {
            game.Toasts.Show("SLOT OCCUPIED");
            return;
        }
        SlotType[] types = game.Lobby.Online == null ? [SlotType.Local, SlotType.Cpu] : Enum.GetValues<SlotType>();
        var type = types[Wrap(Array.IndexOf(types, SelectedSlotType) + direction, types.Length)];
        if (preview)
        {
            slotPreview = type;
            menuSoundPending = true;
        }
        else if (game.Lobby.Edit(SelectedSeat, type))
            menuSoundPending = true;
    }

    private void ApplySelectedType()
    {
        game.Lobby.ApplySlotType(SelectedSlotType);
        slotPreview = null;
        menuSoundPending = true;
    }

    private void RemoveSelectedPlayer()
    {
        if (game.Lobby.Roster.Slots[SelectedSeat].Player is not { } player)
            return;
        if (player.Peer != game.Lobby.LocalPeer)
            game.Online.Lobby!.Kick(player.Peer, false);
        else
            game.Lobby.Remove(player.Peer, player.Id);
        menuSoundPending = true;
    }
}
