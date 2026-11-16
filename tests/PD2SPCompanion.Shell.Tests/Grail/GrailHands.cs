using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// What the tests of the grail do with their hands: the Items page's grail put on the right page, and a card
/// brought up as a player brings it up - its tab, its name in the grail's own box. Asked on the program's thread.
/// </summary>
internal static class GrailHands
{
    /// <summary>The Items page's grail, where cards are chosen and copies counted.</summary>
    public static GrailViewModel OfItems(Program program) => program.Get<VP_ViewModel_Vault_R>().Grail;

    /// <summary>The Extra page's grail, which is only browsed.</summary>
    public static GrailViewModel OfExtra(Program program) => program.Get<VP_ViewModel_Extra_R>().Grail;

    /// <summary>The grail on the right page of the Items book: its lists are made as it is first shown.</summary>
    public static GrailViewModel OnRight(Program program)
    {
        program.Main.ShowRightCommand.Execute(MainViewModel.GrailMark);
        return OfItems(program);
    }

    /// <summary>A card by its tab and its name, typed into the grail's box.</summary>
    public static GrailCard Card(GrailViewModel grail, string tab, string name)
    {
        grail.ShowTabCommand.Execute(tab);
        grail.Search = name;
        return grail.Cards.First(card => card.Name == name);
    }

    /// <summary>The grail as a test found it: nothing chosen, nothing typed, on its first tab.</summary>
    public static void Rest(GrailViewModel grail)
    {
        grail.Selected = null;
        grail.Search = "";
        grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
    }
}
