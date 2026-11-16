using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.CustomControls.Base.Input;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>A worded button says its word to a screen reader, not its content's type.</summary>
[Collection(WithTheProgram.Name)]
public sealed class WordedButtonTests(Program program)
{
    [CorpusFact]
    public void A_word_in_a_panel_names_the_button_and_follows_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var word = new TextBlock { Text = "LOAD" };
            var button = new Button { Content = new Grid { Children = { new Avalonia.Controls.Shapes.Path(), word } } };

            WordedButtons.Name(button);
            Assert.Equal("LOAD", AutomationProperties.GetName(button));

            word.Text = "SAVE";
            Assert.Equal("SAVE", AutomationProperties.GetName(button));
        });
    }

    [CorpusFact]
    public void A_name_of_its_own_and_a_glyph_alone_are_left_as_they_are()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var named = new Button { Content = new Grid { Children = { new TextBlock { Text = "LOAD" } } } };
            AutomationProperties.SetName(named, "Open");
            WordedButtons.Name(named);
            Assert.Equal("Open", AutomationProperties.GetName(named));

            var glyph = new Button { Content = new Grid { Children = { new Avalonia.Controls.Shapes.Path() } } };
            WordedButtons.Name(glyph);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetName(glyph)));
        });
    }

    /// <summary>
    /// A bare worn slot's button is named for the item to come, its binding saying nothing yet, and holds the count of
    /// an item hidden in a word's place: bound over, the tile of a ring put on that finger was named "1".
    /// </summary>
    [CorpusFact]
    public void A_ring_put_on_a_bare_finger_is_named_for_itself_in_the_window()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var main = program.Main;
            var page = main.Inventory!;
            var window = new Avalonia.Controls.Window { Width = 1400, Height = 1200, Content = new VP_View_Inv_L { DataContext = page } };
            window.Show();
            Pump();
            try
            {
                var finger = main.Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.LeftFinger && !slot.IsSwap);
                Assert.False(main.Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.RightFinger && !slot.IsSwap).IsFilled);
                string ring = finger.Item!.Name;

                page.TakeOff(finger);
                Pump();
                var inventory = page.Sections.First(section => section.Pages is not null).Pages!.First(each => each.Label == "Inventory").Grid!;
                page.Equip(inventory.Items.First(tile => tile.Name == ring));
                Pump();

                var tile = window.GetVisualDescendants().OfType<Button>().First(each => AutomationProperties.GetAutomationId(each) == "Tile"
                    && each.DataContext is EquipSlotViewModel { Slot.Location: ItemLocation.RightFinger, IsSwap: false });
                Assert.Equal(ring, AutomationProperties.GetName(tile));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }
}
