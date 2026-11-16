using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The keys every picker answers to, said once for all of them: a picker opening takes the focus - its search box, else
/// the row it holds, else
/// its first row, or the control the panel names (<see cref="TakesFocusProperty"/>) - and gives it back on closing; Up and Down walk its rows (<c>Button.pickone</c>), Up from the first
/// back to the search box; Enter in the search box steps into the rows, and on a row picks it, as a click does (the
/// button's own Enter). Only the property picker focused its search box before, and no picker took an arrow key.
/// A panel with no rows - a rename, the drop rates, the backups - is left as it is.
/// </summary>
public static class PickerKeys
{
    /// <summary>
    /// On a text box in a picker that is not its search box but a name being typed - a new vault's, a row's
    /// being renamed: Enter is the box's own, to take the name, and does not step into the rows. Without it
    /// every box in a picker was taken for the search box, and a name's Enter went to the first row instead
    /// (found with the vaults' list).
    /// </summary>
    public static readonly AttachedProperty<bool> OwnsEnterProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("OwnsEnter", typeof(PickerKeys));

    public static bool GetOwnsEnter(TextBox box) => box.GetValue(OwnsEnterProperty);

    public static void SetOwnsEnter(TextBox box, bool value) => box.SetValue(OwnsEnterProperty, value);

    /// <summary>
    /// On the one control of a panel that takes the focus as the panel opens, where the guess below would be
    /// wrong: a panel with no search box and no rows whose first text box is a number - the mercenary's popup,
    /// whose level box took the focus, so that a key pressed next changed its level.
    /// Its button to close has this instead: Enter closes, nothing is changed by accident.
    /// </summary>
    public static readonly AttachedProperty<bool> TakesFocusProperty =
        AvaloniaProperty.RegisterAttached<InputElement, bool>("TakesFocus", typeof(PickerKeys));

    public static bool GetTakesFocus(InputElement element) => element.GetValue(TakesFocusProperty);

    public static void SetTakesFocus(InputElement element, bool value) => element.SetValue(TakesFocusProperty, value);

    /// <summary>What had the focus before each open picker took it: given back when that picker closes.</summary>
    private static readonly Dictionary<Border, IInputElement?> ReturnTo = [];

    private static bool _registered;

    /// <summary>The windows already hooked: attached twice, one press would walk two rows.</summary>
    private static readonly HashSet<TopLevel> Hooked = [];

    /// <summary>Hooks the window: the pickers' opening and closing, and their keys.</summary>
    public static void Attach(TopLevel window, Visual root)
    {
        if (!_registered)
        {
            _registered = true;
            Visual.IsVisibleProperty.Changed.AddClassHandler<Border>((veil, _) => Seen(veil));
        }

        if (Hooked.Add(window))
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => OnKey(window, root, e), RoutingStrategies.Tunnel);
    }

    /// <summary>The veils already watched for their "shown" class.</summary>
    private static readonly HashSet<Border> Watched = [];

    /// <summary>
    /// A veil come into the tree's sight. A picker is open while its veil is in sight <b>and</b> wears "shown":
    /// most stay laid out once opened (Presence.Keep), only the class coming and going - so the class is watched.
    /// </summary>
    private static void Seen(Border veil)
    {
        if (!veil.Classes.Contains("veil"))
            return;

        if (Watched.Add(veil))
            veil.Classes.CollectionChanged += (_, _) => Shown(veil);

        Shown(veil);
    }

    /// <summary>Whether a veil stands open: in sight and shown (ItemPanels.IsOpenVeil).</summary>
    private static void Shown(Border veil)
    {
        if (TopLevel.GetTopLevel(veil) is not { } window)
            return;

        bool open = ItemPanels.IsOpenVeil(veil);
        if (open == ReturnTo.ContainsKey(veil))
            return;

        if (open)
        {
            ReturnTo[veil] = window.FocusManager?.GetFocusedElement();

            TakeFocus(veil, tries: 10);
            return;
        }

        if (ReturnTo.Remove(veil, out var before) && before is Visual { IsEffectivelyVisible: true } && before is InputElement element)
            element.Focus();
    }

    /// <summary>
    /// The opening picker takes the focus: its search box, else the row it holds, else its first. After the veil is
    /// laid out - a hidden box takes no focus - and again a few times while its rows are still being built: a list
    /// fills after the panel shows, and the corruption picker's focus stayed on the button that opened it.
    /// </summary>
    private static void TakeFocus(Border veil, int tries)
    {
        void Try()
        {
            if (!ItemPanels.IsOpenVeil(veil))
                return;

            // Told where: the panel's own word beats the guess.
            if (veil.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(each => GetTakesFocus(each) && each.IsEffectivelyVisible) is { } told)
            {
                told.Focus();
                return;
            }

            // Never a number's box: the drop rates' first box took the focus, and Up or PageUp then changed a group's
            // rate - the trap TakesFocus was made for in the mercenary's popup, met again.
            var search = veil.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(box => box.IsEffectivelyVisible && !GetOwnsEnter(box) && box is not NumericTextBox);
            if (search is not null)
            {
                search.Focus();
                return;
            }

            var rows = Rows(veil);
            if ((rows.FirstOrDefault(row => row.Classes.Contains("held")) ?? rows.FirstOrDefault()) is { } row)
                row.Focus(NavigationMethod.Directional);
            else if (tries > 1)
                TakeFocus(veil, tries - 1);
        }

        // The first try once laid out; the rest a beat apart, as the opening panel builds its list.
        if (tries == 10)
            Dispatcher.UIThread.Post(Try, DispatcherPriority.Loaded);
        else
            DispatcherTimer.RunOnce(Try, System.TimeSpan.FromMilliseconds(40));
    }

    /// <summary>The rows a picker offers, in the order they are drawn: its pick buttons in sight.</summary>
    private static List<Button> Rows(Visual veil) =>
        [.. veil.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("pickone") && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)];

    private static void OnKey(TopLevel window, Visual root, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.Enter or Key.Return)
            || ItemPanels.TopVeil(root) is not { } veil)
        {
            return;
        }

        var rows = Rows(veil);
        if (rows.Count == 0)
            return;

        var focused = window.FocusManager?.GetFocusedElement() as Visual;

        // A number box in a picker - an affix's value - steps its number with the arrows itself.
        if (focused is NumericTextBox)
            return;
        // A name being typed keeps its Enter, and its arrows are the caret's.
        if (focused is TextBox named && GetOwnsEnter(named))
            return;

        bool inSearch = focused is TextBox box && veil.IsVisualAncestorOf(box);
        int at = focused is Button button ? rows.IndexOf(button) : -1;

        // Enter on a row is the row's own: the button picks it.
        if (e.Key is Key.Enter or Key.Return)
        {
            if (!inSearch)
                return;

            Move(rows[0], e);
            return;
        }

        if (e.Key == Key.Down)
        {
            Move(at < 0 ? rows[0] : rows[System.Math.Min(at + 1, rows.Count - 1)], e);
            return;
        }

        // Up: the row above, or from the first back to the search box.
        if (at > 0)
        {
            Move(rows[at - 1], e);
        }
        else if (at == 0 && veil.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(each => each.IsEffectivelyVisible && !GetOwnsEnter(each)) is { } search)
        {
            search.Focus();
            e.Handled = true;
        }
    }

    private static void Move(Button row, KeyEventArgs e)
    {
        row.Focus(NavigationMethod.Directional);
        row.BringIntoView();
        e.Handled = true;
    }
}
