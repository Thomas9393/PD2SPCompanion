using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A row that is kept in sight while it is the one in force (the Items page's list of
/// pages once it scrolls: the row of the page shown must not be scrolled away unseen): as <c>When</c> turns
/// true - and where it is true as the row is drawn - the row is brought into view in whatever scrolls it.
/// </summary>
public sealed class KeepInView : AvaloniaObject
{
    public static readonly AttachedProperty<bool> WhenProperty =
        AvaloniaProperty.RegisterAttached<KeepInView, Control, bool>("When");

    public static bool GetWhen(Control control) => control.GetValue(WhenProperty);

    public static void SetWhen(Control control, bool value) => control.SetValue(WhenProperty, value);

    static KeepInView()
    {
        WhenProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is not true)
                return;

            // Once it is laid out: asked as the value arrives, a row not yet measured has no place to be brought to.
            Dispatcher.UIThread.Post(() =>
            {
                if (GetWhen(control) && control.IsEffectivelyVisible)
                    control.BringIntoView();
            }, DispatcherPriority.Loaded);
        });
    }
}
