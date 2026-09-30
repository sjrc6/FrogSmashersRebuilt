namespace FrogSmashers.Client;

public readonly record struct LocalSeat(int Device, int Team = 0, int Color = 0, int Id = 0)
{
    public string Label =>
        Device < 0 ? "CPU"
        : Device < 2 ? $"KEYBOARD {Device + 1}"
        : $"CONTROLLER {Device - 1}";
}
