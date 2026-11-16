using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Tips;

/// <summary>
/// The comparison beside an item's tooltip (TipActions): Tab opens it on what is worn where
/// a click would put the item - "Equipped - Helm" - and shuts it, kept as left; open, the strip lists the keys
/// alone, Ctrl turns it to the mercenary's slot and back, Alt to the next slot that takes the item, and an
/// empty slot says "Empty". With no comparison in sight Alt and Ctrl turn nothing; Tab is a tooltip's only while
/// it offers one. (A plain right click opens the
/// item's menu, whose rows name the slot and whose.)
/// <para>
/// What the keys do to the comparison, asked as the window's key handler asks (Toggle, SwitchWhose, Cycle) with
/// a tooltip up (<see cref="Tooltip"/>). That Tab, Ctrl and Alt pressed over a tooltip reach it - once a press,
/// however long held - and that the comparison is drawn in a box of its own beside the tooltip, are the UI
/// cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TipComparisonTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private EquipSlotViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    private ItemTileViewModel InGrid(string name) =>
        Page.Sections.First(section => section.Pages is not null).Pages!.First(page => page.Label == "Inventory").Grid!.Items.First(tile => tile.Name == name);

    private void WithTip(Action<Tooltip> test) => program.On(() =>
    {
        using var tip = new Tooltip();
        test(tip);
    });

    [CorpusFact]
    public void Tab_opens_it_on_what_is_worn_there_with_the_keys_alone_on_the_strip_and_shuts_it_again()
    {
        if (!program.Ready())
            return;

        WithTip(tip =>
        {
            var settings = program.Get<EditorSettings>();
            tip.Over(InGrid("Veil of Steel"));
            Assert.True(TipActions.CanCompare);
            Assert.False(TipActions.IsShowing);

            TipActions.Toggle();
            Assert.True(TipActions.IsShowing);
            Assert.Equal("Equipped - Helm", tip.Caption);
            Assert.Equal(["Veil of Steel"], tip.Compared);
            Assert.Same(Worn(ItemLocation.Head).Item, tip.Info!.Compared[0]);
            Assert.Equal("RMB - Menu; Ctrl - Mercenary; Tab - Hide comparison", tip.Strip);

            // Kept as left, for the next item and the next start.
            Assert.True(settings.CompareOnHover);

            TipActions.Toggle();
            Assert.Equal("", tip.Caption);
            Assert.Empty(tip.Compared);
            Assert.EndsWith("Tab - Compare", tip.Strip);
            Assert.False(settings.CompareOnHover);
        });
    }

    [CorpusFact]
    public void Ctrl_turns_it_to_the_mercenarys_piece_where_it_stays_and_Ctrl_again_to_the_characters()
    {
        if (!program.Ready())
            return;

        WithTip(tip =>
        {
            tip.Over(InGrid("Veil of Steel"));
            TipActions.Toggle();

            Assert.True(TipActions.SwitchWhose());
            Assert.Equal("Mercenary - Helm", tip.Caption);
            Assert.Equal(["Naj's Circlet"], tip.Compared);
            Assert.Equal("RMB - Menu; Ctrl - Character; Tab - Hide comparison", tip.Strip);

            Assert.True(TipActions.SwitchWhose());
            Assert.Equal("Equipped - Helm", tip.Caption);
        });
    }

    [CorpusFact]
    public void With_no_comparison_in_sight_Alt_and_Ctrl_turn_nothing_and_Tab_is_not_a_tooltips_key()
    {
        if (!program.Ready())
            return;

        WithTip(tip =>
        {
            // No tooltip up: Tab walks the window as ever.
            Assert.False(TipActions.CanCompare);

            // Up over an item that goes on someone, the comparison shut: Alt and Ctrl turn nothing.
            tip.Over(InGrid("Veil of Steel"));
            Assert.True(TipActions.CanCompare);
            Assert.False(TipActions.Cycle());
            Assert.False(TipActions.SwitchWhose());
            Assert.Equal("RMB - Menu; Tab - Compare", tip.Strip);

            // Open, but over an item nobody wears: it offers no comparison, and none is in sight.
            TipActions.Toggle();
            tip.Away();
            tip.Over(InGrid("Tome of Town Portal"));
            Assert.False(TipActions.CanCompare);
            Assert.False(TipActions.IsShowing);
            Assert.False(TipActions.Cycle());
            Assert.Equal("RMB - Menu", tip.Strip);
        });
    }

    [CorpusFact]
    public void Over_a_bare_finger_it_says_Empty_and_Alt_turns_it_to_the_other_finger()
    {
        if (!program.Ready(changes: true))
            return;

        WithTip(tip =>
        {
            Page.TakeOff(Worn(ItemLocation.LeftFinger));

            tip.Over(InGrid("Nagelring"));
            TipActions.Toggle();
            Assert.Equal("Equipped - Ring", tip.Caption);
            Assert.Empty(tip.Compared);
            Assert.Equal("Empty", tip.Info!.Empty);
            Assert.Equal("RMB - Menu; Alt - Next slot; Tab - Hide comparison", tip.Strip);

            // Alt tapped over it: the next slot that takes it, the comparison still up - and round to the first.
            Assert.Equal(0, TipActions.Slot);
            Assert.True(TipActions.Cycle());
            Assert.Equal("Equipped - Ring", tip.Caption);
            Assert.Equal(1, TipActions.Slot);
            Assert.True(TipActions.Cycle());
            Assert.Equal(0, TipActions.Slot);
        });
    }
}
