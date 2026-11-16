using PD2SPCompanion.Core.Storage;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Trade;

/// <summary>
/// What the tests of the Trade page do with their hands: a listing of the corpus's put where the program
/// keeps the listings it has fetched - so a link shows its item with nothing asked of the trade site - and a
/// purchase made of it at nothing. Everything but the listing's file is asked on the program's thread.
/// </summary>
internal static class TradeHands
{
    /// <summary>A listing the corpus keeps as the trade site handed it out (TestCorpus\trade\desec), by its id.</summary>
    public const string ListingId = "6a92170a9f44c105fc2892b4";

    public const string Link = "https://www.projectdiablo2.com/market/listing/" + ListingId;

    private static string Kept => Path.Combine(EditorData.TradeCache, ListingId + ".json");

    /// <summary>The listing in the run's own trade cache, which begins empty. False where the corpus has none.</summary>
    public static bool Cache()
    {
        string source = Path.Combine(Program.Corpus, "trade", "desec", ListingId + ".json");
        if (!File.Exists(source))
            return false;

        Directory.CreateDirectory(EditorData.TradeCache);
        File.Copy(source, Kept, overwrite: true);
        return true;
    }

    public static void Uncache()
    {
        if (File.Exists(Kept))
            File.Delete(Kept);
    }

    public static VP_ViewModel_Trade Page(Program program) => program.Get<VP_ViewModel_Trade>();

    /// <summary>The Trade page on the right, in sight: Shown is what its view says as it comes to the front.</summary>
    public static VP_ViewModel_Trade OnRight(Program program)
    {
        var trade = Page(program);
        program.Main.ShowRightCommand.Execute(MainViewModel.TradeMark);
        trade.Shown();
        return trade;
    }

    /// <summary>The link pasted, and its item shown: the listing read from the cache.</summary>
    public static void Paste(Program program)
    {
        var trade = Page(program);
        program.On(() =>
        {
            trade.ShowBuyCommand.Execute(null);
            trade.ClearCommand.Execute(null);
            trade.Link = Link;
        });
        Assert.True(program.Until(() => trade.HasItem && !trade.IsLoading, 15000), "the listing was not shown");
    }

    /// <summary>One purchase of the listing at nothing: the link, Buy, an offer of 0, Pay. The item's name, as the strip says it.</summary>
    public static string BuyAtNothing(Program program)
    {
        Paste(program);
        var trade = Page(program);
        return program.On(() =>
        {
            string name = trade.Item!.Name;
            trade.ConfirmCommand.Execute(null);
            Assert.True(trade.IsPaymentOpen, program.Main.Session.RightMessages.Text);

            trade.Payment.PicksByHand = false;
            trade.Payment.Price = "0";
            Assert.Equal("Paying nothing.", trade.Payment.Summary);
            Assert.True(trade.Payment.PayCommand.CanExecute(null));
            trade.Payment.PayCommand.Execute(null);

            Assert.False(trade.IsPaymentOpen, trade.Payment.Summary);
            return name;
        });
    }

    /// <summary>The page as a test found it: no listing, no panel, buying, put away; the play mode as it was.</summary>
    public static void Rest(Program program, string mode)
    {
        var trade = Page(program);
        trade.Payment.PicksByHand = false;
        trade.ClosePaymentCommand.Execute(null);
        trade.CloseSaleCommand.Execute(null);
        trade.CloseRecordsCommand.Execute(null);
        trade.ClearCommand.Execute(null);
        trade.ShowBuyCommand.Execute(null);
        trade.Hidden();
        program.Get<EditorSettings>().PlayMode = mode;
    }
}
