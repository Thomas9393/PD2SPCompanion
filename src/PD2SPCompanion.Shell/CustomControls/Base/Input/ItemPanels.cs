using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The item panels on screen - the inventory and the vault list - now that the two can share it:
/// which one the window's keys go to, and whether
/// another is showing to take something carried out of this one.
/// <para>
/// Each listens for its keys on the window, so with both in sight Ctrl+A, Esc and Delete would act
/// on both at once. The keys are the panel's that was last pressed in; with only one in sight they
/// are that one's.
/// </para>
/// <para>
/// A panel registers itself when it is attached, <b>before</b> adding its own window handlers: the
/// press that claims the keys is then seen first, so a panel's own press handler already knows
/// whether the keys are its.
/// </para>
/// </summary>
public static class ItemPanels
{
    private static readonly List<WeakReference<Control>> Panels = [];
    private static readonly HashSet<TopLevel> Watched = [];
    private static WeakReference<Control>? _owner;

    /// <summary>
    /// Whether a point of a control lies inside every scroller it is in. A control scrolled under the page's header lies
    /// under the pointer by its own bounds, and neither its size nor IsEffectivelyVisible knows of the clip.
    /// </summary>
    public static bool InSight(Visual control, Point at)
    {
        foreach (var scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (control.TranslatePoint(at, scroller) is not { } within || !new Rect(scroller.Bounds.Size).Contains(within))
                return false;
        }

        return true;
    }

    /// <summary>Counts <paramref name="panel"/> among the panels that can hold the keys.</summary>
    public static void Register(Control panel)
    {
        Panels.RemoveAll(each => !each.TryGetTarget(out var alive) || ReferenceEquals(alive, panel));
        Panels.Add(new WeakReference<Control>(panel));

        if (TopLevel.GetTopLevel(panel) is { } window && Watched.Add(window))
            window.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Leaves <paramref name="panel"/> out once it is off the screen.</summary>
    public static void Unregister(Control panel) =>
        Panels.RemoveAll(each => !each.TryGetTarget(out var alive) || ReferenceEquals(alive, panel));

    /// <summary>
    /// Whether the keys are <paramref name="panel"/>'s: nothing else in sight was pressed in last.
    /// A press outside every panel - the item editor, the search - leaves the keys where they were.
    /// </summary>
    public static bool OwnsKeys(Control panel) =>
        _owner is null
        || !_owner.TryGetTarget(out var owner)
        || ReferenceEquals(owner, panel)
        || !owner.IsEffectivelyVisible;

    /// <summary>
    /// The keyboard let go of by the part of the window that holds it - the search box, the Search page - to
    /// <paramref name="to"/>, focused as a pointer focuses (no outline), so the pages' keys reach them at once.
    /// Only while the keyboard is still in <paramref name="from"/>, and that is asked of the focus manager:
    /// IsKeyboardFocusWithin follows a focus change a moment late. Read 3 ms after the Search page's keyword
    /// field had taken the caret, it still said the search box held the keyboard, and the caret was taken from
    /// the field again (the UI cases' search.ctrl-enter, which passed only in a program of its own).
    /// </summary>
    public static void LetKeysGo(Visual from, InputElement to)
    {
        if (TopLevel.GetTopLevel(from)?.FocusManager?.GetFocusedElement() is not Visual held
            || !ReferenceEquals(held, from) && !from.IsVisualAncestorOf(held))
        {
            return;
        }

        to.Focusable = true;
        to.IsTabStop = false;
        to.Focus(NavigationMethod.Pointer);
    }

    /// <summary>
    /// A press anywhere but a box or a picker takes the keys back from the box or picker that holds them. Tiles
    /// and cards take no focus, so a box typed in or a picker chosen from kept it: the item clicked next was
    /// selected, and Delete, Escape and Ctrl+A went to the box - a letter of the vault's search taken back instead
    /// of the card deleted. Written for a report that Delete did not work on the stash,
    /// which a test pad did not reproduce once a stuck Ctrl was let go. Called from a page's tunnelling press.
    /// </summary>
    public static void TakeKeysBack(Visual page, PointerPressedEventArgs e)
    {
        // To the page itself, made focusable for it and focused as a pointer focuses (no outline): this Avalonia's
        // focus manager has no ClearFocus, and the window would not take the focus (measured).
        if (e.Source is Visual pressed && !IsBoxOrPicker(pressed)
            && page is InputElement taker
            && IsBoxOrPicker(TopLevel.GetTopLevel(page)?.FocusManager?.GetFocusedElement() as Visual))
        {
            taker.Focusable = true;
            taker.Focus(NavigationMethod.Pointer);
        }
    }

    /// <summary>
    /// Whether a press or the keys at <paramref name="at"/> are a box's or a picker's, not the
    /// panel's: a text box, a picker, or anything in a popup. A picker's open list is a popup, a
    /// visual tree of its own, so its entries have no picker above them; its press still reaches
    /// the panel (a popup routes through the control that opened it), and choosing a stash page
    /// read as a click on empty space - the selection went, and an edit staged on it.
    /// <para>
    /// Or anything in a panel that keeps the keys while the keyboard is in it (the class <c>ownkeys</c>): the
    /// standing search's panel floats over the pages, and Esc with a button of its own focused was taken by
    /// the page under it - the selection let go, the panel left open - as Delete would have been.
    /// </para>
    /// </summary>
    public static bool IsBoxOrPicker(Visual? at) =>
        at is not null
        && (at is TextBox or ComboBox
            || at.FindAncestorOfType<TextBox>() is not null
            || at.FindAncestorOfType<ComboBox>() is not null
            || at.FindAncestorOfType<PopupRoot>(includeSelf: true) is not null
            || at.GetSelfAndVisualAncestors().OfType<StyledElement>().Any(each => each.Classes.Contains("ownkeys")));

    /// <summary>
    /// The picker or overlay on top: the last veil in sight, in the order the window draws them - a picker
    /// opened from another's lies after it. Null where none stands. What Esc closes (Esc closed no picker, overlay or
    /// panel - only a rename box).
    /// </summary>
    public static Border? TopVeil(Visual? root)
    {
        if (root is null || !root.IsVisible)
            return null;

        Border? top = root is Border border && IsOpenVeil(border) ? border : null;
        foreach (var child in root.GetVisualChildren())
        {
            if (TopVeil(child) is { } deeper)
                top = deeper;
        }

        return top;
    }

    /// <summary>
    /// The control an arrow key goes to from <paramref name="from"/>: the nearest of <paramref name="among"/> whose
    /// middle lies that way, weighing a step aside twice a step ahead - Explorer's icon view. By where each is drawn,
    /// so a grid of mixed sizes, the worn slots and a wrapped list of cards are walked alike. Null where none lies
    /// that way.
    /// </summary>
    public static T? Nearest<T>(Visual root, Visual from, IEnumerable<T> among, Key arrow) where T : Visual
    {
        static Point? Middle(Visual root, Visual each) =>
            each.TranslatePoint(new Point(each.Bounds.Width / 2, each.Bounds.Height / 2), root);

        if (Middle(root, from) is not { } here)
            return null;

        var others = among.Where(each => !ReferenceEquals(each, from))
            .Select(each => (Control: each, Middle: Middle(root, each)))
            .Where(each => each.Middle is not null)
            .ToList();

        return Nearest(here, others.Select(each => each.Middle!.Value).ToList(), arrow) is { } best ? others[best].Control : null;
    }

    /// <summary>
    /// The same by the middles alone - which of <paramref name="among"/> an arrow goes to from
    /// <paramref name="here"/>, or null - for what is drawn with no control of its own: a grid's items
    /// (TileLayer).
    /// </summary>
    public static int? Nearest(Point here, IReadOnlyList<Point> among, Key arrow)
    {
        int? best = null;
        double bestScore = double.MaxValue;
        for (int index = 0; index < among.Count; index++)
        {
            var there = among[index];
            double dx = there.X - here.X, dy = there.Y - here.Y;
            (double ahead, double aside) = arrow switch
            {
                Key.Left => (-dx, dy),
                Key.Right => (dx, dy),
                Key.Up => (-dy, dx),
                _ => (dy, dx),
            };

            // At least a little way ahead - two tiles side by side are not above one another - and within a cone that
            // way: from the amulet, Right went down to the boots, the only thing further right at all.
            if (ahead < 4 || System.Math.Abs(aside) > ahead * 1.5)
                continue;

            double score = ahead + 2 * System.Math.Abs(aside);
            if (score < bestScore)
            {
                bestScore = score;
                best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a veil stands open: in sight and wearing "shown". Most pickers stay laid out once opened
    /// (Presence.Keep) and only the class comes and goes; one fading out is in sight and no longer shown.
    /// </summary>
    public static bool IsOpenVeil(Border veil) =>
        veil.Classes.Contains("veil") && veil.Classes.Contains("shown") && veil.IsEffectivelyVisible;

    /// <summary>
    /// Closes the picker or overlay on top, as a click beside it does: its backdrop's command - every veil holds
    /// a <c>Button.backdrop</c> bound to its picker's close. True where one was closed.
    /// </summary>
    public static bool CloseTopVeil(Visual? root)
    {
        if (TopVeil(root) is not { } veil)
            return false;

        var backdrop = veil.GetVisualDescendants().OfType<Button>().FirstOrDefault(each => each.Classes.Contains("backdrop"));
        if (backdrop?.Command is not { } close || !close.CanExecute(backdrop.CommandParameter))
            return false;

        close.Execute(backdrop.CommandParameter);
        return true;
    }

    /// <summary>
    /// Whether a picker or a question stands over the window: a veil in sight (every one is a
    /// <c>Border.veil</c>, shown only while its picker is), or the question (<c>Border.asking</c>). The
    /// panels' keys are not theirs then - Ctrl+A, Delete and Esc reached the items behind an open property
    /// picker whose search box had not been clicked into, and two presses of Delete emptied a test
    /// character; Enter on a vault folder's question opened the folder instead of answering.
    /// Hidden branches are not walked: the pages kept behind the one in front are most of the tree.
    /// </summary>
    public static bool IsVeiled(Visual? root)
    {
        if (root is null || !root.IsVisible)
            return false;

        if (root is Border border && (border.Classes.Contains("veil") || border.Classes.Contains("asking")))
            return true;

        foreach (var child in root.GetVisualChildren())
        {
            if (IsVeiled(child))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an item panel other than <paramref name="panel"/> is in sight: something carried
    /// out of this one has somewhere to go, so leaving this one does not put it back.
    /// </summary>
    public static bool OtherShown(Control panel) =>
        Panels.Any(each => each.TryGetTarget(out var alive) && !ReferenceEquals(alive, panel) && alive.IsEffectivelyVisible);

    /// <summary>How far a press on a socket of the Sockets row or on a Grail card moves before it is a drag.</summary>
    public const double DragStart = 5;

    /// <summary>
    /// The cell of a grid under the pointer, wherever in the window - for a carry begun outside the grids, the Sockets
    /// row's or a Grail card's: asked of the grid that is drawn there. Null over none.
    /// </summary>
    public static PD2SPCompanion.Shell.ViewModels.GridCell? CellAt(TopLevel? window, PointerEventArgs e)
    {
        if (window?.InputHitTest(e.GetPosition(window)) is not Visual hit
            || (hit as ContainerGrid ?? hit.FindAncestorOfType<ContainerGrid>()) is not { } grid)
        {
            return null;
        }

        return grid.GetVisualDescendants().OfType<CellLayer>().FirstOrDefault() is { } layer ? layer.CellAt(e.GetPosition(layer)) : null;
    }

    private static void Pressed(object? sender, PointerPressedEventArgs e)
    {
        var pressed = Panels
            .Select(each => each.TryGetTarget(out var alive) ? alive : null)
            .FirstOrDefault(panel => panel is { IsEffectivelyVisible: true, IsPointerOver: true });

        if (pressed is not null)
            _owner = new WeakReference<Control>(pressed);
    }
}
