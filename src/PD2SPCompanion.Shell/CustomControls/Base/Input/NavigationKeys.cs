using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Getting about the window without the mouse:
/// <list type="bullet">
/// <item>Ctrl+1 to Ctrl+6 - the side bar's pages, top to bottom: Characters, Character, Items, Extra, Experimental, Settings;</item>
/// <item>Ctrl+Tab and Ctrl+Shift+Tab - the right page's bookmarks round, as a browser's tabs;</item>
/// <item>Ctrl+PageUp and Ctrl+PageDown - the left page's two bookmarks, Inventory and Vault;</item>
/// <item>Ctrl+F - the left page's search.</item>
/// </list>
/// Each presses the very button a click would, through its automation peer, so every guard it has holds: a page
/// that needs a character stays shut without one, a hidden bookmark (the Forge outside Free) is passed over.
/// Not while a picker or a question stands over the window.
/// </summary>
public static class NavigationKeys
{
    private static readonly string[] Pages =
        ["Logo", "SideBar_Char", "SideBar_Inventory", "SideBar_Extra", "SideBar_Experimental", "SideBar_Settings"];

    /// <summary>The right page's bookmarks, in the order they stand.</summary>
    private static readonly string[] Right =
        ["Bookmark_Edit", "Bookmark_Vault_Right", "Bookmark_Search", "Bookmark_Forge", "Bookmark_Trade", "Bookmark_Grail"];

    private static readonly string[] Left = ["Bookmark_Inventory", "Bookmark_Vault_Left"];

    /// <summary>Handles the key where it is one of these; true where it was.</summary>
    public static bool Handle(Control root, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || ItemPanels.IsVeiled(root))
            return false;

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!shift && e.Key is >= Key.D1 and <= Key.D6)
            return Press(Find(root, Pages[e.Key - Key.D1]));

        if (!shift && e.Key is >= Key.NumPad1 and <= Key.NumPad6)
            return Press(Find(root, Pages[e.Key - Key.NumPad1]));

        return e.Key switch
        {
            Key.Tab => Step(root, Right, shift ? -1 : +1),
            Key.PageDown when !shift => Step(root, Left, +1),
            Key.PageUp when !shift => Step(root, Left, -1),
            _ => false,
        };
    }

    /// <summary>
    /// What Tab walks, and nothing else, no other control responding to it: the window's three,
    /// then the side bar top to bottom, round. Enter or Space then presses the one reached.
    /// </summary>
    private static readonly string[] Ring =
        ["MinimizeButton", "MaximizeButton", "CloseButton", .. Pages];

    /// <summary>
    /// Whether the question is up: its panel in sight and shown. One fading out (it stays in the window for its
    /// fade) is answered already, and takes no key.
    /// </summary>
    private static bool Asking(Control root) =>
        root.FindControl<Border>("QuestionPanel") is { IsEffectivelyVisible: true } panel && panel.Classes.Contains("shown");

    /// <summary>Tab and Shift+Tab round <see cref="Ring"/>, or round a question's own buttons while one is up.</summary>
    public static bool Tab(Control root, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;

        var window = TopLevel.GetTopLevel(root);
        if (window is null)
            return false;

        int by = e.KeyModifiers == KeyModifiers.Shift ? -1 : +1;

        // A picker open: round its own boxes and buttons, never out of it to the window behind.
        // The caret in a box elsewhere: round the boxes of its section - a form's fields, the next a Tab away.
        if (!Asking(root) && WithinRing(window, by) is { } moved)
        {
            return moved;
        }

        // A question up: its buttons, as a dialog's Tab does - not the window behind it.
        List<Button> ring = Asking(root) && root.FindControl<Border>("QuestionPanel") is { } question
            ? [.. question.GetVisualDescendants().OfType<Button>().Where(Reachable)]
            : [.. Ring.Select(name => window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == name))
                   .OfType<Button>().Where(Reachable)];
        if (ring.Count == 0)
            return false;

        int at = ring.FindIndex(button => button.IsFocused);
        var next = ring[at < 0 ? (by > 0 ? 0 : ring.Count - 1) : ((at + by) % ring.Count + ring.Count) % ring.Count];
        next.Focus(NavigationMethod.Tab);
        return true;
    }

    private static bool Reachable(Button button) => button.IsEffectivelyVisible && button.IsEffectivelyEnabled;

    /// <summary>
    /// Tab inside a picker, or among a section's boxes: the next of them, round - or null where neither holds and the
    /// window's own ring is Tab's. A picker's are its boxes and its buttons (not the backdrop that closes it); a
    /// section's, its boxes alone, each with its text selected, as a click into one selects it.
    /// </summary>
    private static bool? WithinRing(TopLevel window, int by)
    {
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        List<InputElement> ring;

        if (ItemPanels.TopVeil(window) is { } veil)
        {
            ring = [.. veil.GetVisualDescendants().OfType<InputElement>()
                .Where(each => (each is TextBox || each is Button button && !button.Classes.Contains("backdrop"))
                               && each.IsEffectivelyVisible && each.IsEffectivelyEnabled && each.Focusable)];
        }
        else if (focused is TextBox box && Section(box) is { } section)
        {
            ring = [.. section.GetVisualDescendants().OfType<TextBox>()
                .Where(each => each.IsEffectivelyVisible && each.IsEffectivelyEnabled && !each.IsReadOnly)];
        }
        else
        {
            return null;
        }

        if (ring.Count == 0)
            return null;

        int at = focused is null ? -1 : ring.FindIndex(each => ReferenceEquals(each, focused) || each.IsVisualAncestorOf(focused));
        var next = ring[at < 0 ? (by > 0 ? 0 : ring.Count - 1) : ((at + by) % ring.Count + ring.Count) % ring.Count];
        next.Focus(NavigationMethod.Tab);
        if (next is TextBox text)
            text.SelectAll();
        return true;
    }

    /// <summary>The section a box stands in: the nearest framed section, else the page it is on.</summary>
    private static Control? Section(Control box) =>
        box.GetVisualAncestors().OfType<Control>().FirstOrDefault(each => each is Border border && border.Classes.Contains("section"))
        ?? box.GetVisualAncestors().OfType<UserControl>().FirstOrDefault();

    /// <summary>The bookmark after (or before) the one showing, among those in sight; round from the last to the first.</summary>
    private static bool Step(Control root, string[] names, int by)
    {
        var shown = names.Select(name => Find(root, name)).Where(button => button is { IsEffectivelyVisible: true }).ToList();
        if (shown.Count == 0)
            return false;

        int at = shown.FindIndex(button => button!.Classes.Contains("current"));
        return Press(shown[((at < 0 ? 0 : at + by) % shown.Count + shown.Count) % shown.Count]);
    }

    private static Button? Find(Control root, string name) =>
        root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button => button.Name == name);

    /// <summary>A click without the pointer: the button's own Invoke, or for a side bar switch its Toggle while off.</summary>
    public static bool Press(Button? button)
    {
        if (button is not { IsEffectivelyEnabled: true, IsEffectivelyVisible: true })
            return false;

        var peer = ControlAutomationPeer.CreatePeerForElement(button);
        if (button is ToggleButton toggle)
        {
            if (toggle.IsChecked != true && peer.GetProvider<IToggleProvider>() is { } switcher)
                switcher.Toggle();
            return true;
        }

        if (peer.GetProvider<IInvokeProvider>() is not { } invoker)
            return false;

        invoker.Invoke();
        return true;
    }
}
