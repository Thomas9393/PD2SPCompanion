using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's runeword builder: a word made in place on a base with the sockets for it, or
/// a runeword cleaned back to its white base, staged into the panel's <see cref="ItemDraft"/>
/// until KEEP.
/// </summary>
public sealed partial class RunewordEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public RunewordEditor(IItemPanel panel) => _panel = panel;

    private ItemDraft Draft => _panel.Draft;

    /// <summary>
    /// Whether the Zod button shows: the play mode builds runewords and the base can hold some
    /// word. Not while a socket change waits: the words are the ones the item's sockets take, and
    /// those are about to change.
    /// </summary>
    public bool CanBuild =>
        _panel.Rules.BuildsRunewords
        && _panel.Transforms
        && _panel.Selected is { TabSlot: null } tile
        && !_panel.SocketsAreChanged
        && RunewordBuilder.WordsFor(tile.Item).Count > 0;

    public bool IsStaged => Draft.Runeword is not null;

    /// <summary>A staged list worn by a shadow copy, which must not share the stats the rows edit.</summary>
    private static ItemStatList CopyOf(ItemStatList list)
    {
        var copy = new ItemStatList();
        copy.Stats.AddRange(list.Stats.Select(stat => new ItemStat
        {
            Id = stat.Id, Stat = stat.Stat, SkillTab = stat.SkillTab, CharacterClass = stat.CharacterClass,
            SkillId = stat.SkillId, SkillLevel = stat.SkillLevel, MaxCharges = stat.MaxCharges,
            Param = stat.Param, Value = stat.Value,
        }));
        return copy;
    }

    /// <summary>
    /// The staged runeword on a shadow copy, for everything that asks questions of the item as
    /// the panel has it.
    /// </summary>
    public void Wear(Item shadow)
    {
        if (Draft.Runeword is not { } staged)
            return;

        try
        {
            if (staged is { Word: { } word, List: { } list })
                ItemForge.ApplyRuneword(shadow, word, CopyOf(list));
            else
                RunewordBuilder.Clean(shadow);
        }
        catch (Exception)
        {
            // A shadow that cannot wear it answers as the item is; KEEP says why.
        }
    }

    /// <summary>
    /// Writes the staged change onto the item itself at KEEP, after the sockets: the same list the
    /// pending rows have just written their values into goes onto the item.
    /// </summary>
    public void Commit(Item item)
    {
        if (Draft.Runeword is not { } rune)
            return;

        if (rune is { Word: { } word, List: { } words })
            ItemForge.ApplyRuneword(item, word, words);
        else
            RunewordBuilder.Clean(item);

        Draft.Runeword = null;
    }

    /// <summary>The Zod rune, which is what the builder's button wears.</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon =>
        SpriteBitmap.From(SpriteLoader.Current?.Load("invrZod"));

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>Kept apart from openness so the veil can fade out - see <see cref="Presence"/>.</summary>
    [ObservableProperty] private bool _isPickerPresent;

    partial void OnIsPickerOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPickerOpen, on => IsPickerPresent = on, VP_ViewModel_Inv_R.VeilFade);

    /// <summary>The word picked and its properties rolled, on its way to the item: Confirm stages it at once.</summary>
    [ObservableProperty]
    private RunewordBuilder.Draft? _chosen;

    /// <summary>The base the word goes into, for the panel to name - and the word it holds now, if one.</summary>
    public string Base =>
        _panel.Selected is not { } tile ? ""
        : tile.Item.IsRuneword ? L($"{tile.View.BaseName}, now {tile.Name}")
        : tile.Name;

    /// <summary>
    /// Whether the item is, or is about to be, a runeword: the panel then offers the white base
    /// back, and a word picked replaces the one it has.
    /// </summary>
    public bool IsNow =>
        Draft.Runeword is { } staged
            ? staged.Word is not null
            : _panel.Selected is { } tile && tile.Item.IsRuneword;

    /// <summary>Every word this base can hold, with the one chosen marked.</summary>
    public IReadOnlyList<RunewordChoiceRow> Choices =>
        _panel.Selected is { } tile
            ? [.. RunewordBuilder.WordsFor(tile.Item)
                .Select(word => new RunewordChoiceRow(word, ReferenceEquals(word, Chosen?.Word)))]
            : [];

    /// <summary>What the button and the picker read from the item and the draft, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(IsStaged));
        OnPropertyChanged(nameof(IsNow));
    }

    /// <summary>The picker belongs to the item that was showing when it opened; a rebuild closes it.</summary>
    public void Reset()
    {
        IsPickerOpen = false;
        Chosen = null;
    }

    /// <summary>
    /// Stages the white base back: a runeword cleaned, its runes gone - or, where the item is
    /// white and only a word was staged, that word taken back. KEEP writes it, UNDO drops it.
    /// </summary>
    [RelayCommand]
    private void Clean()
    {
        if (_panel.Selected is not { } tile || !IsNow)
            return;

        _panel.Press();

        Chosen = null;
        // Open still, as the corruption's picker: the pick is marked, another takes its place; written when it closes.
        Draft.Runeword = tile.Item.IsRuneword ? (null, null) : null;
        _panel.ShowStagedRuneword(tile);
        Notify();

        if (tile.Item.IsRuneword)
            _panel.Say(L($"{tile.Name}: white base, runes gone."));
        else
            _panel.Say(L($"{tile.Name}: runeword taken back."));
    }

    [RelayCommand]
    private void OpenPicker()
    {
        if (!CanBuild)
            return;

        _panel.Press();

        Chosen = null;
        OnPropertyChanged(nameof(Base));
        OnPropertyChanged(nameof(IsNow));
        OnPropertyChanged(nameof(Choices));
        IsPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePicker()
    {
        Chosen = null;
        IsPickerOpen = false;
        OnPropertyChanged(nameof(Choices));
    }

    /// <summary>
    /// Takes a word from the list, its properties rolled inside their ranges, and stages it - the
    /// picker closes (a pick is the change - written at once, and taken back by Ctrl + Z).
    /// </summary>
    [RelayCommand]
    private void Pick(RunewordChoiceRow? row)
    {
        if (row is null)
            return;

        _panel.Press();
        Chosen = RunewordBuilder.Roll(row.Word, new Random());
        Confirm();
    }

    /// <summary>
    /// Stages the word on the item, in place: worked out on a shadow of the item as the panel has
    /// it, and shown as pending rows. KEEP writes it with everything else; UNDO drops it.
    /// </summary>
    private void Confirm()
    {
        if (_panel.Selected is not { } tile || Chosen is not { } chosen)
            return;

        // Worked out on the item as it would be without a word staged already: a second pick
        // replaces the first rather than building on it.
        Draft.Runeword = null;
        if (_panel.Pending() is not { } shadow)
            return;

        try
        {
            ItemForge.MakeRuneword(shadow, chosen);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            _panel.Say(L($"{chosen.Name} not built: {problem.Message}"));
            return;
        }

        // The name it has before the word is its own: the change is kept at once, and the tile is then the word's.
        string name = tile.Name;

        Draft.Runeword = (chosen.Word, shadow.StatLists[^1]);
        Chosen = null;
        // Open still, as the corruption's picker: the pick is marked, another takes its place; written when it closes.
        _panel.ShowStagedRuneword(tile);
        Notify();

        _panel.Say(L($"{name}: becomes {chosen.Name}."));
    }
}
