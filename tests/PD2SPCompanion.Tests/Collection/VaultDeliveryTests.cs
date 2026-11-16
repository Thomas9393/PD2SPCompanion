using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// Where smart sort delivers a kind of item, asked without delivering (the Extra page's
/// wizards and the sorter must look at the same thing). The sorter follows a
/// folder it made by a remembered link, renamed or moved; the wizards looked a top folder up by its name. They
/// ask the vault now, which finds the folders as the sorter does (<c>Vault.DeliveryFolders</c>, <c>RowsUnder</c>),
/// and the rules, which say the top categories (<c>SmartSort.BasesRoots</c>, <c>CraftingBasesRoots</c>).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultDeliveryTests(Game game) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-delivery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private Vault Open()
    {
        Directory.CreateDirectory(_folder);
        return Vault.Open(Path.Combine(_folder, "delivery.pd2lib"), "S13", 10);
    }

    private static string Names(Vault vault, IEnumerable<long> ids)
    {
        var folders = vault.Folders().ToDictionary(each => each.Id, each => each.Name);
        return string.Join(", ", ids.Select(id => folders[id]).Order(StringComparer.Ordinal));
    }

    [CorpusFact]
    public void A_categorys_folders_are_found_where_the_sorter_would_deliver_renamed_or_moved()
    {
        if (!game.Available)
            return;

        using var vault = Open();

        // Nothing delivered yet, and no folder of the name: none.
        Assert.Empty(vault.DeliveryFolders("Bases"));
        Assert.Null(vault.DeliveryName("Bases"));
        Assert.Empty(vault.RowsUnder("Bases"));

        // A top folder of the name made by hand, which the sorter would take on: found, though not linked.
        long byHand = vault.CreateFolder(null, "Bases");
        Assert.Equal([byHand], vault.DeliveryFolders("Bases"));
        Assert.Equal("Bases", vault.DeliveryName("Bases"));

        // The sorter delivers: the hand-made folder is taken on, and the levels under it made.
        long weapons = vault.FolderFor(["Bases", "Weapons"]);
        long helms = vault.FolderFor(["Bases", "Helms"]);
        vault.FolderFor(["Crafting Bases", "Rings"]);
        Assert.Equal("Bases, Helms, Weapons", Names(vault, vault.DeliveryFolders("Bases")));

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var items = save.PlayerItemList!.Items.Take(6).ToList();
        long own = vault.CreateFolder(weapons, "Kept apart");
        long?[] into = [weapons, helms, own];
        var stored = new List<long>();
        foreach (var item in items)
        {
            if (vault.Add(item, into[stored.Count % into.Length], out _) is { } key)
                stored.Add(key);
        }

        Assert.True(stored.Count >= 3);

        // Everything under the category: its folders', and a folder's the player made inside.
        Assert.Equal(stored.Order(), vault.RowsUnder("Bases").Select(row => row.Key).Order());
        Assert.Empty(vault.RowsUnder("Crafting Bases"));

        // Renamed, and a level moved out to the top and renamed: still the category's, as the sorter has it.
        vault.RenameFolder(byHand, "White things");
        vault.MoveFolder(weapons, null);
        vault.RenameFolder(weapons, "Sticks");
        Assert.Equal(weapons, vault.FolderFor(["Bases", "Weapons"]));
        Assert.Equal("Helms, Sticks, White things", Names(vault, vault.DeliveryFolders("Bases")));
        Assert.Equal("White things", vault.DeliveryName("Bases"));
        Assert.Equal(stored.Order(), vault.RowsUnder("Bases").Select(row => row.Key).Order());

        // A new top folder of the old name is nobody's: the category has its folder.
        vault.CreateFolder(null, "Bases");
        Assert.Equal("Helms, Sticks, White things", Names(vault, vault.DeliveryFolders("Bases")));

        // "Bases" is not "Bases and more".
        vault.FolderFor(["Bases and more", "Weapons"]);
        Assert.Equal("Helms, Sticks, White things", Names(vault, vault.DeliveryFolders("Bases")));
    }

    [CorpusFact]
    public void The_rules_say_which_top_categories_bases_are_delivered_to()
    {
        var shipped = SmartSort.Parse(SmartSort.DefaultText);
        Assert.Empty(shipped.Problems);
        Assert.Equal([SmartSort.BasesFolder], SmartSort.BasesRoots(shipped));
        Assert.Equal([SmartSort.CraftingBasesFolder], SmartSort.CraftingBasesRoots(shipped));

        // Rules rewritten: the wizards follow them.
        var own = SmartSort.Parse("""
            ItemDisplay[CRAFTBASE rin]: Craft/Rings
            ItemDisplay[CRAFTBASE]: Craft/%SLOT%
            ItemDisplay[UNI]: Uniques/%SLOT%
            ItemDisplay[NMAG !SUP SOCK>0 WEAPON]: Runeword Bases/%WEAPON%
            ItemDisplay[NMAG OR SUP]: White/%SLOT%
            ItemDisplay[]: Unsorted
            """);
        Assert.Empty(own.Problems);
        Assert.Equal(["Runeword Bases", "White"], SmartSort.BasesRoots(own));
        Assert.Equal(["Craft", SmartSort.CraftingBasesFolder], SmartSort.CraftingBasesRoots(own));

        // No rule for white items at all: the shipped name, where a folder of bases laid out by hand would be.
        var none = SmartSort.Parse("""
            ItemDisplay[]: Unsorted
            """);
        Assert.Equal([SmartSort.BasesFolder], SmartSort.BasesRoots(none));
    }
}
