using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Generic;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

// The character sheet under the worn gear: in the inventory bookmarks, one under equipped and one under the
// mercenary, each a separate foldable section. Phase 1: the
// character's, as the game's own Advanced Stats page shows it. Phase 2: the buff row above it - what is up for good,
// and every cast pressed on and off, all of it on to begin with ("the maximized route"). Always out of town, where the
// mercenary's and a spirit's auras reach the character: no out-of-town switch, as a player generally cares about
// combat. Phase 3: the two skill boxes over the buff row, the save's own left and
// right skill - the breakpoints and a Paladin's aura follow them.
public partial class VP_ViewModel_Inv_L
{
    /// <summary>
    /// Folded to begin with: a page and more of numbers, open under the gear, put every container a page further
    /// down for whoever had not asked for it. Its fold is remembered as every section's is.
    /// </summary>
    [ObservableProperty]
    private bool _sheetIsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(SheetIsOpen), initially: false);

    partial void OnSheetIsOpenChanged(bool value)
    {
        PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(SheetIsOpen), value);
        RefreshSheet();
    }

    /// <summary>The mercenary's sheet, in its own section under Mercenary: folded to begin with, as the character's.</summary>
    [ObservableProperty]
    private bool _mercSheetIsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(MercSheetIsOpen), initially: false);

    partial void OnMercSheetIsOpenChanged(bool value)
    {
        PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(MercSheetIsOpen), value);
        RefreshSheet();
    }

    [ObservableProperty]
    private CharacterSheetViewModel? _mercSheet;

    [RelayCommand]
    private void ToggleMercSheet()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        MercSheetIsOpen = !MercSheetIsOpen;
    }

    /// <summary>The sheet's lines; worked out only while the section is open, and again at every change to the character.</summary>
    [ObservableProperty]
    private CharacterSheetViewModel? _sheet;

    /// <summary>What was pressed in the buff rows, kept by the session for the open character; the rest stands as it begins.</summary>
    private Dictionary<string, bool> PressedHere => Session.BuffPresses;

    /// <summary>
    /// A buff pressed, in either sheet's row - the mercenary's are the character's own buffs that reach it: off
    /// where it is on, on where it is off - and on, the others of its lot let go (one form, one aura of his own,
    /// one spirit, one cold armor at a time).
    /// </summary>
    [RelayCommand]
    private void ToggleBuff(BuffChip chip)
    {
        var row = Sheet?.Buffs.Contains(chip) == true ? Sheet : MercSheet;
        if (!chip.Toggles || row is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        var pressed = PressedHere;
        bool on = !chip.Buff.On;
        pressed[chip.Buff.Key] = on;
        if (on && chip.Buff.Rivals.Length > 0)
        {
            // Of the others those up stay as many as the lot has room for beside it - a second spirit with Spiritkeeper.
            int kept = 0;
            foreach (var rival in row.Buffs.Select(other => other.Buff)
                         .Where(other => other.Rivals == chip.Buff.Rivals && other.Key != chip.Buff.Key && other.Toggles)
                         .OrderByDescending(other => other.On).ThenByDescending(other => other.Level))
                pressed[rival.Key] = rival.On && ++kept < chip.Buff.Room;
        }

        RefreshSheet();
    }

    /// <summary>The weapon set swapped from the sheet's header: the same swap as Equipped's own tabs.</summary>
    [RelayCommand]
    private void SwapSheetSet() => ShowSet(IsSetI ? 2 : 1);

    [RelayCommand]
    private void ToggleSheet()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SheetIsOpen = !SheetIsOpen;
    }

    /// <summary>
    /// The sheets worked out again - the character changed, a section opened - while the page is seen; asked behind
    /// another page, once when it is next shown (an edit on the Character page or in the vault made them again for
    /// nobody).
    /// </summary>
    private void RefreshSheet() => SheetWork.Ask();

    private WhenShown? _sheetWork;

    /// <summary>
    /// An edit of the character, anywhere - the Edit page's KEEP, a charm moved into the inventory, a socket filled -
    /// asked once for a burst, after it has drawn (the sheet seemed to lag or miss updates - it was asked only when
    /// the attributes or the belt changed). What the sheets read unchanged, they
    /// are left as they were (RefreshSheetNow's key).
    /// </summary>
    private void SheetAfterEdit()
    {
        if (_sheetPosted)
            return;

        _sheetPosted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _sheetPosted = false;
            RefreshSheet();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private bool _sheetPosted;

    private WhenShown SheetWork => _sheetWork ??= new WhenShown(() => RefreshSheetNow());

    /// <summary>
    /// Laid out ahead of a visit (MainView.PrepareCharacterPages, after a character opens): what the sheets owe,
    /// done first and whole, so the page laid out behind is the one that will show (it stung at the first load - a session's first sheet, its code made and its icons read, 175 ms of
    /// the page's first show while the sections were unfolded). The wait goes into the open, where it is expected -
    /// a step of its own, the mercenary's after it, the layout after that (MainView): in one step they were a stop of
    /// 480 ms after the open.
    /// </summary>
    public void SettleSheet()
    {
        SheetWork.Settle();
        // The tiles' marks too, before the layout ahead (JudgeAll).
        JudgeWork.Settle();
    }

    /// <summary>
    /// The page in front (VP_View_Inv_L.OnShown): what the sheets owe is done once its first frame is drawn - the
    /// page's fade under way - and not before it. Done before it, the fade waited on the sheets: a character opened
    /// with the sections unfolded, the Items page came 20-50 ms late, a noticeable lag. Done behind the page instead it cost more - a change inside a
    /// hidden page is laid out whole when it is shown (PageDeck.Reclaim).
    /// </summary>
    public void SheetShown()
    {
        _pageShown = true;
        // The tiles' marks, before the page's first frame (JudgeAll).
        JudgeWork.Shown();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_pageShown)
                SheetWork.Shown();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    public void SheetHidden()
    {
        _pageShown = false;
        SheetWork.Hidden();
        JudgeWork.Hidden();
    }

    private bool _pageShown;

    /// <summary>
    /// Each open section's sheet merged into the one it shows (<see cref="CharacterSheetViewModel.Update"/>): the
    /// character's worked out once, and the mercenary's from it. A folded section is left as it was, and caught up
    /// when it is opened.
    /// </summary>
    private void RefreshSheetNow()
    {
        if (!Session.IsLoaded)
        {
            Sheet = null;
            MercSheet = null;
            return;
        }

        var options = new CharacterSheet.Options(OutOfTown: true, Pressed: PressedHere);
        bool mercenaryShown = MercSheetIsOpen && HasMerc;

        // Nothing the sheets read changed - a move in the stash or the cube, a store - and both are left as they are
        // (CharacterSheet.InputsOf). Asked with 0 - no character, or the save would not write - it is worked out.
        int inputs = Session.SheetInputs();
        var key = new System.HashCode();
        key.Add(inputs);
        key.Add(Session.CharacterName);
        key.Add(SheetIsOpen);
        key.Add(mercenaryShown);
        key.Add(PD2SPCompanion.Core.GameFiles.InstalledTables.Generation);
        foreach (var (buff, on) in System.Linq.Enumerable.OrderBy(PressedHere, pair => pair.Key, System.StringComparer.Ordinal))
        {
            key.Add(buff);
            key.Add(on);
        }
        int asKept = key.ToHashCode();
        if (asKept == _sheetKey && inputs != 0 && (!SheetIsOpen || Sheet is not null) && (!mercenaryShown || MercSheet is not null))
            return;
        _sheetKey = asKept;

        var character = SheetIsOpen || mercenaryShown ? Session.Sheet(options) : null;

        if (SheetIsOpen)
        {
            if (character is null)
                Sheet = null;
            else
                (Sheet ??= new CharacterSheetViewModel(ChooseSkill)).Update(character);
        }

        // The mercenary's: what reaches it of the party is what the character's buff row has on. Laid out apart from
        // the character's, after it - two short stops in place of one long one while the page fades in.
        if (!HasMerc)
            MercSheet = null;
        else if (mercenaryShown)
        {
            int asked = ++_mercenaryAsked;
            void Mercenary()
            {
                if (asked != _mercenaryAsked || !Session.IsLoaded || !MercSheetIsOpen || !HasMerc)
                    return;
                if (Session.MercenarySheet(options, character) is { } mercenary)
                    (MercSheet ??= new CharacterSheetViewModel()).Update(mercenary);
                else
                    MercSheet = null;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(Mercenary, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>Which mercenary sheet asked for is the latest: an older one, overtaken before its turn, is let go.</summary>
    private int _mercenaryAsked;

    /// <summary>What the sheets were last worked out from (RefreshSheetNow).</summary>
    private int _sheetKey;

    /// <summary>
    /// A skill chosen in one of the two boxes: written to the save, as the game keeps it. After
    /// the box's own change is done - the sheet it sits in is made again by it.
    /// </summary>
    private void ChooseSkill(bool right, int skillId) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Session.IsLoaded)
                Session.SetSkillBox(right, skillId);
        });
}
