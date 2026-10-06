using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyMenuLayout()
    {
        var original = device.Viewport;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            checks++;
        }
        MenuEntry[] entries = Enumerable
            .Range(0, 30)
            .Select(index => new MenuEntry(
                "row-" + index,
                index % 3 == 0 ? "A LONG MATCH OPTION LABEL" : "OPTION",
                Value: index % 3 == 0 ? "A LONG ARENA NAME FOR WRAPPING" : "1",
                SeparatorBefore: index % 5 == 0
            ))
            .ToArray();
        try
        {
            foreach (var size in new[] { new Point(640, 360), new Point(1365, 768), new Point(3840, 2160) })
            {
                device.Viewport = new Viewport(0, 0, size.X, size.Y);
                foreach (var view in new[] { new Rectangle(0, 0, 1280, 720), new Rectangle(50, 20, 960, 540) })
                {
                    for (int selected = 0; selected < entries.Length; selected++)
                    {
                        var layout = MenuLayout.Measure(
                            GameScreen.MatchSettings,
                            entries,
                            assets.Font,
                            selected: selected,
                            viewport: view
                        );
                        Check(view.Contains(layout.Panel), $"Menu stays inside viewport {view} at {size}");
                        Check(!layout.Rows[selected].Bounds.IsEmpty, $"Row {selected} is reachable at {size}");
                        if (layout.Paginated)
                            Check(view.Contains(MenuLayout.PageButton(layout, 1)), "Page button is reachable");
                        var visible = layout.Rows.Where(row => !row.Bounds.IsEmpty).ToArray();
                        for (int index = 0; index < visible.Length; index++)
                        {
                            var row = visible[index];
                            Check(layout.Panel.Contains(row.Bounds), "Menu rows stay within their panel");
                            Check(
                                visible.Skip(index + 1).All(other => !row.Bounds.Intersects(other.Bounds)),
                                "Visible menu rows do not overlap"
                            );
                            Check(
                                assets.Font.Measure(row.Text.Label).X <= row.Bounds.Width
                                    && (
                                        row.Text.Value == null
                                        || assets.Font.Measure(row.Text.Value).X <= row.Bounds.Width
                                    ),
                                "Wrapped text fits the available row width"
                            );
                        }
                    }
                }
            }
        }
        finally
        {
            device.Viewport = original;
        }
        return [$"Menu layout: {checks} containment, reachability and overlap checks passed"];
    }
}
