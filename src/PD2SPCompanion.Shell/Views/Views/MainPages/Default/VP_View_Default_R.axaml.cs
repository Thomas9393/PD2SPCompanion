using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Default_R : UserControl
{
    public VP_View_Default_R()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Measures the link list again once the page is up.
    /// <para>
    /// Measured with UI Automation: on a fresh start the four links sat 15 px lower
    /// than after any resize, with the window, the panel, the title, the jewel and every link's
    /// own size identical both times. So the list's own height was the one out of date: it is
    /// taken before the links' styled runs (the Diablo font, the sizes from resources) are in
    /// effect, the links then grow by about 30 px between them without the list being measured
    /// again, and a list centred on its old height sits 15 px low with its last link running
    /// into the bottom margin. Any later layout pass puts it right, which is what resizing did.
    /// </para>
    /// <para>
    /// Posted at Background, after the first layout and render, so the runs are styled by then.
    /// The list is hidden until that second measure has been laid out - a second Background post
    /// runs after the layout pass the first one asked for - and then fades in, so the jump from
    /// the old height to the right one is never seen.
    /// </para>
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Dispatcher.UIThread.Post(() =>
        {
            Links.InvalidateMeasure();
            Dispatcher.UIThread.Post(() => LinksReveal.Opacity = 1, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }
}
