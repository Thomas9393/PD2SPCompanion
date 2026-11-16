using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests;

/// <summary>
/// A right-click menu as a test uses one: the item selected by the press and its menu made, as the view does it
/// (<c>Menu</c>), and a row or an icon picked by its words (<c>Pick</c>) - "Store in vault" finds "Store in vault
/// (3)" too. A row that is dimmed is not picked: the test fails, saying why the row says it is blocked.
/// </summary>
internal static class Menus
{
    public static ActMenu Menu(this VP_ViewModel_Inv_L page, ItemTileViewModel tile)
    {
        page.SelectForMenu(tile);
        return page.MenuFor(tile);
    }

    public static ActMenu Menu(this VP_ViewModel_Vault_L page, ItemTileViewModel card)
    {
        page.SelectForMenu(card);
        return page.MenuFor(card);
    }

    public static IEnumerable<MenuAct> All(this ActMenu menu) => menu.Icons.Concat(menu.Rows);

    /// <summary>The menu's worded rows as one line, top to bottom, a dimmed one in brackets: "Withdraw; [Equip]; Move to folder...".</summary>
    public static string Words(this ActMenu menu) =>
        string.Join("; ", menu.Rows.Select(act => act.IsEnabled ? act.Label : $"[{act.Label}]"));

    /// <summary>The row or icon of these words, or null where the menu has none.</summary>
    public static MenuAct? Find(this ActMenu menu, string label) =>
        menu.All().FirstOrDefault(act => act.Label == label || act.Label.StartsWith(label + " (", StringComparison.Ordinal));

    public static MenuAct Row(this ActMenu menu, string label) =>
        menu.Find(label) ?? throw new Xunit.Sdk.XunitException($"no '{label}' in the menu: {string.Join(" | ", menu.All().Select(act => act.Label))}");

    public static void Pick(this ActMenu menu, string label)
    {
        var act = menu.Row(label);
        Assert.True(act.IsEnabled, $"'{label}' is dimmed: {act.Why}");
        act.Run();
    }
}
