using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// The search form's Family box:
/// a craft's family - Blood, Caster... - as a condition of the search, picked in a
/// box that waits for the rarity to be Crafted, as a base waits for a type. It took the place of the grail's
/// Crafting tab, whose recipe cards set the same condition.
/// <para>
/// What the search decides. That the box is there and dim until Crafted is picked is a window's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchFamilyTests(Program program)
{
    private MainViewModel Main => program.Main;
    private SearchWindowViewModel Search => Main.SearchWindow!;
    private VaultFilter Filter => program.Get<VaultService>().Filter;

    private FilterChoice Rarity(string label) => Filter.RarityChoices.First(choice => choice.Label == label);

    private FilterChoice FamilyOf(string label) => Filter.FamilyChoices.First(choice => choice.Label == label);

    private List<FoundItem> Finds() => [.. Search.Finds.Rows.OfType<FoundItem>()];

    [CorpusFact]
    public void The_box_waits_for_Crafted_and_another_rarity_lets_the_family_go()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);

            // Any first, then the families as the wiki's chart heads its columns.
            Assert.Equal(["Any", "Blood", "Caster", "Hitpower", "Safety", "Vampiric", "Bountiful", "Brilliant"],
                         Filter.FamilyChoices.Select(choice => choice.Label));
            Assert.False(Filter.CanPickFamily);
            Assert.True(Filter.Family.IsAny);

            Filter.Rarity = Rarity("Rare");
            Assert.False(Filter.CanPickFamily);

            var said = new List<string?>();
            void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs changed) => said.Add(changed.PropertyName);
            Filter.PropertyChanged += Heard;
            try
            {
                Filter.Rarity = Rarity("Crafted");
                Assert.True(Filter.CanPickFamily);
                Assert.Contains(nameof(VaultFilter.CanPickFamily), said);

                said.Clear();
                Filter.Family = FamilyOf("Blood");
                Assert.Equal("Blood", Filter.CraftFamily);
                Assert.Equal("Blood", Filter.Family.Label);
                Assert.Contains(nameof(VaultFilter.Family), said);
            }
            finally
            {
                Filter.PropertyChanged -= Heard;
            }

            // It is a condition of the search, and the presets' line says it.
            Assert.Equal("Blood", Filter.Query().CraftFamily);
            Assert.Contains("Blood craft", Filter.Describe());

            // Any again: no family asked.
            Filter.Family = Filter.FamilyChoices[0];
            Assert.Equal("", Filter.CraftFamily);
            Assert.Null(Filter.Query().CraftFamily);

            // Another rarity: the family chosen under Crafted goes with it.
            Filter.Family = FamilyOf("Caster");
            Filter.Rarity = Rarity("Rare");
            Assert.False(Filter.CanPickFamily);
            Assert.Equal("", Filter.CraftFamily);
            Assert.True(Filter.Family.IsAny);
        });
    }

    [CorpusFact]
    public void A_family_narrows_the_crafted_finds_and_is_kept_with_the_search()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Filter.Rarity = Rarity("Crafted");
        });
        Assert.True(program.Until(() => Finds().Count > 0), "the pad's crafted items were not found: " + program.On(() => Search.Found));

        // What each family should find, read off the crafted finds' own items: the wait below is for that count, so
        // it needs no timer, and a family that found nothing where it has items cannot pass.
        var byFamily = program.On(() => Finds().GroupBy(find => AffixRanges.CraftFamilyOf(find.Tile.Item) ?? "").ToDictionary(group => group.Key, group => group.Count()));
        int crafted = byFamily.Values.Sum();

        string[] families = ["Blood", "Caster", "Hitpower", "Safety", "Vampiric", "Bountiful", "Brilliant"];
        var found = families.Where(byFamily.ContainsKey).ToList();
        Assert.True(found.Count > 0, "no crafted find of the pad is of a named family");

        foreach (string family in families)
        {
            int expected = byFamily.GetValueOrDefault(family);
            program.On(() => Filter.Family = FamilyOf(family));
            Assert.True(program.Until(() => Finds().Count == expected),
                        $"{family}: {program.On(() => Finds().Count)} finds, {expected} of {crafted} crafted are of it");
            program.On(() => Assert.All(Finds(), find => Assert.Equal(family, AffixRanges.CraftFamilyOf(find.Tile.Item))));
        }

        // It narrows: another family of the pad's, or one it has none of, leaves out what the first found.
        Assert.Contains(families, family => byFamily.GetValueOrDefault(family) < crafted);

        program.On(() =>
        {
            // Left and come back to, and kept as a preset's: the family is part of what is asked.
            Filter.Family = FamilyOf("Blood");
            var kept = Filter.Snapshot();
            Assert.Equal("Blood", kept.CraftFamily);

            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Assert.Equal("", Filter.CraftFamily);
            Assert.Contains("Blood craft", Main.SearchBookmarkHint);

            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Assert.Equal("Blood", Filter.CraftFamily);
            Assert.Equal("Crafted", Filter.Rarity.Label);
            Assert.True(Filter.CanPickFamily);

            // The brush empties it with the rest.
            Search.Panel.ClearSearchCommand.Execute(null);
            Assert.Equal("", Filter.CraftFamily);
            Assert.False(Filter.CanPickFamily);
        });
    }
}
