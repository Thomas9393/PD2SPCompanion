using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Trade;

/// <summary>
/// Trading beside the vault, so trading need not bounce
/// back to the inventory: Trade and the vault stay side by side; a listing bought beside the
/// vault goes into the folder it shows, the strip saying so; the Sell tab offers the card selected in the
/// vault and the sale takes its row out, which Undo puts back; in Legit a listing bought before wears a
/// warning and buys again all the same; and the trade records list what was traded, a sale undone not among
/// them.
/// <para>
/// What the pages decide, asked as the view asks, on a listing of the corpus's read from the trade cache (no
/// network). That the link box, Buy, the payment and the records are there to click, and the bought card
/// drawn in the folder, is the UI cases' to show (units/trade-vault.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TradeVaultTests(Program program)
{
    private MainViewModel Main => program.Main;
    private VP_ViewModel_Trade Trade => TradeHands.Page(program);
    private PD2SPCompanion.Core.Collection.Vault Store => program.Get<VaultService>().Store!;

    private string Mode => program.On(() => program.Get<EditorSettings>().PlayMode);

    /// <summary>The vault on the left page and Trade on the right, the listing in the cache. False where the corpus has no listing.</summary>
    private bool Beside(out VP_ViewModel_Vault_L left)
    {
        left = null!;
        if (!TradeHands.Cache())
            return false;

        // At the vault's top, whatever a test before left it on.
        var vault = program.On(() =>
        {
            var shown = VaultHands.OnLeft(program);
            shown.GoTo(null);
            TradeHands.OnRight(program);
            return shown;
        });
        Assert.True(program.Until(() => VaultHands.CardsOf(vault).Count > 0), "the vault's cards were not listed");
        left = vault;
        return true;
    }

    private void Rest(string mode)
    {
        program.On(() =>
        {
            TradeHands.Rest(program, mode);
            VaultHands.Rest(program);
        });
        TradeHands.Uncache();
    }

    /// <summary>
    /// The vault read again beside an open sale - a save, a change of the sheet's width - makes the same row's card again:
    /// the sale, its search and what was typed stay. A row really gone ends it.
    /// </summary>
    [CorpusFact]
    public void A_vault_reload_leaves_an_open_sale_and_its_search_and_a_row_gone_ends_it()
    {
        if (!program.Ready(changes: true))
            return;

        string mode = Mode;
        try
        {
            if (!Beside(out var left))
                return;

            const string tome = "Tome of Town Portal";
            Assert.True(program.Until(() => VaultHands.CardOrNone(left, tome) is not null), "the tome's card was not listed");

            program.On(() =>
            {
                var vaults = program.Get<VaultService>();
                Trade.ShowSellCommand.Execute(null);
                left.ClickTileCommand.Execute(VaultHands.Card(left, tome));
                Trade.OpenSaleCommand.Execute(null);
                Assert.True(Trade.IsSaleOpen);

                var search = Trade.Search;
                var tile = Trade.SellItem;
                Trade.Payment.Price = "7";
                var card = (ItemTileViewModel)left.SelectedEntry!;
                int changes = 0;
                void Count() => changes++;
                vaults.ChosenChanged += Count;

                try
                {
                    vaults.SayChanged();

                    // The new card chosen once, with no "nothing chosen" between: the page following it keeps its place.
                    Assert.Equal(1, changes);
                    Assert.NotSame(card, left.SelectedEntry);
                    Assert.Equal(card.Key, vaults.Chosen?.Key);
                    Assert.True(Trade.IsSaleOpen);
                    Assert.Same(search, Trade.Search);
                    Assert.Same(tile, Trade.SellItem);
                    Assert.Equal("7", Trade.Payment.Price);
                }
                finally
                {
                    vaults.ChosenChanged -= Count;
                }

                // The row gone: nothing left to sell.
                Store.Remove([card.Key]);
                vaults.SayChanged();
                Assert.Null(vaults.Chosen);
                Assert.False(Trade.IsSaleOpen);
                Assert.Null(Trade.Search);
            });
        }
        finally
        {
            Rest(mode);
        }
    }

    [CorpusFact]
    public void Trade_and_the_vault_stay_side_by_side_in_either_order_of_taking_them()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            Main.ShowRightCommand.Execute(MainViewModel.TradeMark);
            Assert.True(Main.IsLeftVault, "Trade taken beside the vault: the vault stays on the left");
            Assert.True(Main.IsRightTrade);
            Assert.True(Trade.WithVault);

            Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            Assert.True(Main.IsRightTrade);
            Assert.False(Trade.WithVault);

            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            Assert.True(Main.IsLeftVault);
            Assert.True(Main.IsRightTrade, "the vault taken beside Trade: Trade stays on the right");
            Assert.True(Trade.WithVault);
        });
    }

    [CorpusFact]
    public void A_listing_bought_beside_the_vault_goes_into_the_folder_it_shows_and_the_strip_says_so()
    {
        if (!program.Ready(changes: true))
            return;

        string mode = Mode;
        try
        {
            if (!Beside(out var left))
                return;

            int stored = program.On(() => Store.Count());
            program.On(() => VaultHands.GoTo(left, "Jewels"));
            string name = TradeHands.BuyAtNothing(program);

            program.On(() => Assert.Equal($"Bought {name} to vault.", Main.Session.Messages.Text));
            Assert.True(program.Until(() => Store.Count() == stored + 1 && VaultHands.CardOrNone(left, name) is not null), "the bought card is not in the folder shown");
            program.On(() =>
            {
                Assert.Equal(VaultHands.Place(left, "Jewels").Id, VaultHands.Card(left, name).Row.FolderId);
                Assert.False(Trade.HasItem, "the listing is let go of once bought");
            });
        }
        finally
        {
            Rest(mode);
        }
    }

    [CorpusFact]
    public void In_Legit_a_listing_bought_before_wears_a_warning_beside_Buy_and_is_bought_again_all_the_same()
    {
        if (!program.Ready(changes: true))
            return;

        string mode = Mode;
        try
        {
            program.On(() => program.Get<EditorSettings>().PlayMode = PlayPolicy.LegitName);
            if (!Beside(out _))
                return;

            int stored = program.On(() => Store.Count());
            TradeHands.Paste(program);
            program.On(() => Assert.False(Trade.WasBoughtBefore, "a listing never bought: no warning mark"));

            TradeHands.BuyAtNothing(program);
            Assert.True(program.Until(() => Store.Count() == stored + 1), "the purchase was not kept");

            TradeHands.Paste(program);
            program.On(() =>
            {
                Assert.True(Trade.WasBoughtBefore);
                Assert.Matches(@"^(Bought before: .+, \d\d/\d\d/\d{4}\.|Already bought\.)$", Trade.BoughtBefore);
            });

            TradeHands.BuyAtNothing(program);
            Assert.True(program.Until(() => Store.Count() == stored + 2), "it was not bought again");
        }
        finally
        {
            Rest(mode);
        }
    }

    [CorpusFact]
    public void Beside_the_vault_the_Sell_tab_offers_the_card_selected_the_sale_takes_its_row_out_and_Undo_puts_it_back()
    {
        if (!program.Ready(changes: true))
            return;

        const string tome = "Tome of Town Portal";
        string mode = Mode;
        try
        {
            if (!Beside(out var left))
                return;

            int rows = 0;
            program.On(() =>
            {
                rows = Store.Count();
                Trade.ShowSellCommand.Execute(null);
                Assert.True(Trade.ShowsSellHint, "nothing selected: 'Please select an item.'");
                Assert.False(Trade.OpenSaleCommand.CanExecute(null));

                left.ClickTileCommand.Execute(VaultHands.Card(left, tome));
                Assert.Equal(tome, Trade.SellItem?.Name);
                Assert.True(Trade.ShowsSellItem);

                Trade.OpenSaleCommand.Execute(null);
                Assert.True(Trade.IsSaleOpen);
                Assert.StartsWith("Materials: ", Trade.Payment.HoldingText);

                Trade.Payment.Price = "0";
                Assert.Equal("Receiving nothing.", Trade.Payment.Summary);
                Trade.Payment.PayCommand.Execute(null);

                Assert.Equal($"Sold {tome} from vault.", Main.Session.Messages.Text);
                Assert.False(Trade.IsSaleOpen);
                Assert.False(Trade.HasSellItem);
            });
            Assert.True(program.Until(() => Store.Count() == rows - 1), "the vault did not lose the row");

            Assert.True(program.Until(() => Main.Home.UndoCommand.CanExecute(null)), "nothing to undo");
            program.On(() => Main.Home.UndoCommand.Execute(null));
            Assert.True(program.Until(() => Store.Count() == rows && VaultHands.CardOrNone(left, tome) is not null), "Undo did not put the sold card back");
        }
        finally
        {
            Rest(mode);
        }
    }

    [CorpusFact]
    public void The_trade_records_list_what_was_traded_newest_first_and_a_sale_undone_is_not_among_them()
    {
        if (!program.Ready(changes: true))
            return;

        const string tome = "Tome of Town Portal";
        string mode = Mode;
        try
        {
            if (!Beside(out var left))
                return;

            int trades = program.On(() => Store.Trades().Count), rows = program.On(() => Store.Count());
            int nothingSales = program.On(() => Store.Trades().Count(trade => trade.Kind == "sold" && trade.Price == "0 HR"));

            string name = TradeHands.BuyAtNothing(program);
            Assert.True(program.Until(() => Store.Trades().Count == trades + 1), "the purchase was not written down");

            // A sale at nothing, and undone: a trade undone leaves no record of itself.
            program.On(() =>
            {
                Trade.ShowSellCommand.Execute(null);
                left.ClickTileCommand.Execute(VaultHands.Card(left, tome));
                Trade.OpenSaleCommand.Execute(null);
                Trade.Payment.Price = "0";
                Trade.Payment.PayCommand.Execute(null);
            });
            Assert.True(program.Until(() => Store.Count() == rows && Main.Home.UndoCommand.CanExecute(null)), "the sale was not kept");
            program.On(() => Main.Home.UndoCommand.Execute(null));
            Assert.True(program.Until(() => Store.Count() == rows + 1), "the sale was not undone");

            program.On(() =>
            {
                Trade.ShowRecordsCommand.Execute(null);

                Assert.True(Trade.IsRecordsOpen);
                Assert.True(Trade.HasRecords);
                Assert.Equal(trades == 0 ? "1 trade" : $"{trades + 1} trades", Trade.RecordsCount);
                Assert.Equal(trades + 1, Trade.Records.Count);

                var newest = Trade.Records[0];
                Assert.Equal(name, newest.Name);
                Assert.StartsWith("Bought", newest.Deal);
                Assert.Equal("Nothing paid", newest.Moved);
                Assert.False(newest.IsSale);
                Assert.EndsWith($" · {Program.Open} · {mode}", newest.About);

                Assert.Equal(nothingSales, Trade.Records.Count(line => line.Deal == "Sold · 0 HR"));

                Trade.CloseRecordsCommand.Execute(null);
                Assert.False(Trade.IsRecordsOpen);
            });
        }
        finally
        {
            Rest(mode);
        }
    }

    /// <summary>A trade written with no step to own it stays in the ledger and on no next character's first step.</summary>
    [CorpusFact]
    public void A_trade_written_as_a_character_closes_is_no_next_steps_to_undo()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var history = program.Get<ItemHistory>();
            var item = Main.Session.HeldItems.First();
            history.Traded(new PD2SPCompanion.Core.Collection.TradeRecord(DateTimeOffset.Now, Main.Session.CharacterName, "bought",
                null, null, null, null, "Left behind", "1", "1", "Free"), item);

            Assert.Null(program.Get<PD2SPCompanion.Shell.Services.Character.SaveSet>().Commit());
            program.Get<PD2SPCompanion.Shell.Services.Character.SessionHistory>().Restart();

            // The next step undone takes nothing out of the ledger: the trade is not that step's.
            var steps = program.Get<PD2SPCompanion.Shell.Services.Character.SessionHistory>();
            steps.Record("Next step");
            Assert.Null(steps.Undo());
            Assert.Contains(Store.Trades(), trade => trade.Name == "Left behind");
        });
    }
}
