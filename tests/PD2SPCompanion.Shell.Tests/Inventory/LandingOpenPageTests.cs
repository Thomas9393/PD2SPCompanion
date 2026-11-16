using System.Text.RegularExpressions;

using PD2SPCompanion.Core.Trade;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// What arrives lands on the page a pane shows - the Forge's new item, a trade's, the vault's
/// Withdraw button - not the default fallback path.
/// With the first pane on an empty shared page: FORGE puts its item there
/// and says so, a purchase lands there, and the vault's Withdraw button brings its item there - where each went
/// to the inventory before.
/// <para>
/// What the three pages decide, asked through their own commands (the Forge's FORGE, the payment's Pay, the
/// vault's Withdraw) and read back from the shared page as the program holds it. The purchase is of a listing
/// kept in the corpus, put in the run's own cache: nothing is asked of the trade site. That the wheel over a
/// pane's picker steps past the rule between the character's containers and the shared pages is the view's
/// own doing.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class LandingOpenPageTests(Program program)
{
    private const string Shown = "Shared Page 5", Tome = "Tome of Town Portal";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    private bool IsOnTheSharedPage(string name) =>
        program.Holds(Shown, name)
        && program.Get<ItemLanding>().WhereIs(program.Tile(name, Shown).Item) is { Shared: true, Page: 5 };

    [CorpusFact]
    public void FORGE_with_a_shared_page_shown_puts_its_item_there_and_says_so()
    {
        if (!program.Ready(changes: true))
            return;

        // The Forge is Free's.
        using var mode = Home.Made.Mode(program, PlayPolicy.FreeName);
        program.On(() =>
        {
            Assert.Empty(program.Grid(Shown).Items);
            program.Panes(Shown);

            Main.ShowRightCommand.Execute(MainViewModel.ForgeMark);
            Assert.True(Main.IsRightForge);
            var forge = program.Get<VP_ViewModel_Vault_R>().Forge;

            forge.Search = "war trav";
            forge.PickCommand.Execute(forge.Matches.First(find => find.Name == "War Traveler"));
            Assert.Equal("War Traveler", forge.Draft?.Name);
            Assert.Equal("Forge to character", forge.ForgeTip);

            Assert.True(forge.ForgeCommand.CanExecute(null));
            forge.ForgeCommand.Execute(null);
            Assert.Equal("War Traveler forged to shared page 5.", Session.Messages.Text);
            Assert.True(IsOnTheSharedPage("War Traveler"));
            Assert.False(program.Holds("Inventory", "War Traveler"));
        });
    }

    [CorpusFact]
    public void The_vaults_Withdraw_button_brings_its_item_to_the_shared_page_shown()
    {
        if (!program.Ready(changes: true))
            return;

        int stored = 0;
        program.On(() =>
        {
            Assert.Empty(program.Grid(Shown).Items);
            program.Panes(Shown);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
        });
        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == Tome)), "the vault's cards were not listed");

        program.On(() =>
        {
            stored = program.InVault();
            Vault.SelectOnly(InventoryPad.CardsOf(Vault).First(card => card.Name == Tome));
            Assert.True(Vault.WithdrawCommand.CanExecute(null));
            Vault.WithdrawCommand.Execute(null);

            // Said with what else there is to say of it - the character holds a tome of the same id already.
            Assert.StartsWith("1 item moved to shared stash.", Vault.Notice);
            Assert.StartsWith("1 item moved to shared stash.", Session.Messages.Text);
            Assert.True(IsOnTheSharedPage(Tome));
        });
        Assert.True(program.Until(() => program.InVault() == stored - 1), "the tome's row did not leave the vault");
    }

    [CorpusFact]
    public void A_purchase_with_a_shared_page_shown_lands_there()
    {
        if (!program.Ready(changes: true))
            return;

        // A listing as the trade site hands it out, kept in the corpus; in the run's cache it is opened by its
        // link with nothing asked of the site.
        string kept = Path.Combine(Program.Corpus, "trade", "desec", "6a92170a9f44c105fc2892b4.json");
        if (!File.Exists(kept))
            return;

        string id = Path.GetFileNameWithoutExtension(kept);
        string cached = Path.Combine(TradeListings.CacheFolder, id + ".json");
        var trade = program.Get<VP_ViewModel_Trade>();
        try
        {
            Directory.CreateDirectory(TradeListings.CacheFolder);
            File.Copy(kept, cached, overwrite: true);

            program.On(() =>
            {
                Assert.Empty(program.Grid(Shown).Items);
                program.Panes(Shown);
                Main.ShowRightCommand.Execute(MainViewModel.TradeMark);
                trade.Link = $"https://www.projectdiablo2.com/market/listing/{id}";
            });
            Assert.True(program.Until(() => trade.HasItem, 10000), "the listing was not shown");

            program.On(() =>
            {
                Assert.True(trade.ConfirmCommand.CanExecute(null));
                trade.ConfirmCommand.Execute(null);
                Assert.True(trade.IsPaymentOpen, $"the payment did not open: {Session.RightMessages.Text}");

                trade.Payment.Price = "0";
                Assert.Equal("Paying nothing.", trade.Payment.Summary);
                Assert.True(trade.Payment.PayCommand.CanExecute(null));
                trade.Payment.PayCommand.Execute(null);

                var bought = Regex.Match(Session.Messages.Text, @"^Bought (.+)\.$");
                Assert.True(bought.Success, $"the strip does not say what was bought: '{Session.Messages.Text}' / '{trade.Payment.Summary}'");
                Assert.True(IsOnTheSharedPage(bought.Groups[1].Value));
                Assert.False(trade.IsPaymentOpen);
            });
        }
        finally
        {
            program.On(() =>
            {
                trade.IsPaymentOpen = false;
                trade.Link = "";
            });
            File.Delete(cached);
        }
    }
}
