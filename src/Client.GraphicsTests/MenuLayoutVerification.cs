using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyMenuLayout()
    {
        var device = renderer.Batch.GraphicsDevice;
        var original = device.Viewport;
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            checks.Add(message);
        }
        MenuEntry[] entries = Enumerable
            .Range(0, 30)
            .Select(index => new MenuEntry(
                "row-" + index,
                index % 3 == 0 ? "A LONG MATCH OPTION LABEL" : "OPTION",
                Value: index % 3 == 0 ? "A LONG ARENA NAME FOR WRAPPING" : "1",
                ValueSample: index % 3 == 0 ? "A LONG ARENA NAME FOR WRAPPING" : "999",
                SeparatorBefore: index % 5 == 0
            ))
            .ToArray();
        try
        {
            foreach (
                var size in new[]
                {
                    new Point(640, 360),
                    new Point(960, 540),
                    new Point(1280, 720),
                    new Point(1365, 768),
                    new Point(1920, 1080),
                    new Point(2560, 1440),
                    new Point(3840, 2160),
                }
            )
            {
                device.Viewport = new Viewport(0, 0, size.X, size.Y);
                foreach (var view in new[] { new Rectangle(0, 0, 1280, 720), new Rectangle(50, 20, 960, 540) })
                {
                    Rectangle? panel = null;
                    var reached = new HashSet<int>();
                    for (int selected = 0; selected < entries.Length; selected++)
                    {
                        var layout = MenuLayout.Measure(
                            GameScreen.MatchSettings,
                            entries,
                            assets.Font,
                            selected: selected,
                            viewport: view
                        );
                        panel ??= layout.Panel;
                        Check(
                            panel == layout.Panel && view.Contains(layout.Panel),
                            $"Menu panel stays fixed and bounded at {size}, row {selected}, viewport {view}"
                        );
                        Check(
                            !layout.Rows[selected].Bounds.IsEmpty,
                            $"Selected row remains visible at {size}, row {selected}, viewport {view}"
                        );
                        Check(
                            view.Contains(MenuLayout.PageButton(layout, 1)),
                            $"Pagination fits the viewport at {size}, row {selected}, viewport {view}"
                        );
                        for (int index = 0; index < entries.Length; index++)
                        {
                            var row = layout.Rows[index];
                            if (row.Bounds.IsEmpty)
                                continue;
                            reached.Add(index);
                            Check(
                                layout.Panel.Contains(row.Bounds)
                                    && row.Bounds.Top >= MenuLayout.HeaderLine(GameScreen.MatchSettings, layout.Panel)
                                    && row.Bounds.Bottom
                                        <= MenuLayout.FooterLine(GameScreen.MatchSettings, layout.Panel),
                                $"Visible row {index} stays between header and footer at {size}"
                            );
                            Check(
                                assets.Font.Measure(row.Text.Label).X <= layout.Panel.Width - 64
                                    && (
                                        row.Text.Value == null
                                        || assets.Font.Measure(row.Text.Value).X <= layout.Panel.Width - 64
                                    ),
                                $"Wrapped text fits row {index} at {size}"
                            );
                        }
                    }
                    Check(reached.Count == entries.Length, $"Every dynamic row is reachable at {size}");
                }
                var friends = Enumerable
                    .Range(0, 30)
                    .Select(index => new MenuEntry(
                        "friend-" + index,
                        "A LONG FRIEND NAME WITH A PROFILE PICTURE",
                        Avatar: (ulong)index,
                        Value: "INVITED"
                    ))
                    .ToArray();
                var friendLayout = MenuLayout.Measure(GameScreen.InviteFriends, friends, assets.Font, selected: 20);
                Check(
                    friendLayout.Paginated
                        && friendLayout.Rows.Where(row => !row.Bounds.IsEmpty).All(row => row.Bounds.Height >= 54),
                    $"Friend portraits reserve their height even before avatar data arrives at {size}"
                );
                var small = new MenuEntry("number", "VARIABLE TARGET", Value: "1", ValueSample: "999");
                var large = small with { Value = "999" };
                var first = MenuLayout.Measure(GameScreen.Settings, [small], assets.Font);
                var second = MenuLayout.Measure(GameScreen.Settings, [large], assets.Font);
                Check(
                    first.Panel == second.Panel && first.Rows[0].Bounds == second.Rows[0].Bounds,
                    $"Reserved value width prevents layout jumps at {size}"
                );
                var binding = new MenuEntry("binding", "LONG CONTROLLER ACTION", Key: Keys.LeftShift);
                var measured = MenuLayout.Measure(
                    GameScreen.Bindings,
                    [binding],
                    assets.Font,
                    accessorySize: _ => new Vector2(120, 40)
                );
                Check(
                    measured.Rows[0].Bounds.Height >= 54
                        && assets.Font.Measure(measured.Rows[0].Text.Label).X <= measured.Panel.Width - 64 - 136,
                    $"Binding drawing and hit testing reserve measured glyph space at {size}"
                );
                var modifiers = ModifierCatalog
                    .All.Select(definition => new MenuEntry(
                        definition.Id,
                        definition.Label,
                        Value: definition.Value(GameModifiers.Default),
                        ValueSample: definition.ValueSample,
                        Help: definition.Help
                    ))
                    .ToArray();
                Rectangle? modifierPanel = null;
                for (int selected = 0; selected < modifiers.Length; selected++)
                {
                    var layout = MenuLayout.Measure(GameScreen.Modifiers, modifiers, assets.Font, selected: selected);
                    modifierPanel ??= layout.Panel;
                    Check(
                        layout.Panel == modifierPanel && !layout.Rows[selected].Bounds.IsEmpty,
                        $"Every modifier stays visible within a stable panel at {size}"
                    );
                    string help = assets.Font.Wrap(
                        "ENABLE BODY BOUNCING. " + modifiers[selected].Help,
                        layout.Panel.Width - 40,
                        3
                    );
                    Check(
                        assets.Font.Measure(help).Y <= 90 && assets.Font.Measure(help).X <= layout.Panel.Width - 40,
                        $"Modifier help fits above the footer buttons at {size}"
                    );
                }
            }
        }
        finally
        {
            device.Viewport = original;
        }
        return [$"Menu layout: {checks.Count} bounds, pagination, text and shared measurement checks passed"];
    }
}
