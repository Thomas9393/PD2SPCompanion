using Avalonia.Controls;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Keys;

/// <summary>
/// Tooltips that list what keys do (KeyedTip): the lines that are a key's - the keys, a
/// colon, what they do - are drawn apart as caps, the words over them and under them stay words, and one
/// shortcut in brackets after a name stays in its sentence. The Selection menu teaches the two drags, and the
/// Edit page says them only while several are picked - not under "Please select an item.".
/// <para>
/// Which of a tooltip's lines are a key's is the control's own reading of the text, asked of the control made
/// as a tooltip makes it; what the pages hand it is asked of the pages. That a tooltip coming up swaps its text
/// for the caps, that a tooltip showing a control still shows it, and that Shift + drag begun on an item draws
/// the rectangle, are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class KeyedTipTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private EquipSlotViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    /// <summary>A tooltip's text as it is drawn: the words over the keys, each key with what it does, the words under.</summary>
    private static (string Lead, string Keys, string Foot) Drawn(string text)
    {
        var tip = new KeyedTip { Text = text };
        string keys = string.Join(" | ", (tip.FindControl<KeyHints>("Keys")!.Hints ?? []).Select(hint => $"{hint.Keys} | {hint.Does}"));
        return (tip.FindControl<TextBlock>("Lead")!.Text ?? "", keys, tip.FindControl<TextBlock>("Foot")!.Text ?? "");
    }

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [CorpusFact]
    public void A_name_over_its_keys_is_drawn_as_the_name_then_each_key_apart_from_what_it_does()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // The Character page's Add points, as its XAML writes it: a line feed between the lines.
            string text = "Add points\nCtrl + LMB: 5 points\nShift + LMB: 20 points";
            Assert.True(KeyedTip.HasKeys(text));
            Assert.Equal(("Add points", "Ctrl + LMB | 5 points | Shift + LMB | 20 points", ""), Drawn(text));
        });
    }

    [CorpusFact]
    public void One_shortcut_in_brackets_after_a_name_stays_a_line_of_words()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.False(KeyedTip.HasKeys("Next find (F3)"));
            Assert.False(KeyedTip.HasKeys(program.Get<VP_ViewModel_Settings_L>().KeysHint));
            Assert.False(KeyedTip.HasKeys(Page.DeleteHint));
            Assert.False(KeyedTip.HasKeys(""));
            Assert.False(KeyedTip.HasKeys(null));

            // Drawn all the same, it would be the words it was.
            Assert.Equal(("Next find (F3)", "", ""), Drawn("Next find (F3)"));
        });
    }

    [CorpusFact]
    public void Words_over_the_keys_stay_words_and_words_after_them_go_under()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // The socket stepper's minus.
            var minus = Drawn("Remove socket\nIts socketed item comes out\nShift + LMB: no sockets");
            Assert.Equal(Lines("Remove socket", "Its socketed item comes out"), minus.Lead);
            Assert.Equal("Shift + LMB | no sockets", minus.Keys);
            Assert.Equal("", minus.Foot);

            // A requirement after the keys is no key's line: under them.
            var after = Drawn(Lines("Store selected item in vault", "Ctrl + LMB: sorted", "Require vault page open."));
            Assert.Equal(("Store selected item in vault", "Ctrl + LMB | sorted", "Require vault page open."), after);

            // A colon alone makes no key of a word that is none.
            Assert.False(KeyedTip.HasKeys("Level: 86"));
        });
    }

    [CorpusFact]
    public void The_Selection_menus_say_the_two_drags_that_draw_a_rectangle_from_on_top_of_an_item()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            foreach (string hint in new[] { Page.EquippedPicks.Hint, Page.MercPicks.Hint })
            {
                Assert.Equal(("Selection", "Shift + Drag | select area | Ctrl + Drag | add area", ""), Drawn(hint));
            }

            Assert.Equal(Lines("Shift + Drag: select area", "Ctrl + Drag: add area"), PickMenu.Drags);
        });
    }

    [CorpusFact]
    public void The_Edit_page_says_the_drags_with_several_picked_and_not_with_nothing_chosen()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                var edit = program.Get<VP_ViewModel_Inv_R>();
                var right = program.Get<VP_ViewModel_Vault_R>();

                // Nothing chosen: "Please select an item." stands alone - its notice is handed no keys at all.
                Page.ClearSelection();
                Assert.False(edit.HasItem);
                Assert.False(edit.IsPicking);

                // The ring, then the amulet with Ctrl: how many, and the two drags under it.
                Page.ClickTileCommand.Execute(Worn(ItemLocation.LeftFinger).Item);
                Assert.True(edit.HasItem);
                Assert.True(Page.BeginDragPick(keepSelected: true));
                Page.SetPicked(Worn(ItemLocation.Neck).Item!, true);
                Page.EndPick();

                Assert.True(edit.IsPicking);
                Assert.Equal("2 selected", edit.PickedText);
                Assert.Equal(PickMenu.Drags, right.SelectKeys);
                Assert.True(KeyedTip.HasKeys(right.SelectKeys));
            });
        }
        finally
        {
            program.On(Page.ClearSelection);
        }
    }
}
