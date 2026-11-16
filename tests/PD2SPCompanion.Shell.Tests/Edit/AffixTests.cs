using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The Affixes section of the Edit page on the worn rare amulet (AffixEditor, RareNameEditor). The name: its
/// two lists say "No match." only while a search leaves one empty; a half picked is the name at once and the
/// picker stays open for the other half; closed, the amulet is the Doom Necklace. The affixes: one rolled in
/// the picker for the slot it opens on and confirmed is that slot's affix; the section's reroll gives a whole
/// new set in one press; and the legitimacy check has nothing to say of the result.
/// <para>
/// What the editors decide, asked through their commands and read back from the item. Nothing of it is the
/// window's alone.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class AffixTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private RareNameEditor Name => Edit.RareName;
    private AffixEditor Affixes => Edit.Affixes;

    private static string Ids(Item item) =>
        $"{string.Join("/", item.MagicPrefixIds ?? [])} | {string.Join("/", item.MagicSuffixIds ?? [])}";

    [CorpusFact]
    public void The_names_lists_say_no_match_only_while_a_search_leaves_one_empty()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            program.Select("Beast Necklace");
            Assert.True(Name.HasName);
            Assert.Equal("Beast Necklace", Name.Name);

            var told = new List<string?>();
            void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs changed) => told.Add(changed.PropertyName);

            Name.OpenPickerCommand.Execute(null);
            Name.PropertyChanged += Heard;
            try
            {
                Assert.Contains(Name.FirstNames, half => half.Name == "Doom");
                Assert.False(Name.NoFirstNames);
                Assert.False(Name.NoSecondNames);
                Assert.Single(Name.FirstNames, half => half.IsHeld);
                Assert.Equal("Beast", Name.FirstNames.First(half => half.IsHeld).Name);

                // Told again wherever the list is: read once and never raised, the note stood over a full list.
                Name.FirstSearch = "zzzz";
                Assert.True(Name.NoFirstNames);
                Assert.False(Name.NoSecondNames);
                Assert.Contains(nameof(RareNameEditor.NoFirstNames), told);

                told.Clear();
                Name.FirstSearch = "";
                Assert.False(Name.NoFirstNames);
                Assert.Contains(nameof(RareNameEditor.NoFirstNames), told);
            }
            finally
            {
                Name.PropertyChanged -= Heard;
                Name.ClosePickerCommand.Execute(null);
                program.Deselect();
            }
        });
    }

    /// <summary>
    /// The rare name picker's skull: a name at random, another than it was, the picker open
    /// on it - drawn from the whole of each list, whatever a search has narrowed them to.
    /// </summary>
    [CorpusFact]
    public void The_name_pickers_skull_gives_another_name_from_the_whole_lists()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Beast Necklace");
            Name.OpenPickerCommand.Execute(null);
            Name.FirstSearch = "Doom";
            Assert.Single(Name.FirstNames);

            var seen = new HashSet<string>();
            for (int press = 0; press < 12; press++)
            {
                string was = Name.Name;
                Name.RollCommand.Execute(null);
                Assert.NotEqual(was, Name.Name);
                Assert.True(Name.IsPickerOpen);
                seen.Add(Name.Name.Split(' ')[0]);
            }

            // Twelve presses never all land on the one first half the search shows.
            Assert.True(seen.Count > 1, string.Join(", ", seen));

            Name.FirstSearch = "";
            Name.ClosePickerCommand.Execute(null);
        });
        program.Settled();
    }

    [CorpusFact]
    public void A_half_picked_is_the_name_at_once_the_picker_stays_for_the_other_and_closed_it_is_the_items()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            Name.OpenPickerCommand.Execute(null);
            Name.PickFirstCommand.Execute(Name.FirstNames.First(half => half.Name == "Doom"));

            Assert.Equal("Doom Necklace", Name.DraftName);
            Assert.Equal("Doom Necklace", Name.Name);
            Assert.True(Name.IsPickerOpen);
            Assert.True(Name.FirstNames.First(half => half.Name == "Doom").IsHeld);

            // Nothing is written while the picker stands.
            Assert.Equal("Beast Necklace", program.TileOf(amulet).Name);

            Name.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => program.TileOf(amulet).Name == "Doom Necklace"), "the amulet is not the Doom Necklace");
        program.Settled();
        program.On(() =>
        {
            Assert.Equal("Doom Necklace", program.Worn(ItemLocation.Neck).Item!.Name);
            Assert.Equal("Doom Necklace", Edit.Summary?.Name);
            Assert.False(Name.IsChanged);
        });
    }

    [CorpusFact]
    public void An_affix_rolled_in_the_picker_is_the_slots_affix_at_once()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        int rolled = 0;
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            Assert.Contains(Affixes.AffixPairs, pair => pair.PrefixName == "Wyrm's");

            Affixes.OpenAffixPickerCommand.Execute(null);
            Assert.True(Affixes.IsAffixPickerOpen);

            // The picker opens on the first prefix; a roll puts one in it at once, weighed as the game weighs them,
            // and the picker stays open, as the corruption's does.
            Affixes.RollAffixCommand.Execute(null);
            rolled = Affixes.ChosenAffix!.Id;
            Assert.True(rolled > 0);
            Assert.True(Affixes.IsAffixPickerOpen);
            Assert.StartsWith("Beast Necklace: affixes changed", program.Main.Session.RightMessages.Text);
            Assert.Contains(Affixes.AffixChoices, row => row.IsHeld && row.Option.Id == rolled);

            // Closing takes nothing back: the page keeps it.
            Affixes.CloseAffixPickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => amulet.MagicPrefixIds[0] == rolled), "the rolled affix is not the first prefix");
        program.Settled();
        program.On(() =>
        {
            Assert.False(Affixes.AnyStaged);
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void The_sections_reroll_gives_a_whole_new_set_of_affixes_in_one_press()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        string was = "";
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            was = Ids(amulet);
            Assert.True(Affixes.CanRerollAffixes);
        });

        // A roll may land on the set it had: asked again, a few times at most.
        for (int press = 0; press < 5 && program.On(() => Ids(amulet)) == was; press++)
        {
            program.On(() =>
            {
                Affixes.RerollAllAffixesNowCommand.Execute(null);
                Assert.False(Affixes.IsAffixPickerOpen);
            });
            program.Settled();
        }

        program.On(() =>
        {
            Assert.NotEqual(was, Ids(amulet));
            Assert.Equal(ItemQuality.Rare, amulet.Quality);
            Assert.Empty(Edit.Findings());
        });
    }
}
