using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

// The mercenary's own lines over its slots, and its editing (MercenaryEdit). Three lines
// say it - its name, "Level 77 Barbarian", its variant - and a pencil after the name opens a popup
// in the character rename's style where the name, the variant and the level are changed and a dead one is
// revived (reworked the same night: the controls stood under the slots, "too repetitive and noisy").
public partial class VP_ViewModel_Inv_L
{
    /// <summary>The header: its name, or what it is where its type's list gives no name.</summary>
    public string MercName => Session.MercenaryName is { Length: > 0 } name ? name : Session.Mercenary.Title;

    /// <summary>Under the name: "Level 77 Barbarian".</summary>
    public string MercLevelLine => Session.Mercenary.LevelLine;

    /// <summary>The third line: "Combat (Might)".</summary>
    public string MercVariantName => Session.Mercenary.VariantText;

    /// <summary>
    /// Whether the mercenary can be edited: one is hired, and the play mode edits the character. Where the mode
    /// does not, the pencil is not there at all - hidden, not dimmed (dimmed is for what
    /// the mode allows and something else holds back just now).
    /// </summary>
    public bool EditsMerc => HasMerc && Rules.EditsCharacter;

    /// <summary>The popup, open; and still drawn while it fades out.</summary>
    [ObservableProperty] private bool _isMercEditOpen;

    [ObservableProperty] private bool _isMercEditPresent;

    partial void OnIsMercEditOpenChanged(bool value) =>
        Presence.Keep(value, () => IsMercEditOpen, on => IsMercEditPresent = on);

    [RelayCommand]
    private void EditMerc()
    {
        if (!EditsMerc)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        IsMercEditOpen = !IsMercEditOpen;
    }

    [RelayCommand]
    private void CloseMercEdit() => IsMercEditOpen = false;

    private IReadOnlyList<MercenaryEdit.Variant> _mercVariants = [];

    /// <summary>The names its type draws from.</summary>
    public IReadOnlyList<string> MercNames { get; private set; } = [];

    /// <summary>The variants of its own type, each once: its attack or aura, no difficulty.</summary>
    public IReadOnlyList<string> MercVariants { get; private set; } = [];

    public int MercNameIndex
    {
        get => Session.MercenaryNameIndex is var at && at < MercNames.Count ? at : -1;
        set
        {
            // -1 is the box emptied while its list is read again, not a choice.
            if (TakesMercEdit && value >= 0 && value != Session.MercenaryNameIndex)
                Session.SetMercenaryName(value);
        }
    }

    public int MercVariantIndex
    {
        get => Session.MercenaryVariantIndex is var at && at < MercVariants.Count ? at : -1;
        set
        {
            // The variant it already is, is not chosen again: one hired in Nightmare or Hell keeps its own row
            // until another variant is picked: no auto conversion.
            if (TakesMercEdit && value >= 0 && value < _mercVariants.Count && value != Session.MercenaryVariantIndex)
                Session.SetMercenaryVariant(_mercVariants[value].TypeId);
        }
    }

    /// <summary>The most its level may be: the character's own, or 98 where the play mode adds freely.</summary>
    public int MercLevelMost => Session.MercenaryMostLevel(Rules.AddsCharacterPoints);

    public string MercLevelText
    {
        get => Session.Mercenary.Level.ToString();
        set
        {
            if (TakesMercEdit && int.TryParse(value, out int level) && level >= 1 && level != Session.Mercenary.Level)
                Session.SetMercenaryLevel(level, Rules.AddsCharacterPoints);
        }
    }

    public bool CanLowerMercLevel => EditsMerc && Session.Mercenary.Level > 1;

    public bool CanRaiseMercLevel => EditsMerc && Session.Mercenary.Level < MercLevelMost;

    public string MercLevelHint => Rules.AddsCharacterPoints
        ? L($"Level 1-{MercenaryEdit.MostLevel}")
        : L($"Level 1-{MercLevelMost}, the character's own");

    [RelayCommand]
    private void LowerMercLevel() => StepMercLevel(-1);

    [RelayCommand]
    private void RaiseMercLevel() => StepMercLevel(1);

    /// <summary>Several levels at once - Ctrl and Shift on the steppers, as the character's level takes them.</summary>
    public void StepMercLevel(int levels)
    {
        if (!EditsMerc)
            return;

        Session.SetMercenaryLevel(Math.Clamp(Session.Mercenary.Level + levels, 1, MercLevelMost), Rules.AddsCharacterPoints);
    }

    /// <summary>The steppers' tooltips, as the character's level's.</summary>
    public string LowerMercLevelHint => L("Lower level") + Environment.NewLine + L("Ctrl + LMB: 5 levels")
        + Environment.NewLine + L("Shift + LMB: 20 levels") + Environment.NewLine + L("Ctrl + Shift + LMB: level 1");

    public string RaiseMercLevelHint => L("Raise level") + Environment.NewLine + L("Ctrl + LMB: 5 levels")
        + Environment.NewLine + L("Shift + LMB: 20 levels") + Environment.NewLine + L($"Ctrl + Shift + LMB: level {MercLevelMost}");

    /// <summary>Dead: the DEAD badge beside its level line, as a dead character's card wears one.</summary>
    public bool MercIsDead => HasMerc && Session.Mercenary.IsDead;

    /// <summary>
    /// Revive, after the pencil: there while it is dead and the play mode edits the character - a dead
    /// character's own rule (the home page's CanRevive): the character style of death and revive reused, except that
    /// the revive button follows the edit button's treatment.
    /// </summary>
    public bool ShowsMercRevive => EditsMerc && MercIsDead;

    [RelayCommand]
    private void ReviveMerc()
    {
        if (!EditsMerc)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        Session.ReviveMercenary();
    }

    /// <summary>
    /// Whether what a box writes is the player's edit: a mercenary is there to edit, and the page is not in
    /// the middle of reading it again. A box handed a new value answers by writing one back - its list
    /// swapped, its text brought within its bounds - and that is the page talking to itself. A save the game
    /// had just written, read again, had its level box write 1 at a character with no mercenary: refused, and
    /// the refusal took the place of "reloaded. Saved in game." on the strip (a full UI run).
    /// </summary>
    private bool TakesMercEdit => !_readingMerc && EditsMerc;

    private bool _readingMerc;

    /// <summary>
    /// The mercenary read again: its lists first, then which of each it has, so a box never shows an index of
    /// the list before. A list is only replaced when it differs - they are its type's, the same after every
    /// edit of this row - because a box whose items are swapped while it is choosing one writes the choice
    /// before back (a variant picked was set and at once unset).
    /// </summary>
    private void SayMerc()
    {
        _readingMerc = true;
        try
        {
            ReadMerc();
        }
        finally
        {
            _readingMerc = false;
        }
    }

    private void ReadMerc()
    {
        _mercVariants = Session.MercenaryVariants();
        string[] variants = [.. _mercVariants.Select(variant => variant.Label)];
        var names = Session.MercenaryNames();

        // Nothing to edit any more - another character, none hired, a mode that locks it: the popup goes.
        if (!EditsMerc)
            IsMercEditOpen = false;

        OnPropertyChanged(nameof(MercName));
        OnPropertyChanged(nameof(MercLevelLine));
        OnPropertyChanged(nameof(MercVariantName));
        OnPropertyChanged(nameof(EditsMerc));
        if (!variants.SequenceEqual(MercVariants))
        {
            MercVariants = variants;
            OnPropertyChanged(nameof(MercVariants));
        }

        if (!names.SequenceEqual(MercNames))
        {
            MercNames = names;
            OnPropertyChanged(nameof(MercNames));
        }

        OnPropertyChanged(nameof(MercVariantIndex));
        OnPropertyChanged(nameof(MercNameIndex));
        OnPropertyChanged(nameof(MercLevelMost));
        OnPropertyChanged(nameof(RaiseMercLevelHint));
        OnPropertyChanged(nameof(MercLevelText));
        OnPropertyChanged(nameof(MercLevelHint));
        OnPropertyChanged(nameof(CanLowerMercLevel));
        OnPropertyChanged(nameof(CanRaiseMercLevel));
        OnPropertyChanged(nameof(MercIsDead));
        OnPropertyChanged(nameof(ShowsMercRevive));
    }
}
