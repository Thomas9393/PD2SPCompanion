using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// An item's lines drawn by one control (ItemFactLines).
/// It must draw a line as the line's controls drew it, and
/// tell UI Automation what they told it: the window tests read a tooltip by its texts.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ItemFactLinesTests(Program program)
{
    private static void Pump(int rounds = 4)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    /// <summary>
    /// Lines of every sort a line can be: plain, a property with its bracket, a line too long for the width -
    /// with a bracket that then has no room beside it - a corruption's and a desecration's asterisks, alone and
    /// together, and each tone.
    /// </summary>
    private static List<ItemFact> Lines() =>
    [
        new("Stone Barb [4]", Tone: ItemFact.Name),
        new("Two-Hand Damage: 31 to 94", Tone: ItemFact.Fact),
        new("+123% Enhanced Damage", "[100-150]", ItemFact.Magic),
        new("Adds 12-34 Fire Damage to every attack made while the moon is out", "[10-14 to 30-40]", ItemFact.Magic),
        new("+2 to All Skills", "[1-2]", ItemFact.Corrupt, Corrupted: true),
        new("+15% Chance to Pierce", "", ItemFact.Magic, Corrupted: true, Desecrated: true),
        new("Desecrated", Tone: ItemFact.Desecrate, Desecrated: true),
        new("Required Strength: 156", Tone: ItemFact.Unmet),
        new("Tal + Thul + Ort + Amn", Tone: ItemFact.Runes),
        new("Dyed Black", Tone: ItemFact.Skin),
        new("+50 to Life (2 items)", Tone: ItemFact.SetBonus),
        new("Cannot be equipped", Tone: ItemFact.Warning),
    ];

    private static Avalonia.Controls.Window Shown(Control content, double width)
    {
        var window = new Avalonia.Controls.Window
        {
            Width = 400, Height = 500, Background = Brushes.Black,
            Content = new Border
            {
                Width = width, Child = content,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            },
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://PD2SPCompanion.Shell/")) { Source = new Uri("avares://PD2SPCompanion.Shell/Styles/CommonControls/ItemWording.axaml") });
        window.Show();
        Pump();
        return window;
    }

    private static byte[] Pixels(Avalonia.Controls.Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var locked = frame.Lock();
        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Against the line's own controls, which a card still shows its lines by (the ItemLine template): the same
    /// picture to the pixel, at a width the long line must wrap in and at one it need not.
    /// </summary>
    [CorpusTheory]
    [InlineData(190)]
    [InlineData(380)]
    public void The_lines_are_drawn_as_their_controls_drew_them(double width)
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var lines = Lines();
            Assert.True(Application.Current!.TryFindResource("ItemLine", out var template));

            var old = Shown(new ItemsControl { ItemsSource = lines, ItemTemplate = (IDataTemplate)template! }, width);
            var drawn = Shown(new ItemFactLines { Items = lines }, width);
            try
            {
                var before = Pixels(old);
                Assert.Contains(before, value => value is not 0 and not 255);
                Assert.Equal(before, Pixels(drawn));

                // And it takes the room they took.
                Assert.Equal(old.GetVisualDescendants().OfType<ItemsControl>().Single().Bounds.Size,
                    drawn.GetVisualDescendants().OfType<ItemFactLines>().Single().Bounds.Size);
            }
            finally
            {
                old.Close();
                drawn.Close();
            }
        });
    }

    [CorpusFact]
    public void A_rule_takes_nine_pixels_and_the_lines_no_control()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var lines = new List<ItemFact> { new("One", Tone: ItemFact.Fact), new("Two", Tone: ItemFact.Fact) };
            var ruled = new List<ItemFact> { lines[0], new("", Tone: ItemFact.Rule), lines[1] };

            var plain = Shown(new ItemFactLines { Items = lines, IsCentered = true }, 200);
            var with = Shown(new ItemFactLines { Items = ruled, IsCentered = true }, 200);
            try
            {
                var a = plain.GetVisualDescendants().OfType<ItemFactLines>().Single();
                var b = with.GetVisualDescendants().OfType<ItemFactLines>().Single();
                Assert.Equal(a.Bounds.Height + 9, b.Bounds.Height);
                Assert.Empty(b.GetVisualDescendants());
                Assert.NotEqual(Pixels(plain), Pixels(with));
            }
            finally
            {
                plain.Close();
                with.Close();
            }
        });
    }

    [CorpusFact]
    public void UI_Automation_is_told_of_a_text_a_line_and_one_a_bracket()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var lines = new List<ItemFact>
            {
                new("Stone Barb [4]", Tone: ItemFact.Name),
                new("", Tone: ItemFact.Rule),
                new("+2 to All Skills", "[1-2]", ItemFact.Corrupt, Corrupted: true),
                new("+15% Chance to Pierce", "", ItemFact.Magic, Corrupted: true, Desecrated: true),
            };
            var window = Shown(new ItemFactLines { Items = lines }, 300);
            try
            {
                var control = window.GetVisualDescendants().OfType<ItemFactLines>().Single();
                var peers = ControlAutomationPeer.CreatePeerForElement(control).GetChildren();

                Assert.Equal(["Stone Barb [4]", "+2 to All Skills*", "[1-2]", "+15% Chance to Pierce**"], peers.Select(peer => peer.GetName()));
                Assert.All(peers, peer => Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType()));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>The tooltip of an item of the character's is built with it: one control for all its lines.</summary>
    [CorpusFact]
    public void An_items_tooltip_draws_its_lines_by_the_one_control()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var tile = new[] { "Inventory", "Personal Stash" }.SelectMany(name => program.Grid(name).Items)
                .OrderByDescending(item => item.Tooltip.Count).First();
            var window = Shown(new ItemActionTip { Tile = tile }, 380);
            try
            {
                var lines = window.GetVisualDescendants().OfType<ItemFactLines>().Single();
                Assert.Same(tile.Tooltip, lines.Items);
                Assert.True(lines.IsCentered);
                Assert.True(lines.Bounds.Height > tile.Tooltip.Count * 9);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// A host that arranges the lines narrower than they measured - by a pixel, as a tooltip does at 125%, its padding
    /// rounded to the screen's pixels - and clips them to the height they asked for, as the tooltip's scroller does.
    /// </summary>
    private sealed class Narrower(double by) : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(availableSize);
            return Child?.DesiredSize ?? default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(0, 0, Math.Max(0, Child.DesiredSize.Width - by), Child.DesiredSize.Height));
            return Child?.DesiredSize ?? finalSize;
        }
    }

    /// <summary>
    /// A Necromancer's amulet in an Amazon's inventory: the tooltip did not show the FCR. The
    /// line before it - "+3 to Summoning Skills (Necromancer Only)" with its bracket beside it - just fitted the width
    /// the lines were measured at; given a pixel less at 125% - the tooltip's padding rounded to the screen's pixels -
    /// the lines were laid out again, the bracket went under its words, and the last line fell below the height the
    /// tooltip had made room for (seen in the window). A width short by a pixel or less is the measured one.
    /// </summary>
    [CorpusFact]
    public void A_host_a_pixel_narrower_cuts_no_line_off()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            List<ItemFact> lines =
            [
                new("+3 to Summoning Skills (Necromancer Only)", "[1 - 3]", ItemFact.Magic),
                new("+30% Faster Cast Rate", Tone: ItemFact.Warning, Corrupted: true, Desecrated: true),
            ];

            // At the left, as a card has them: a narrower host then moves nothing that is not laid out again.
            ItemFactLines Lines() => new() { Items = lines };

            // The least width at which the bracket still stands beside its words: the lines as tight as they go, as
            // the amulet's were - a pixel less, and the bracket goes under. Asked in a window, with the lines' styles.
            double Tall(double wide)
            {
                var window = Shown(Lines(), wide);
                try
                {
                    return window.GetVisualDescendants().OfType<ItemFactLines>().Single().Bounds.Height;
                }
                finally
                {
                    window.Close();
                }
            }

            double height = Tall(390), width = 120;
            while (width < 390 && Tall(width) > height)
                width++;

            byte[] Drawn(double by)
            {
                var window = Shown(new Border { ClipToBounds = true, Height = height, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = new Narrower(by) { Child = Lines() } }, width);
                try
                {
                    return Pixels(window);
                }
                finally
                {
                    window.Close();
                }
            }

            var whole = Drawn(0);
            Assert.NotEqual(whole, Drawn(12));
            Assert.Equal(whole, Drawn(1));
        });
    }
}
