namespace FrogSmashers.Core;

public static class ArenaRotation
{
    public static int[] Available(IReadOnlyList<MapData> maps) =>
        Enumerable.Range(0, maps.Count).Where(index => maps[index].Role == MapRole.Arena).ToArray();

    public static int[] StartingAt(IReadOnlyList<MapData> maps, int first)
    {
        if (first < 0 || first >= maps.Count)
            throw new ArgumentException("Invalid first arena");
        if (maps[first].Role == MapRole.Showdown)
            return [first];
        var available = Available(maps);
        int start = Array.IndexOf(available, first);
        if (start < 0)
            throw new ArgumentException("First arena is not enabled");
        return Enumerable
            .Range(0, available.Length)
            .Select(index => available[(start + index) % available.Length])
            .ToArray();
    }
}
