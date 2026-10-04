namespace FrogSmashers.Client;

internal sealed class MenuSelection
{
    private GameScreen screen;
    private string? identity;
    public int Index { get; private set; }

    public void Select(int index)
    {
        Index = index;
        identity = null;
    }

    public void Restore(GameScreen screen, int index, string? identity)
    {
        this.screen = screen;
        Index = index;
        this.identity = identity;
    }

    public void Reconcile(GameScreen current, IReadOnlyList<MenuEntry> entries)
    {
        if (
            entries.Any(entry => string.IsNullOrWhiteSpace(entry.Id))
            || entries.Select(entry => entry.Id).Distinct().Count() != entries.Count
        )
            throw new InvalidOperationException("Menu entries require distinct identities");
        if (screen != current)
            identity = null;
        screen = current;
        int found = identity == null ? -1 : entries.ToList().FindIndex(entry => entry.Id == identity);
        Index = found >= 0 ? found : Math.Clamp(Index, 0, Math.Max(0, entries.Count - 1));
        identity = entries.ElementAtOrDefault(Index)?.Id;
    }
}
