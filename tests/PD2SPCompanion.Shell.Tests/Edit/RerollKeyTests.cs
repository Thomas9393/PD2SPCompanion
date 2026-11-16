using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// Ctrl + R is the Demonic Cube's reroll (checked first for safety;
/// a bare R at first was unsafe - one stray letter from an
/// item changed, and a jump to a name over the vault's list). It rolls the item on the Edit page, once a press
/// however long it is held; a bare R rolls nothing; not from a box of words; and not in a play mode that has no
/// cube.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RerollKeyTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private static void Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    private static void Key(Avalonia.Controls.Window window, bool down, bool ctrl = true, bool shift = false) =>
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = Avalonia.Input.Key.R,
            KeyModifiers = (ctrl ? KeyModifiers.Control : KeyModifiers.None) | (shift ? KeyModifiers.Shift : KeyModifiers.None),
        });

    /// <summary>The Edit page in a window of its own, an item with something to roll selected; how many times the strip said "rerolled".</summary>
    private void OnThePage(string mode, Action<Avalonia.Controls.Window, Func<int>> then)
    {
        using var held = program.InMode(mode);
        program.On(() =>
        {
            // A unique not corrupted, as the game's cube takes (Casual's cube is dimmed on a corrupted one).
            program.Panes("Personal Stash");
            program.Select("Witchwild String", "Personal Stash");
            // A box of words beside the page, as a search or a name is: the keys typed there are its own.
            var window = new Avalonia.Controls.Window
            {
                Width = 1000, Height = 1000, Background = Brushes.Black,
                Content = new DockPanel { Children = { new TextBox { Name = "Words", Width = 120 }, new VP_View_Inv_R { DataContext = Edit } } },
            };
            window.Show();
            Pump();

            int rolls = 0;
            var strip = program.Main.Session.RightMessages;
            void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
            {
                if (change.PropertyName == nameof(strip.Text) && strip.Text.Contains("rerolled"))
                    rolls++;
            }

            strip.PropertyChanged += Heard;
            try
            {
                then(window, () => rolls);
            }
            finally
            {
                strip.PropertyChanged -= Heard;
                program.Deselect();
                window.Close();
            }
        });
    }

    [CorpusFact]
    public void Ctrl_R_rolls_the_item_once_a_press_however_long_it_is_held_and_a_bare_R_rolls_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        OnThePage("Casual", (window, rolls) =>
        {
            Assert.True(Edit.CanReroll);
            Assert.Equal("Reroll all stats (Ctrl + R)", Edit.RerollHint);

            // A letter by itself is no key of the cube's.
            Key(window, down: true, ctrl: false);
            Key(window, down: false, ctrl: false);
            Pump();
            Assert.Equal(0, rolls());

            Key(window, down: true);
            Pump();
            Assert.Equal(1, rolls());
            Assert.StartsWith("Witchwild String rerolled", program.Main.Session.RightMessages.Text);

            // Held: the keyboard says "down" again and again, and nothing more is rolled.
            Key(window, down: true);
            Key(window, down: true);
            Pump();
            Assert.Equal(1, rolls());

            // Let go and pressed again: one more.
            Key(window, down: false);
            Key(window, down: true);
            Pump();
            Assert.Equal(2, rolls());
            Key(window, down: false);

            // With Shift it is the Forge's fuller roll, and no key on an item that is someone's.
            Key(window, down: true, shift: true);
            Key(window, down: false, shift: true);
            Pump();
            Assert.Equal(2, rolls());
        });
    }

    /// <summary>
    /// The Forge shows the same page over its own item: the
    /// key rolls what is being forged.
    /// </summary>
    [CorpusFact]
    public void On_the_Forge_Ctrl_R_rolls_what_is_being_forged()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new PD2SPCompanion.Shell.Tests.Forge.ForgeBench(program);
        program.On(() => bench.Find("war trav", "War Traveler"));
        Assert.True(bench.Until(() => bench.Panel.Stats.Count > 0));

        program.On(() =>
        {
            var window = new Avalonia.Controls.Window { Width = 900, Height = 1000, Background = Brushes.Black, Content = new VP_View_Inv_R { DataContext = bench.Panel } };
            window.Show();
            Pump();
            try
            {
                Assert.True(bench.Panel.CanReroll);

                // War Traveler's damage and defense roll in ranges: sixteen presses do not all land where it stood.
                string Numbers() => string.Join(",", bench.Panel.Stats.Select(row => row.Value));
                string was = Numbers();
                bool moved = false;
                for (int press = 0; press < 16 && !moved; press++)
                {
                    Key(window, down: true);
                    Key(window, down: false);
                    Pump();
                    moved = Numbers() != was;
                }

                Assert.True(moved, "sixteen presses of Ctrl + R left the item on the Forge as it was");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// A number typed into a row leaves the focus in its box, and the key is pressed next: it rolls from there.
    /// From a box of words - a search, a name - the keys are the text's.
    /// </summary>
    [CorpusFact]
    public void It_rolls_from_a_number_box_and_not_from_a_box_of_words()
    {
        if (!program.Ready(changes: true))
            return;

        OnThePage("Casual", (window, rolls) =>
        {
            var number = window.GetVisualDescendants().OfType<NumericTextBox>().First(each => each.IsEffectivelyVisible && each.IsEffectivelyEnabled);
            number.Focus();
            Pump();
            Assert.True(number.IsFocused);

            Key(window, down: true);
            Key(window, down: false);
            Pump();
            Assert.Equal(1, rolls());

            // The box of words beside the page, with the focus: nothing is rolled.
            var words = window.GetVisualDescendants().OfType<TextBox>().First(box => box.Name == "Words");
            words.Focus();
            Pump();
            Assert.True(words.IsFocused);

            Key(window, down: true);
            Key(window, down: false);
            Pump();
            Assert.Equal(1, rolls());
        });
    }

    [CorpusFact]
    public void In_Legit_there_is_no_cube_and_the_key_rolls_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        OnThePage("Legit", (window, rolls) =>
        {
            Assert.False(Edit.Rules.EditsAffixes);

            Key(window, down: true);
            Key(window, down: false);
            Pump();
            Assert.Equal(0, rolls());
            Assert.False(Edit.RerollByKey());
        });
    }
}
