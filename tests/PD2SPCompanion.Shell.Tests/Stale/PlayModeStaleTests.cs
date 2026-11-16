using Avalonia;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using PD2SPCompanion.Shell.Helper.ViewLocator;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Stale;

/// <summary>
/// Every kept page follows the play mode as it changes: Legit, Casual and Free each way, and Custom with each field of
/// <see cref="PlayPolicy"/> changed alone, with the pad's mercenary hired and an item open (StaleCheck). The modes are what
/// the program promises in public, and a page is kept, not built again, when Settings changes one.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PlayModeStaleTests(Program program)
{
    /// <summary>
    /// Answers no view binds, each with why its change needs no word. A name goes here only with its reason and only after
    /// looking in the .axaml files: one a view does bind is a bug to fix, however many other places read it.
    /// </summary>
    private static readonly Dictionary<string, string> NoViewBinds = new()
    {
        ["SaveSet.Version"] = "a count the search's index keeps in code; the home page's rescan, which a mode asks for, moves it.",
        ["VP_ViewModel_Inv_R.IsFreeEditing"] = "read only by CanAddStat, which is told; the views bind that.",
    };

    private List<(string Name, object Page)> KeptPages() => program.On(() =>
    {
        var services = ((App)Application.Current!).Services;
        return new List<(string Name, object Page)>
        {
            ("Main", program.Main),
            ("Home", program.Get<VP_ViewModel_Default_L>()),
            ("HomeRight", program.Get<VP_ViewModel_Default_R>()),
            ("Character", program.Get<VP_ViewModel_Char_L>()),
            ("Skills", program.Get<VP_ViewModel_Char_R>()),
            ("Waypoints", program.Get<WaypointsViewModel>()),
            ("Quests", program.Get<QuestsViewModel>()),
            ("MapSeed", program.Get<MapSeedViewModel>()),
            ("Items", program.Get<VP_ViewModel_Inv_L>()),
            ("Edit", program.Get<VP_ViewModel_Inv_R>()),
            ("Settings", program.Get<VP_ViewModel_Settings_L>()),
            ("SettingsRight", program.Get<VP_ViewModel_Settings_R>()),
            ("Vault", program.Get<VP_ViewModel_Vault_L>()),
            ("RightVault", services.GetRequiredKeyedService<VP_ViewModel_Vault_L>(MainViewPage_ViewLocator.RightSide)),
            ("VaultEdit", program.Get<VP_ViewModel_Vault_R>()),
            ("Trade", program.Get<VP_ViewModel_Trade>()),
            ("Extra", program.Get<VP_ViewModel_Extra_L>()),
            ("ExtraRight", program.Get<VP_ViewModel_Extra_R>()),
            ("Experimental", program.Get<VP_ViewModel_Experimental_L>()),
            ("ExperimentalRight", program.Get<VP_ViewModel_Experimental_R>()),
        };
    });

    /// <summary>One change, with what is owed done before and after it: what it moved with no word of it is added to <paramref name="found"/>.</summary>
    private void Through(StaleCheck check, string change, Action make, Dictionary<string, string> found)
    {
        program.On(check.Settle);
        program.Turned();
        program.On(check.Began);
        program.On(make);
        program.Turned();
        program.On(check.Settle);
        program.Turned();
        foreach (var finding in program.On(check.Ended))
            found.TryAdd(finding.Key, $"{finding.Key}: {finding.Was} -> {finding.Is} ({change}; {finding.Path})");
    }

    private static string Report(Dictionary<string, string> found) =>
        $"{found.Count} answers moved with no word:{Environment.NewLine}"
        + string.Join(Environment.NewLine, found.Values.Order(StringComparer.Ordinal));

    /// <summary>
    /// The items a run is made with: one with properties to edit, and one with its sockets filled, whose section answers
    /// questions the first never asks. Each selected as a click selects it.
    /// </summary>
    private IEnumerable<Action> ItemsOpened() =>
    [
        () => program.Select("Beast Necklace"),
        () => program.Main.Session.SelectedItem = program.Tiles().First(tile => tile.Item.SocketedItems.Count > 0),
    ];

    private void Presets(StaleCheck check, Dictionary<string, string> found)
    {
        var settings = program.On(() => program.Get<EditorSettings>());
        string[] road = [PlayPolicy.LegitName, PlayPolicy.CasualName, PlayPolicy.FreeName, PlayPolicy.LegitName, PlayPolicy.FreeName, PlayPolicy.CasualName, PlayPolicy.LegitName];
        for (int step = 1; step < road.Length; step++)
        {
            string to = road[step];
            Through(check, $"{road[step - 1]} to {to}", () => settings.ChoosePolicy(PlayPolicy.Preset(to)!), found);
        }
    }

    [CorpusFact]
    public void Between_the_three_modes_every_kept_page_says_what_it_changes()
    {
        if (!program.Ready(changes: true))
            return;

        var found = new Dictionary<string, string>();
        using var legit = program.InMode(PlayPolicy.LegitName);
        var check = new StaleCheck(KeptPages(), NoViewBinds);
        try
        {
            foreach (var open in ItemsOpened())
            {
                program.On(open);
                Presets(check, found);

                // That it looked at the mercenary's page and the open item's rows, not at nothing.
                Assert.True(program.On(() => program.EditPage().Stats.Count > 0 && program.Main.Inventory!.HasMerc), "no item open, or no mercenary hired");
                Assert.Contains(nameof(VP_ViewModel_Inv_L), check.TypesReached);
                Assert.Contains(nameof(StatRowViewModel), check.TypesReached);
            }
        }
        finally
        {
            program.On(program.Deselect);
        }

        Assert.True(found.Count == 0, Report(found));
    }

    /// <summary>The same with an item on the vault's own Edit page, which is its own panel and is only told as the character's is.</summary>
    [CorpusFact]
    public void Between_the_three_modes_the_vaults_item_says_what_it_changes()
    {
        if (!program.Ready(changes: true))
            return;

        var found = new Dictionary<string, string>();
        using var legit = program.InMode(PlayPolicy.LegitName);
        program.OnTheVaultsEditPage("crs", 1, "Crystal Sword");
        Presets(new StaleCheck(KeptPages(), NoViewBinds), found);

        Assert.True(found.Count == 0, Report(found));
    }

    /// <summary>
    /// Each of the policy's fields changed alone, and back. A new field is taken up by being one: nothing here names them.
    /// Sockets past the base's maximum are not a setting (<see cref="PlayPolicy.Normalized"/>), so that one has one change.
    /// </summary>
    [CorpusFact]
    public void Custom_with_each_field_changed_alone_every_kept_page_says_what_it_changes()
    {
        if (!program.Ready(changes: true))
            return;

        const string Name = "StaleCheck";
        var found = new Dictionary<string, string>();
        var settings = program.On(() => program.Get<EditorSettings>());
        string name = program.On(() => settings.CustomName);
        var check = new StaleCheck(KeptPages(), NoViewBinds);
        using var casual = program.InMode(PlayPolicy.CasualName);
        try
        {
            foreach (var open in ItemsOpened())
            {
                program.On(open);
                foreach (var (field, policy) in EachChangedAlone(PlayPolicy.Casual))
                {
                    Through(check, $"Casual to Custom, {field}", () => settings.ChoosePolicy(policy, Name), found);
                    Through(check, $"Custom, {field}, to Casual", () => settings.ChoosePolicy(PlayPolicy.Casual), found);
                }
            }
        }
        finally
        {
            program.On(() =>
            {
                program.Deselect();
                settings.ChoosePolicy(PlayPolicy.Casual);
                if (settings.Custom(Name) is { } kept)
                    settings.ForgetCustom(kept);
                settings.CustomName = name;
            });
        }

        Assert.True(found.Count == 0, Report(found));
    }

    /// <summary>A policy for each other value each field of it could have, by what the record itself is made of.</summary>
    private static IEnumerable<(string Field, PlayPolicy Policy)> EachChangedAlone(PlayPolicy from)
    {
        var made = typeof(PlayPolicy).GetConstructors().Single();
        var parts = made.GetParameters();
        object?[] values = [.. parts.Select(part => typeof(PlayPolicy).GetProperty(part.Name!)!.GetValue(from))];
        for (int at = 0; at < parts.Length; at++)
        {
            object?[] others = values[at] switch
            {
                bool on => [!on],
                Reach reach => [.. Enum.GetValues<Reach>().Where(each => each != reach).Cast<object?>()],
                _ => throw new InvalidOperationException($"{parts[at].Name}: a kind of field this test does not know."),
            };

            foreach (object? other in others)
            {
                object?[] changed = [.. values];
                changed[at] = other;
                var policy = ((PlayPolicy)made.Invoke(changed)).Normalized();
                if (policy != from)
                    yield return ($"{parts[at].Name} {other}", policy);
            }
        }
    }

    // ---- that the check itself finds what it is for, on models of its own, with no program

    private sealed class Told : ObservableObject
    {
        public int Behind { get; set; }

        public bool Stale => Behind > 0;

        public bool Said => Behind > 0;

        public bool Throws => Behind > 0 ? throw new InvalidOperationException() : false;

        public void Say(string? name) => OnPropertyChanged(name);
    }

    private sealed class Holder : ObservableObject
    {
        public Told One { get; } = new();

        public List<Told> Rows { get; } = [new()];
    }

    private static List<string> Keys(StaleCheck check, Action change)
    {
        check.Began();
        change();
        return [.. check.Ended().Select(finding => finding.Key).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void The_check_finds_an_answer_moved_with_no_word_and_a_getter_that_began_to_throw()
    {
        var told = new Told();
        var check = new StaleCheck([("Told", told)]);
        Assert.Equal(["Told.Behind", "Told.Said", "Told.Stale", "Told.Throws"], Keys(check, () => told.Behind = 1));
    }

    [Fact]
    public void A_name_heard_clears_that_answer_and_nothing_else()
    {
        var told = new Told();
        var check = new StaleCheck([("Told", told)]);
        Assert.Equal(["Told.Behind", "Told.Stale", "Told.Throws"], Keys(check, () =>
        {
            told.Behind = 1;
            told.Say(nameof(Told.Said));
        }));
    }

    [Fact]
    public void An_empty_name_heard_clears_every_answer()
    {
        var told = new Told();
        var check = new StaleCheck([("Told", told)]);
        Assert.Empty(Keys(check, () =>
        {
            told.Behind = 1;
            told.Say("");
        }));
    }

    [Fact]
    public void The_check_reaches_what_a_page_holds_and_its_lists_and_skips_what_is_excluded()
    {
        var holder = new Holder();
        var check = new StaleCheck([("Holder", holder)], new Dictionary<string, string>
        {
            ["Told.Behind"] = "the test's",
            ["Told.Stale"] = "the test's",
            ["Told.Throws"] = "the test's",
        });
        Assert.Equal(["Told.Said", "Told.Said"], Keys(check, () =>
        {
            holder.One.Behind = 1;
            holder.Rows[0].Behind = 1;
        }));
    }

    [Fact]
    public void A_row_replaced_is_not_compared_with_the_one_before()
    {
        var holder = new Holder();
        var check = new StaleCheck([("Holder", holder)]);
        Assert.Empty(Keys(check, () =>
        {
            holder.Rows[0].Behind = 1;
            holder.Rows[0] = new Told();
        }));
    }
}
