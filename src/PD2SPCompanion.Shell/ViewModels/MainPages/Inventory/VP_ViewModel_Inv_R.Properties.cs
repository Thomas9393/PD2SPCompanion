using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using System.Linq;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Shell.ViewModels;

// The property list's header and the property picker.
public partial class VP_ViewModel_Inv_R
{
    /// <summary>
    /// Shown when the item has properties <b>or could be given one</b>, since the button
    /// that gives it one lives in this section's header - and so does the lock that has to
    /// come off first.
    /// <para>
    /// Asked of the item rather than of the page's mode. Taking the last property off an
    /// item emptied the list, and the next rebuild put free editing back to its default;
    /// with the section keyed on free editing, it vanished - taking with it the only way to
    /// turn free editing on again, and the item could never be given another property.
    /// </para>
    /// </summary>
    public bool HasStats => !IsTabTile && (Stats.Count > 0 || CanCarryStats);

    /// <summary>Whether this item has anywhere to keep a property, mode aside.</summary>
    public bool CanCarryStats => Session.SelectedItem is { } tile
        && !tile.Item.IsCompact
        && StatCatalogue.CanCarry(tile.Item.Code);

    /// <summary>Nothing on it yet, which is worth saying rather than showing a gap.</summary>
    public bool NoStats => Stats.Count == 0;

    public string NoStatsNote => L("No properties.");

    /// <summary>
    /// Whether a property can be put on this item at all. Two questions, and both have to
    /// answer yes.
    /// <para>
    /// <b>Is there room for one?</b> A simple item - a potion, a gem, a rune, gold - is a
    /// code and a place and nothing else, with no extended record for a property list.
    /// </para>
    /// <para>
    /// <b>Would the game ever put one there?</b> Saving a full record is not enough: a
    /// Worldstone Shard, a Skeleton Key and the Horadric Cube all do, and no affix in the
    /// game can reach any of them. See <see cref="StatCatalogue.CanCarry"/>.
    /// </para>
    /// </summary>
    public bool CanAddStat => IsFreeEditing && CanCarryStats;


    /// <summary>
    /// The property picker (PropertyPicker.axaml, the search panel's too): the save's own properties,
    /// narrowed as the search's are - every word typed, in any order, against the game's wording or the
    /// table's name - or a number, against the id, an exact one first.
    /// </summary>
    public PropertyPickerViewModel StatPicker => _statPicker ??= new(
        search =>
        {
            // A map's properties on a map and nowhere else, and nothing else on a map
            // (StatCatalogue.OfferedOn). The raw list is every stat, whatever the item.
            bool raw = Rules.ListsRawProperties;
            bool map = ItemOrder.IsMap(Session.SelectedItem?.Item.Code);
            var listed = raw ? RawStats : SortedStatsFor(map);
            string wanted = search.Trim();
            if (wanted.Length == 0)
                return listed;

            string[] words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool isNumber = wanted.All(char.IsDigit);

            // The game's words answer too where the line shows the engine's name (the raw list).
            return [.. listed.Where(choice =>
                (isNumber && choice.Id.ToString().StartsWith(wanted, StringComparison.Ordinal))
                || words.All(word => choice.Text.Contains(word, StringComparison.OrdinalIgnoreCase)
                                     || choice.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                                     || (raw && choice.Wording.Contains(word, StringComparison.OrdinalIgnoreCase))))
                .OrderBy(choice => !(isNumber && choice.Id.ToString() == wanted))];
        },
        choice => AddStat((StatChoice)choice),
        _sfxManager);

    private PropertyPickerViewModel? _statPicker;

    /// <summary>
    /// The properties A to Z by their words, as the search's list is (PropertyIndex.Sortable),
    /// the ones the list notes - obsolete, the interface's, the engine's own - after the rest.
    /// A stat that names a class, a tree or a skill is a line for each (StatCatalogue.Pickable).
    /// Sorted once for the season the catalogue was read for, once its skills are in.
    /// </summary>
    private static IReadOnlyList<StatChoice> SortedStats
    {
        get
        {
            if (!ReferenceEquals(_sortedFrom, StatCatalogue.All) || _sortedWithoutSkills)
            {
                _sortedFrom = StatCatalogue.All;
                _sortedWithoutSkills = SkillNames.Current is null;
                _sorted = [.. StatCatalogue.Pickable()
                    .OrderBy(choice => choice.HasNote)
                    .ThenBy(choice => PropertyIndex.Sortable(choice.Text), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(choice => choice.Id)];
            }
            return _sorted;
        }
    }

    private static IReadOnlyList<StatChoice>? _sortedFrom;

    /// <summary><see cref="SortedStats"/> as a map is offered it, or as anything else is (<see cref="StatCatalogue.OfferedOn"/>).</summary>
    private static IReadOnlyList<StatChoice> SortedStatsFor(bool map)
    {
        var sorted = SortedStats;
        if (!ReferenceEquals(_offeredFrom, sorted))
        {
            _offeredFrom = sorted;
            _onMaps = [.. sorted.Where(choice => StatCatalogue.OfferedOn(choice.Name, map: true))];
            _offMaps = [.. sorted.Where(choice => StatCatalogue.OfferedOn(choice.Name, map: false))];
        }
        return map ? _onMaps : _offMaps;
    }

    private static IReadOnlyList<StatChoice>? _offeredFrom;
    private static IReadOnlyList<StatChoice> _onMaps = [];
    private static IReadOnlyList<StatChoice> _offMaps = [];

    /// <summary>
    /// The raw list (the play mode's "Raw property list", Custom's alone): every stat once, by id, named
    /// as the tables name it - item_addskill_tab, not a line per tree - for those who know the engine.
    /// </summary>
    private static IReadOnlyList<StatChoice> RawStats
    {
        get
        {
            if (!ReferenceEquals(_rawFrom, StatCatalogue.All))
            {
                _rawFrom = StatCatalogue.All;
                _raw = [.. StatCatalogue.All.OrderBy(choice => choice.Id)
                    .Select(choice => choice with { Worded = choice.Name })];
            }
            return _raw;
        }
    }

    private static IReadOnlyList<StatChoice>? _rawFrom;
    private static IReadOnlyList<StatChoice> _raw = [];
    private static bool _sortedWithoutSkills;
    private static IReadOnlyList<StatChoice> _sorted = [];

    [RelayCommand]
    private void OpenStatPicker() => StatPicker.Open();

    /// <summary>
    /// Puts a property on the item, at the lowest value its field will hold that is something.
    /// <para>
    /// Whatever the game writes as a run is added whole: cold damage is three properties,
    /// and a run missing its tail makes the writer reach past the end of the list. See
    /// <see cref="StatCatalogue.RunFrom"/>.
    /// </para>
    /// </summary>
    private void AddStat(StatChoice? choice)
    {
        if (choice is null || Session.SelectedItem is not { } tile)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        // What no property writes apart goes on together: Enhanced Damage's
        // maximum and minimum, a flat damage's copies - those the base holds - a map's paired lines.
        // A partner the item's own list already has is left as it is: its number is the row's to take.
        var ids = StatCatalogue.RunFrom(choice.Id).ToList();
        foreach (string name in ids.Select(id => StatCatalogue.NameOf(id) ?? "").ToList())
        {
            foreach (string partner in AffixRanges.WrittenWith(tile.Item, name))
            {
                if (StatCatalogue.IdOf(partner) is { } id && !ids.Contains(id)
                    && !Stats.Any(row => !row.IsDeleted && !row.IsRuneword && !row.IsSetBonus
                                         && partner.Equals(row.Stat.Stat, StringComparison.OrdinalIgnoreCase)))
                    ids.Add(id);
            }
        }

        // And a flat damage is the copies the base holds: "+X to Maximum Damage" picked on a two-handed
        // weapon is its two-handed copy, not the one-handed one the line names (DamageKinds). The raw
        // list adds what was picked, as it says.
        if (!Rules.ListsRawProperties)
            ids.RemoveAll(id => !DamageKinds.Holds(tile.Item, StatCatalogue.NameOf(id) ?? ""));

        foreach (int id in ids)
        {
            // The lowest the field holds, but never below nothing: a property whose Save
            // Add allows -400 is legal there and reads as damage, not as a starting point.
            // Nor nothing itself, where the field holds more: a property picked is the item's
            // at once, and "+0 to Sorceress Skill Levels" was written to the save
            // until a number followed - a line the game never writes.
            var held = StatFields.RangeOf(StatCatalogue.NameOf(id) ?? "");
            int floor = Math.Min(Math.Max(1, held?.Min ?? 1), Math.Max(0, held?.Max ?? int.MaxValue));

            string name = StatCatalogue.NameOf(id) ?? "";

            var stat = new ItemStat
            {
                Id = (ushort)id,
                Stat = name,
                Value = floor,
            };

            // A stat that packs a skill needs its skill fields to exist before the row can
            // offer them - a null skill id reads as "this property names no skill", and the
            // picker never appeared on the properties that most need one.
            switch (StatFields.EncodingOf(name))
            {
                case 2:
                    stat.SkillId = 0;
                    stat.SkillLevel = 1;
                    break;

                case 3:
                    stat.SkillId = 0;
                    stat.SkillLevel = 1;
                    stat.MaxCharges = 1;
                    stat.Value = 1;
                    break;
            }

            // A line for one class, tree or skill comes with it chosen: the row's own field, set as
            // the row would set it - a tree also split into the class and tree the wording reads.
            if (id == choice.Id && choice.Param is int param)
            {
                stat.Param = param;
                if (StatShapes.Of(name) == StatShape.SkillTree)
                {
                    stat.SkillTab = param & 0x7;
                    stat.CharacterClass = (param >> 3) & 0x1fff;
                }
            }

            // An element starts at the first one: its list opens with "(none)", which no item means,
            // and the row read "+0 to (none) Skills". Every other list's first entry is a real one.
            if (id == choice.Id && stat.Param is null && StatShapes.Of(name) == StatShape.Element)
                stat.Param = 1;

            // A dye the same way: 0 is no colour, and the row read "Tint 0" over an empty box.
            if (name.Equals(Dyes.Stat, StringComparison.OrdinalIgnoreCase) && stat.Value == 0
                && Dyes.Colours.Count > 0)
            {
                stat.Value = Dyes.Colours[0].Dye;
            }

            _added.Add(stat);
            Stats.Add(Row(tile, stat, added: true));

            // The one the line names, of a run: brought into sight when it is kept (ShowStatRow).
            if (id == choice.Id)
                _picked = stat;
        }


        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(StatsHeader));
        NotifyChanges();
    }

    public string StatsHeader
    {
        get
        {
            int going = Stats.Count(row => row.IsDeleted);
            return going > 0
                ? L($"Properties  ({Stats.Count - going} / {Stats.Count})")
                : L($"Properties  ({Stats.Count})");
        }
    }

    /// <summary>
    /// Shows a staged runeword change in the property rows: the word's old lines struck through,
    /// and the new word's lines under its name, pending - editable as any row is, and written with
    /// everything else at KEEP.
    /// </summary>
    private void ShowStagedRuneword(ItemTileViewModel tile)
    {
        foreach (var row in Stats.Where(row => row.IsRuneword && row.IsAdded).ToList())
            Stats.Remove(row);

        foreach (var row in Stats.Where(row => row.IsRuneword))
            row.IsDeleted = true;

        if (Draft.Runeword is { Word: { } word, List: { } list }
            && Pending() is { } shadow)
        {
            var granted = AffixRanges.AddedTo(shadow);
            var shown = AffixRanges.DisplayedFor(shadow);
            bool first = true;

            foreach (var stat in list.Stats)
            {
                var row = Row(tile, stat, added: true, rolls: granted, alongside: list.Stats, painted: shown);
                row.MarkGroup(runeword: true, set: false, heading: first ? L($"{Runewords.DisplayName(word)} (pending)") : "");
                first = false;
                Stats.Add(row);
            }
        }

        Runeword.Notify();
        Sockets.Notify();
        NotifyChanges();
    }

    /// <summary>
    /// The properties the corruption wrote, staged corruption included - which is
    /// what the red mark beside a property answers to.
    /// </summary>
    private (HashSet<string> Corrupted, HashSet<string> Desecrated) CorruptedStats =>
    	Session.SelectedItem is { } tile && (Corruption.Now > 0 || Desecration.Now > 0)
    		? MarkedOn(Staged(tile.Item))
    		: (new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The properties a corruption wrote, and those a desecration wrote. The game marks both
    /// with an asterisk each; the page says which.
    /// </summary>
    private static (HashSet<string> Corrupted, HashSet<string> Desecrated) MarkedOn(Item item) =>
        (new(Corruptions.StatsOn(item), StringComparer.OrdinalIgnoreCase),
         new(Desecrations.StatsOn(item), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Puts what the change worked out onto the property rows: values on the rows that
    /// have them, new rows for what the item did not carry before, and a struck-through
    /// row for what the change takes away. All of it reversible until KEEP.
    /// </summary>
    private void StageStatsFrom(Item shadow, ItemTileViewModel tile)
    {
        // Every number comes from the shadow; no row moves another (MoveTogether).
        _settingRows = true;
        try
        {
            StageRowsFrom(shadow, tile);
        }
        finally
        {
            _settingRows = false;
        }
    }

    private void StageRowsFrom(Item shadow, ItemTileViewModel tile)
    {
        var fresh = shadow.StatLists.FirstOrDefault()?.Stats ?? [];

        // The ranges the item will have, not the ones it had. A row still holding its old
        // range clips the number this very change is staging.
        // The item's own list's alone, as BuildStats asks them: the rows staged here are that list's, and with a
        // runeword's ranges added a superior base's Enhanced Damage was ranged - and its die rolled - as the word's
        // as well (a Faith at 600 and more).
        var ranges = AffixRanges.OwnOf(shadow);

        // And the bracket the game would paint on it, which moves with the affixes too.
        var painted = AffixRanges.DisplayedFor(shadow);

        var corrupted = MarkedOn(shadow);
        var vanished = new List<StatRowViewModel>();

        // Matched by key, not by name (AffixRanges.KeyOf): a stat that names a skill, a skill
        // tree, a class or a monster is a different line for each one, and one stat can carry
        // several. By name alone a rerolled "+1 to Masteries (Barbarian Only)" landed in the old
        // "+1 to Javelin and Spear Skills" row as its number, the tree left behind.
        var unmatched = fresh.ToList();

        foreach (var row in Stats)
        {
            if (row.Stat.Stat is not { Length: > 0 } name)
                continue;

            // A set's bonuses and a runeword's lines are in the lists after the item's own, which no
            // change staged here writes - a corruption, a desecration, an affix, a craft, an upgrade
            // all work on the first. Matched against the first list they were never found, so they
            // were struck and KEEP took them off: a set piece corrupted here lost its set bonus.
            // They stay as they are while the item keeps those lists.
            if (row.IsSetBonus || row.IsRuneword)
            {
                if (shadow.StatLists.Count <= 1 && !row.IsAdded)
                    row.IsDeleted = true;
                continue;
            }

            string key = AffixRanges.KeyOf(row.Stat);

            // The range by key first, then by name - as Row reads it.
            row.Retune(
                ranges.TryGetValue(key, out var range) || ranges.TryGetValue(name, out range) ? range : null,
                painted.TryGetValue(key, out var bracket) || painted.TryGetValue(name, out bracket) ? bracket : null);
            row.MarkCorrupted(corrupted.Corrupted.Contains(name), corrupted.Desecrated.Contains(name));

            var found = unmatched.FirstOrDefault(each => AffixRanges.KeyOf(each) == key);

            if (found is null)
            {
                // A line an earlier change added and this one no longer writes was never on the
                // item: it goes, rather than staying as "pending to add" - a second reroll left
                // the first one's line behind.
                if (row.IsAdded)
                {
                    vanished.Add(row);
                    continue;
                }

                row.IsDeleted = true;
                continue;
            }

            unmatched.Remove(found);
            row.IsDeleted = false;
            row.Value = found.Value.ToString();

            // And what the number does not carry: the level its skill is cast at, its charges.
            row.TakePacked(found);
        }

        foreach (var row in vanished)
        {
            Stats.Remove(row);
            _added.Remove(row.Stat);
        }

        foreach (var stat in unmatched)
        {
            if (stat.Stat is not { Length: > 0 } name)
                continue;

            // The pair a corruption keeps for itself is not a property and is never listed.
            // BuildStats has always skipped them; staging did not, so confirming a corruption
            // put "Corrupted 50" and "Corrupt 1" in the list as things to edit.
            if (name is Corruptions.IdStat or Corruptions.FlagStat
                or Desecrations.ValueStat or Desecrations.FlagStat)
            	continue;

            _added.Add(stat);

            // With the ranges the item will have: asked of the stored item, a line the change
            // itself adds - a corruption's, a desecration's - had no range to show.
            Stats.Add(Row(tile, stat, added: true, rolls: ranges, painted: painted));
        }

        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(StatsHeader));
    }

    [ObservableProperty] private bool _statsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(StatsOpen));

    partial void OnStatsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(StatsOpen), value);

}
