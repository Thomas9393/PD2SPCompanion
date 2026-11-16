using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// When duplicates are looked for: once per session or per character loading.
/// A copy can only come into being at a few
/// moments, so the files are read then - a character loaded, an item made, another file of the save folder
/// written - and not at every redraw of a page; while something is marked they are read at each marking pass,
/// so a copy dealt with loses its mark.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DuplicateCheckTests(Program program)
{
    private MainViewModel Main => program.Main;
    private DuplicateIndex Index => program.Get<DuplicateIndex>();
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private List<ItemTileViewModel> Tiles() =>
        [.. Main.Session.AllGrids.SelectMany(grid => grid.Items).Concat(Main.Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>())];

    [CorpusFact]
    public void With_nothing_held_twice_the_files_are_read_at_a_load_and_not_at_every_marking_pass()
    {
        // The pad is put back after: its other character and a vault item are taken away here.
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            Assert.True(Index.IsOn);

            // The pad holds things twice by design - Finder is PhySpearZon's save under another name, and one
            // of the character's items lies in the vault too. Both taken away: nothing is held twice.
            File.Delete(program.SaveOf(Program.Other));
            var store = program.Get<VaultService>().Store!;
            var mine = Main.Session.HeldItems.Where(item => !item.IsCompact).Select(item => item.Id).ToHashSet();
            store.Remove([.. store.OriginIds().Where(row => mine.Contains(row.Id)).Select(row => row.Key)]);
            Index.LookAgain();

            // The first marking pass after reads the files.
            Index.Mark(Tiles());
            int looked = Index.Looks;
            Assert.All(Tiles(), tile => Assert.Null(tile.DuplicatePlaces));

            // Pass after pass - every move redraws the tiles - nothing is read.
            for (int pass = 0; pass < 5; pass++)
                Index.Mark(Tiles());
            Assert.Equal(looked, Index.Looks);
            Assert.Empty(Index.DuplicateIds());
            Assert.Equal(looked, Index.Looks);

            // Something that can make a copy: read once more, at the next pass, and no more after.
            Index.LookAgain();
            Index.Mark(Tiles());
            Index.Mark(Tiles());
            Assert.Equal(looked + 1, Index.Looks);
        });
    }

    [CorpusFact]
    public void Another_character_loaded_is_looked_at_once()
    {
        if (!program.Ready())
            return;

        using var legit = program.InMode("Legit");
        int looked = program.On(() =>
        {
            Index.Mark(Tiles());
            return Index.Looks;
        });

        program.On(() => Main.Home.SwitchTo(program.SaveOf(Program.Other)));
        Assert.True(program.Until(() => Main.Session.IsLoaded && Main.Session.CharacterName == Program.Other, 15000), "the other character was not loaded");

        program.On(() =>
        {
            // Finder is PhySpearZon's save under another name: every item it holds, the other holds too.
            Index.Mark(Tiles());
            Assert.True(Index.Looks > looked, "a character loaded was not looked at");
            Assert.Contains(Tiles(), tile => tile.DuplicatePlaces is { Count: > 1 });

            // While something is marked, each pass looks again: the marks follow what is done about it.
            int marked = Index.Looks;
            Index.Mark(Tiles());
            Assert.Equal(marked + 1, Index.Looks);
        });
    }

    [CorpusFact]
    public void Off_in_a_mode_that_does_not_mark_them_nothing_is_read()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            Assert.False(Index.IsOn);
            int looked = Index.Looks;
            Index.LookAgain();
            Index.Mark(Tiles());
            Assert.Equal(looked, Index.Looks);
            Assert.All(Tiles(), tile => Assert.Null(tile.DuplicatePlaces));
        });
    }
}
