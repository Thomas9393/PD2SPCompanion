using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Trade;

/// <summary>
/// The Trade page's Sell tab offers the item selected on the inventory. The page
/// only notes a choice made while it is hidden, and takes it up when shown: the item chosen beside the Edit
/// page is the one on sale on opening Trade, a choice made with Trade open shows at once, and one made beside
/// Edit again is there on coming back. The sale's panel says what the materials tab holds in all. Nothing is
/// sold.
/// <para>
/// What the page decides, told of its coming and going as its view tells it (Shown, Hidden). That the view
/// does tell it, as the bookmarks are clicked, is the UI cases' to show (units/sell-follow.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SellFollowTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Trade Trade => TradeHands.Page(program);

    private ItemTileViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap).Item!;

    private void Rest(string mode)
    {
        TradeHands.Rest(program, mode);
        Page.ClearSelection();
    }

    [CorpusFact]
    public void The_item_chosen_beside_Edit_is_on_sale_on_opening_Trade_one_chosen_with_Trade_open_at_once_and_one_chosen_while_it_is_away_on_coming_back()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            string mode = program.Get<EditorSettings>().PlayMode;
            try
            {
                // Edit on the right: the Trade page is put away.
                Trade.Hidden();
                Page.ClickTileCommand.Execute(Worn(ItemLocation.RightHand));
                Assert.Equal("Stone Barb", Session.SelectedItem?.Name);

                TradeHands.OnRight(program);
                Trade.ShowSellCommand.Execute(null);
                Assert.Equal("Stone Barb", Trade.SellItem?.Name);
                Assert.True(Trade.ShowsSellItem);

                Page.ClickTileCommand.Execute(VaultHands.Tile(program, "Annihilus", "Inventory"));
                Assert.Equal("Annihilus", Trade.SellItem?.Name);

                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Trade.Hidden();
                Page.ClickTileCommand.Execute(Worn(ItemLocation.Torso));
                Assert.Equal("Annihilus", Trade.SellItem?.Name);

                TradeHands.OnRight(program);
                Assert.Equal("Duriel's Shell", Trade.SellItem?.Name);
            }
            finally
            {
                Rest(mode);
            }
        });
    }

    [CorpusFact]
    public void With_none_or_several_selected_nothing_is_on_sale()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            string mode = program.Get<EditorSettings>().PlayMode;
            try
            {
                TradeHands.OnRight(program);
                Trade.ShowSellCommand.Execute(null);

                // Two of the inventory's upper half: the charm area below takes no pick under the inventory lock.
                Page.ClickTileCommand.Execute(VaultHands.Tile(program, "The Stone of Jordan", "Inventory"));
                Assert.Equal("The Stone of Jordan", Trade.SellItem?.Name);

                VaultHands.CtrlClick(Page, VaultHands.Tile(program, "Veil of Steel", "Inventory"));
                Assert.True(Page.IsPicking);
                Assert.Null(Trade.SellItem);
                Assert.True(Trade.ShowsSellHint);
                Assert.False(Trade.OpenSaleCommand.CanExecute(null));

                Page.ClearSelection();
                Assert.Null(Trade.SellItem);
            }
            finally
            {
                Rest(mode);
            }
        });
    }

    [CorpusFact]
    public void The_sales_panel_says_what_the_materials_tab_holds_in_all()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            string mode = program.Get<EditorSettings>().PlayMode;
            try
            {
                Page.ClickTileCommand.Execute(Worn(ItemLocation.RightHand));
                TradeHands.OnRight(program);
                Trade.ShowSellCommand.Execute(null);

                Assert.True(Trade.OpenSaleCommand.CanExecute(null));
                Trade.OpenSaleCommand.Execute(null);

                Assert.True(Trade.IsSaleOpen);
                Assert.True(Trade.Payment.IsReceiving);
                Assert.Equal("SELL", Trade.Payment.ActionWord);
                Assert.Matches(@"^Materials: [\d.]+ HR$", Trade.Payment.HoldingText);
                Assert.True(Session.Holds(Worn(ItemLocation.RightHand).Item), "nothing is sold");
            }
            finally
            {
                Rest(mode);
            }
        });
    }
}
