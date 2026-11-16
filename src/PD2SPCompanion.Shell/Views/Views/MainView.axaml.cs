using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class MainView : UserControl
{
    #region Fields and Members
    /// <summary>
    /// Where the carried item's ghost sits relative to the pointer. Half a cell up and left, so
    /// the pointer is on the item rather than at its corner - which is where the cell being
    /// pointed at feels like it should be.
    /// </summary>
    private const double GhostOffset = -14;
    #endregion

    #region Constructor
    public MainView()
    {
        InitializeComponent();

        // The ghost follows the pointer over the whole window, both pages and the spine between.
        // Tunnelled and handled-too, so nothing under the
        // pointer can keep it from moving.
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);

        // New is not select (MainViewModel.SettleForge): with the Forge up, what a click or a key selected on
        // the left takes the right page to Edit. Asked once the release has been dealt with - a drop made, a
        // click's command run - and told whether a carry was under way when it came, which is known only now.
        AddHandler(PointerReleasedEvent, (_, _) => SettleForgeSoon(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, _) => SettleForgeSoon(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // ---- the window's search: the sections and the settings there are to find --------------------------------

    /// <summary>The words a page's kept name begins with, and what the side bar calls the page.</summary>
    private static string? PageName(string key) => key.Split('_')[0] switch
    {
        "Char" => L("Character"),
        "Inventory" => key.EndsWith("_L", StringComparison.Ordinal) ? L("Inventory") : L("Edit"),
        "Extra" => L("Extra"),
        "Settings" => L("Settings"),
        "Experimental" => L("Experimental"),
        _ => null,
    };

    /// <summary>
    /// The settings and sections the window's search finds (<see cref="QuickSearchViewModel.Targets"/>), read off
    /// the pages built so far rather than listed by hand, so a fold or a setting added to a page is found the
    /// day it is added: a fold's heading, a page's title and headings are sections; on the Settings page every
    /// row's label is a setting. What a play mode hides is not there to be read and is left out.
    /// </summary>
    private IReadOnlyList<SearchTarget> SearchTargets()
    {
        if (DataContext is not MainViewModel main)
            return [];

        var targets = new List<SearchTarget>(main.PageTargets());
        var seen = new HashSet<(string, string)>(targets.Select(target => (target.Where, target.Name)));

        foreach (var (content, view) in ViewPage_Left.Kept.Concat(ViewPage_Right.Kept))
        {
            if (main.PageKeyOf(content) is not { } key || PageName(key) is not { } page)
                continue;
            if (key.Split('_')[0] is "Char" or "Inventory" && !main.Session.IsLoaded)
                continue;

            bool settings = key.StartsWith("Settings", StringComparison.Ordinal);

            foreach (var element in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(view).OfType<Control>())
            {
                string? name = null;
                bool isSetting = false;

                if (element is Button fold && fold.Classes.Contains("fold"))
                    name = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(fold).OfType<TextBlock>()
                        .Select(text => text.Text).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                else if (element is TextBlock heading && (heading.Classes.Contains("heading") || heading.Classes.Contains("title")))
                    name = heading.Text;
                else if (settings && element is TextBlock label && (label.Classes.Count == 0 || label.Classes.Contains("body"))
                         && !Within<Button>(label, view) && !Within<ItemsControl>(label, view) && !Within<TextBox>(label, view)
                         && !Within<ComboBox>(label, view))
                {
                    name = label.Text;
                    isSetting = true;
                }

                // A heading says how many it holds after its name - "Mercenary  (6)": the name is what is found.
                name = name is null ? null : System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+\(\d+(\s*/\s*\d+)?\)$", "");
                if (name is not { Length: >= 2 and <= 48 } || name.Contains('\\') || name.Contains('/') || !InSight(element, view)
                    || !seen.Add((page, name)))
                {
                    continue;
                }

                var at = element;
                targets.Add(new SearchTarget(isSetting, name, page, () => GoToTarget(main, key, at)));
            }
        }

        return targets;
    }

    private static bool Within<T>(Control element, Control page) where T : Control
    {
        for (var each = element.Parent; each is not null && !ReferenceEquals(each, page); each = each.Parent)
        {
            if (each is T)
                return true;
        }

        return false;
    }

    /// <summary>Shown as far as its own page goes: nothing between it and the page hides it (the page itself may be behind another).</summary>
    private static bool InSight(Control element, Control page)
    {
        for (Avalonia.StyledElement? each = element; each is not null && !ReferenceEquals(each, page); each = each.Parent)
        {
            if (each is Control { IsVisible: false })
                return false;
        }

        return true;
    }

    /// <summary>A setting or a section gone to: its page shown, a fold of its own unfolded, itself brought into sight.</summary>
    private static void GoToTarget(MainViewModel main, string key, Control element)
    {
        if (!main.ShowPageByKey(key))
        {
            main.Session.Status = L("Require open character.");
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (element is Button fold
                && Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(fold).OfType<Avalonia.Controls.Shapes.Path>()
                       .FirstOrDefault(path => path.Classes.Contains("chevron")) is { } chevron
                && !chevron.Classes.Contains("open")
                && fold.Command is { } unfold && unfold.CanExecute(fold.CommandParameter))
            {
                unfold.Execute(fold.CommandParameter);
            }

            element.BringIntoView();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void SettleForgeSoon()
    {
        if (DataContext is not MainViewModel { IsRightForge: true } main)
            return;

        bool carried = main.LeftIsCarrying;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => main.SettleForge(carried));
    }

    private TopLevel? _window;

    /// <summary>
    /// Undo and redo in the session's history, from anywhere: listened for on the window, since
    /// keys go where the focus is and the page may have nothing focused. A box being typed in
    /// keeps its own.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        _window?.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        if (DataContext is MainViewModel { QuickSearch: { } search })
            search.Targets = SearchTargets;

        // Alt and Ctrl alone, with an item's comparison in sight (TipActions): the next slot, the mercenary's.
        // Once a press - a held key repeats - and from every key down and up, handled or not.
        _window?.AddHandler(KeyDownEvent, OnComparisonKeys, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window?.AddHandler(KeyUpEvent, (_, up) =>
        {
            if (up.Key == Key.Tab)
                _tabHeld = false;
            if (up.Key is Key.LeftAlt or Key.RightAlt)
                _altHeld = false;
            if (up.Key is Key.LeftCtrl or Key.RightCtrl)
                _ctrlHeld = false;
            if (up.Key is Key.LeftShift or Key.RightShift)
            {
                // Tapped alone: the socket view turns over (SocketView.Toggle).
                if (_shiftAlone)
                    PD2SPCompanion.Shell.Services.Generic.SocketView.Toggle();
                _shiftAlone = _shiftHeld = false;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // A click while Shift is down makes it a chord - Shift + click - not a tap.
        _window?.AddHandler(PointerPressedEvent, (_, _) => _shiftAlone = false, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The window left with Shift down never hears it let go: no tap.
        if (_window is Window window)
            window.Deactivated += (_, _) => _shiftAlone = _shiftHeld = false;
        _window?.AddHandler(KeyDownEvent, OnEscape, RoutingStrategies.Bubble);
        if (_window is not null)
        {
            PD2SPCompanion.Shell.CustomControls.Base.PickerKeys.Attach(_window, this);
            PD2SPCompanion.Shell.CustomControls.Base.ListKeys.Attach(_window);
        }
        if (DataContext is MainViewModel { Home: { } home } && !ReferenceEquals(home, _opening))
        {
            if (_opening is not null)
                _opening.Opened -= PrepareCharacterPages;
            _opening = home;
            home.Opened += PrepareCharacterPages;
        }

        // Extra, Settings and Experimental need no character, so they are laid out
        // once the window is up and idle - the game tables were opened behind the splash already.
        PrepareOtherPagesSoon(0);
    }

    /// <summary>
    /// Alt or Ctrl pressed with an item's comparison in sight: Alt shows the next slot that takes the item, Ctrl
    /// the mercenary's and back - and a right click then puts it where the comparison
    /// shows. Once a press, however long the key is held. With no comparison in sight they are the click's
    /// chords, as the strip lists them, and nothing is done here.
    /// </summary>
    private void OnComparisonKeys(object? sender, KeyEventArgs e)
    {
        // Shift tapped alone turns a socketed item's reading over - its sockets apart, or summed - everywhere its
        // lines are shown, and it stays so (SocketView: a toggle kept for the session, as holding Shift felt like extra
        // effort). A tap: down and up with no other key or click between,
        // so Shift + click and Shift + F3 leave it. Not while something is typed - a capital is no question about
        // an item - and not as part of a chord begun with Ctrl or Alt. Once a press: a held key repeats.
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            if (!_shiftHeld)
            {
                _shiftHeld = true;
                _shiftAlone = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0
                              && _window?.FocusManager?.GetFocusedElement() is not TextBox;
            }

            return;
        }

        // Any other key while Shift is down: a chord.
        _shiftAlone = false;

        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            if (!_altHeld)
                PD2SPCompanion.Shell.Services.Generic.TipActions.Cycle();
            _altHeld = true;
        }
        else if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            if (!_ctrlHeld)
                PD2SPCompanion.Shell.Services.Generic.TipActions.SwitchWhose();
            _ctrlHeld = true;
        }
    }

    private bool _altHeld, _ctrlHeld;

    /// <summary>Shift is down; and nothing else has been pressed or clicked since - a tap so far.</summary>
    private bool _shiftHeld, _shiftAlone;

    /// <summary>Tab is down from a press that turned the comparison over: its repeats do nothing more.</summary>
    private bool _tabHeld;

    private VP_ViewModel_Default_L? _opening;

    /// <summary>
    /// The Character and Items pages laid out for the character just opened, hidden in their decks - their
    /// first visit was 190-270 ms of building them around the character's content (a wait on the first load is much
    /// more tolerable than one later between pages).
    /// <para>
    /// <b>A page each time the window is idle, from the moment the character is open</b> - not inside the open:
    /// laid out there they were 120 - 280 ms of
    /// an open whose own work is some 100 (a trace), and the window stood still for all of it. The pages'
    /// fixed parts are built behind the splash now (PrepareAllPages), so what is left is the character's
    /// content, a slice a page. A page gone to before its slice is laid out as it is shown, as any page is;
    /// another character opened meanwhile begins the round again.
    /// </para>
    /// </summary>
    private void PrepareCharacterPages()
    {
        if (DataContext is not MainViewModel main)
            return;

        int round = ++_characterRound;
        var pages = new System.Collections.Generic.List<(PD2SPCompanion.Shell.CustomControls.Base.PageDeck Deck, object Page)>();
        foreach (var page in main.CharacterPagesLeft)
            pages.Add((ViewPage_Left, page));
        foreach (var page in main.CharacterPagesRight)
            pages.Add((ViewPage_Right, page));
        // The Items page's pair before the Character page's: a find gone to shows it at once.
        pages.Sort((a, b) => (a.Page is VP_ViewModel_Char_L or VP_ViewModel_Char_R).CompareTo(b.Page is VP_ViewModel_Char_L or VP_ViewModel_Char_R));

        // Begun once the home page has faded from the list to the character: begun at once, its steps held the
        // fade's frames - a session's first, filmed, stood at some 60% for 320 ms and then was done.
        Avalonia.Threading.DispatcherTimer.RunOnce(() => PrepareCharacterPage(main, pages, 0, round), HomeFade);
    }

    /// <summary>The home page's cross-fade, as long as its state style says (VP_View_Default_L, <c>.state</c>), and a frame.</summary>
    private static readonly System.TimeSpan HomeFade = System.TimeSpan.FromMilliseconds(220 + 30);

    private int _characterRound;

    /// <param name="sheetSettled">The Items page's sheets worked out already, in a step of their own before its layout.</param>
    private void PrepareCharacterPage(MainViewModel main, System.Collections.Generic.List<(PD2SPCompanion.Shell.CustomControls.Base.PageDeck Deck, object Page)> pages, int next, int round,
                                      bool sheetSettled = false) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (round != _characterRound)
                return;
            if (next >= pages.Count)
            {
                AfterCharacterPages();
                return;
            }

            try
            {
                if (pages[next].Page is VP_ViewModel_Inv_L items && !sheetSettled)
                {
                    items.SettleSheet();
                    PrepareCharacterPage(main, pages, next, round, sheetSettled: true);
                    return;
                }

                if (pages[next].Page is VP_ViewModel_Char_L or VP_ViewModel_Char_R)
                    main.SettleCharacterPages();
                pages[next].Deck.Prebuild([pages[next].Page]);
            }
            catch (System.Exception problem)
            {
                // What did not lay out here lays out on its first visit, as it did before.
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            }

            PrepareCharacterPage(main, pages, next + 1, round);
        }, Avalonia.Threading.DispatcherPriority.Background);

    private void AfterCharacterPages()
    {
        // The character's search index, a slice of its own: the first word typed only asks.
        int round = _characterRound;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (round == _characterRound)
                (DataContext as MainViewModel)?.PrepareSearch();
        }, Avalonia.Threading.DispatcherPriority.Background);

        // Start-up lays the other pages out (OnAttachedToVisualTree); an open does it only if start-up could
        // not. Extra is the one whose content an open changes - the Rune Wizard counts the character's
        // stash - so it is worked out and laid out again, at idle, or its first visit rebuilt its rows
        // (52 ms, measured).
        if (!_otherPagesStarted)
            PrepareOtherPagesSoon(0);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshExtra, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void RefreshExtra()
    {
        if (DataContext is not MainViewModel main || ViewPage_Left.Bounds.Width <= 0 || ViewPage_Right.Bounds.Width <= 0)
            return;
        try
        {
            // Both halves: the wizard's rows are on the left page, its view model the right's.
            var (left, right) = main.OtherPages[0];
            main.SettleOtherPage(right);
            ViewPage_Left.Prebuild([left]);
            ViewPage_Right.Prebuild([right]);
        }
        catch (System.Exception problem)
        {
            // Not laid out ahead: its first visit after the open works it out, as before.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    private bool _otherPagesStarted;

    /// <summary>
    /// Every page laid out, hidden, before the window is in sight (App: behind the splash, the window cloaked). A pair at
    /// a time, the splash drawn
    /// between two. It needs the pages kept (PageDeck: else one built ahead is built again on its visit), and a
    /// grid as one control (else a character's content is hundreds of
    /// controls no page could have before a character). So the first
    /// open of a session pays only for the character, as every later open does, and no page's first press
    /// builds it.
    /// </summary>
    public async System.Threading.Tasks.Task PrepareAllPages()
    {
        if (DataContext is not MainViewModel main)
            return;

        // The idle chain (PrepareOtherPages) is this one's fallback, and not to run beside it.
        _otherPagesStarted = true;

        for (int tries = 0; tries < 200 && (ViewPage_Left.Bounds.Width <= 0 || ViewPage_Right.Bounds.Width <= 0); tries++)
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
        if (ViewPage_Left.Bounds.Width <= 0 || ViewPage_Right.Bounds.Width <= 0)
        {
            _otherPagesStarted = false;
            PrepareOtherPagesSoon(0);
            return;
        }

        long before = System.GC.GetTotalAllocatedBytes();
        try
        {
            main.WarmPages();
        }
        catch (System.Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }

        PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"  the Forge's lists: allocated {(System.GC.GetTotalAllocatedBytes() - before) / (1024 * 1024)} MB");

        // A pair, then the window's own messages - not the idle queue: start-up's idle work (the patch guard, the
        // cards' figures) is not to be waited for between two pairs.
        foreach (var (left, right) in main.PagesAhead)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var paused = System.GC.GetTotalPauseDuration();
            long settled = 0, leftDone = 0, allocated = System.GC.GetTotalAllocatedBytes();
            try
            {
                if (right is not null)
                    main.SettleOtherPage(right);
                settled = timer.ElapsedMilliseconds;
                if (left is not null)
                    ViewPage_Left.Prebuild([left]);
                leftDone = timer.ElapsedMilliseconds;
                if (right is not null)
                {
                    ViewPage_Right.Prebuild([right]);
                    (ViewPage_Right.Built(right) as VP_View_Vault_R)?.PrebuildStates();
                }
            }
            catch (System.Exception problem)
            {
                // Not laid out ahead: it lays out on its first visit.
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            }

            // Each pair's time into the log, beside the start-up steps': what a page costs to lay out is known.
            string leftName = left?.GetType().Name ?? "-", rightName = right?.GetType().Name ?? "-";
            // And what of it was the page's content worked out, the left page, the right - and the collector's stops.
            long whole = timer.ElapsedMilliseconds, made = (System.GC.GetTotalAllocatedBytes() - allocated) / (1024 * 1024);
            double stops = (System.GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
            PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"  pages {leftName} / {rightName}: {whole} ms (content {settled}, left {leftDone - settled}, right {whole - leftDone}; allocated {made} MB; collector {stops:0})");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void PrepareOtherPagesSoon(int next, int tries = 0) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PrepareOtherPages(next, tries), Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>
    /// Extra, Settings and Experimental laid out ahead, one pair each time the editor is idle after it
    /// starts (MainViewModel.OtherPages): kept from then on, as every page is, so their first visits are as
    /// quick as Items' - with a character or without. A click meanwhile waits at most for the one pair
    /// being laid out. Before the window has a size there is nothing to lay out in: asked again, a while.
    /// </summary>
    private void PrepareOtherPages(int next, int tries)
    {
        if (DataContext is not MainViewModel main || next >= main.OtherPages.Count)
            return;
        if (ViewPage_Left.Bounds.Width <= 0 || ViewPage_Right.Bounds.Width <= 0)
        {
            if (tries < 100)
                PrepareOtherPagesSoon(next, tries + 1);
            return;
        }
        if (next == 0)
        {
            if (_otherPagesStarted)
                return;
            _otherPagesStarted = true;
        }
        try
        {
            var (left, right) = main.OtherPages[next];
            main.SettleOtherPage(right);
            ViewPage_Left.Prebuild([left]);
            ViewPage_Right.Prebuild([right]);
        }
        catch (System.Exception problem)
        {
            // Not laid out ahead: it lays out on its first visit, as it did before.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
        PrepareOtherPagesSoon(next + 1);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.RemoveHandler(KeyDownEvent, OnKey);
        _window?.RemoveHandler(KeyDownEvent, OnEscape);
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        // A question up owns the keyboard, as a Windows dialog does: Enter
        // answers yes, Esc no, and no key reaches the page behind it - Delete and Ctrl+Z did, and Enter after
        // Delete on the Characters list opened the character the question was about.
        if (DataContext is MainViewModel { Confirm: { IsAsking: true } question })
        {
            // But for a question with two answers and no "no" (NeedsClick), which a key does not answer.
            if (question.NeedsClick)
            {
            }
            else if (e.Key is Key.Enter or Key.Return && question.AcceptCommand.CanExecute(null))
                question.AcceptCommand.Execute(null);
            else if (e.Key == Key.Escape && question.RejectCommand.CanExecute(null))
                question.RejectCommand.Execute(null);

            // Tab walks the question's buttons; nothing else gets past.
            PD2SPCompanion.Shell.CustomControls.Base.NavigationKeys.Tab(this, e);
            e.Handled = true;
            return;
        }

        // Over an item's tooltip cut to the screen, [ and ] scroll it (TipActions) - every repeat of a held key,
        // and not while something is being typed.
        if (e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets && e.KeyModifiers == KeyModifiers.None
            && e.Source is not TextBox
            && PD2SPCompanion.Shell.Services.Generic.TipActions.Scroll(e.Key == Key.OemOpenBrackets ? -1 : 1))
        {
            e.Handled = true;
            return;
        }

        // Over an item's tooltip that offers it, Tab turns the comparison on and off (TipActions) - once a press,
        // however long it is held: a held key repeats, and each repeat turned it over again, the box flickering.
        // The repeats of that press are swallowed, so they walk nothing either.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            if (_tabHeld)
            {
                e.Handled = true;
                return;
            }

            if (PD2SPCompanion.Shell.Services.Generic.TipActions.CanCompare)
            {
                _tabHeld = true;
                PD2SPCompanion.Shell.Services.Generic.TipActions.Toggle();
                e.Handled = true;
                return;
            }
        }

        // The window's search (QuickSearchViewModel), from anywhere - a text box too: Ctrl + F puts the pointer in
        // its box.
        if (DataContext is MainViewModel { QuickSearch: { } quick } && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            quick.Focus();
            e.Handled = true;
            return;
        }

        // F3 and Shift + F3 are the Search page's while it is up: the next of its finds
        // gone to, and the one before. They were the search box's, whose finds are gone with its list now.
        if (e.Key == Key.F3 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift
            && DataContext is MainViewModel { IsRightSearch: true, Button_Inventory_isChecked: true, SearchWindow: { } page })
        {
            page.Step(e.KeyModifiers == KeyModifiers.Shift ? -1 : +1);
            e.Handled = true;
            return;
        }

        // F1: the sheet of keys and gestures, shown or put away - from anywhere, a text box too.
        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            PD2SPCompanion.Shell.Services.Settings.KeySheetService.Shared.Toggle();
            e.Handled = true;
            return;
        }

        // Esc with the sheet of keys or What's new up closes that and nothing else: each is over everything,
        // so it has the key before a page or the search does. (Both are veils, so no page takes a key under
        // them: without that mark the Items page had Esc first, let its selection go, and the sheet stayed.)
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (PD2SPCompanion.Shell.Services.Settings.KeySheetService.Shared.IsOpen)
            {
                PD2SPCompanion.Shell.Services.Settings.KeySheetService.Shared.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (DataContext is MainViewModel { WhatsNew: { IsOpen: true } notes })
            {
                notes.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // Tab walks the window's three and the side bar, and no other control (NavigationKeys.Tab).
        if (PD2SPCompanion.Shell.CustomControls.Base.NavigationKeys.Tab(this, e))
        {
            e.Handled = true;
            return;
        }


        // The window's own: pages, bookmarks, search (NavigationKeys) - not from inside a text box, whose Ctrl+F or
        // Ctrl+Tab are its own. A number's box has no use for them, and keeps the keyboard after a number is typed: from
        // there they did nothing until a click elsewhere.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox or PD2SPCompanion.Shell.CustomControls.Base.NumericTextBox
            && PD2SPCompanion.Shell.CustomControls.Base.NavigationKeys.Handle(this, e))
        {
            e.Handled = true;
            return;
        }

        // Not under a picker either: undo there reached behind it, past what the picker had staged.
        if (DataContext is not MainViewModel { Home: { } home } || e.KeyModifiers != KeyModifiers.Control
            || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox
            || PD2SPCompanion.Shell.CustomControls.Base.ItemPanels.IsVeiled(this))
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Z => home.UndoCommand,
            Key.Y => home.RedoCommand,
            _ => null,
        };

        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
    /// <summary>
    /// Esc closes the picker or overlay on top (ItemPanels.CloseTopVeil). Bubbled, so a box that takes Esc
    /// itself - a rename's cancel - has it first.
    /// </summary>
    private void OnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            return;

        // A veil - a picker, the search's conditions' own among them - is closed by it.
        if (PD2SPCompanion.Shell.CustomControls.Base.ItemPanels.CloseTopVeil(this))
        {
            e.Handled = true;
            return;
        }

        // And what stands over a page without a veil, as its cross or its way back does (every
        // overlay closes the same ways): a jewel opened from its item's sockets, back to the item; a backup shown on the
        // home page, closed.
        if (DataContext is not MainViewModel main)
            return;

        if (main.EditLayerInSight is { } edit && edit.CloseNestedCommand.CanExecute(null))
        {
            edit.CloseNestedCommand.Execute(null);
            e.Handled = true;
        }
        else if (main.Home.Session.IsViewingBackup && IsHomeShown(main) && main.Home.CloseBackupCommand.CanExecute(null))
        {
            main.Home.CloseBackupCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The home page in front: no page of the side bar chosen.</summary>
    private static bool IsHomeShown(MainViewModel main) =>
        !main.Button_Char_isChecked && !main.Button_Inventory_isChecked && !main.Button_Extra_isChecked
        && !main.Button_Experimental_isChecked && !main.Button_Settings_isChecked;
    #endregion

    #region Events
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel main
            || !(main.Inventory?.Session.IsHolding == true || main.IsVaultCarrying
                 || main.Inventory?.IsTabCarrying == true || main.SocketCarry.IsCarrying))
        {
            return;
        }

        // A carry stays in the book - both pages and the spine between them.
        // Out of it, on the sidebar or the frame, what was carried goes back, as leaving the
        // inventory page once put it back: nothing moved, so there is nothing to undo.
        if (!InBook(e.GetPosition(this)))
        {
            if (main.Inventory?.Session.IsHolding == true)
                main.Inventory.CancelHold();
            main.Inventory?.EndTabCarry();
            main.Vault?.EndCarry();
            main.RightVault?.EndCarry();
            main.SocketCarry.Cancel?.Invoke();
            return;
        }

        var at = e.GetPosition(GhostLayer);
        main.GhostLeft = at.X + GhostOffset;
        main.GhostTop = at.Y + GhostOffset;
    }

    /// <summary>Whether a point of this view lies on the book: the left page's corner to the right page's.</summary>
    private bool InBook(Point at)
    {
        if (ViewPage_Left.TranslatePoint(default, this) is not { } from
            || ViewPage_Right.TranslatePoint(new Point(ViewPage_Right.Bounds.Width, ViewPage_Right.Bounds.Height), this) is not { } to)
        {
            return true;
        }

        return new Rect(from, to).Contains(at);
    }
    #endregion
}
