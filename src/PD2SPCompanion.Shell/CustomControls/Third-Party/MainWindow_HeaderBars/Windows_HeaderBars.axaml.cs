using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Layer_UserInterface.CustomControls;

public partial class Windows_HeaderBars : UserControl
{
    #region Fields and Members
    #region Child Control Ref
    private Button? minimizeButton;
    private Button? maximizeButton;
    private Path? maximizeIcon;
    private ToolTip? minimizeToolTip;
    private ToolTip? maximizeToolTip;
    private ToolTip? closeToolTip;
    private Button? closeButton;
    private TextBlock? headerTitle;
    #endregion

    #region Temp Fixed Properties
    private static string minimizeButton_Text = "Minimize";
    private static string maximizeButton_Text_maximizing = "Maximize";
    private static string maximizeButton_Text_restoring = "Restore";
    private static string closeButton_Text = "Close";
    #endregion
    #endregion

    #region Properties
    #region Title Content
    public static readonly StyledProperty<string> TitleContentProperty =
        AvaloniaProperty.Register<Windows_HeaderBars, string>(nameof(TitleContent));

    public string TitleContent
    {
        get { return GetValue(TitleContentProperty); }
        set
        {
            SetValue(TitleContentProperty, value);
            if (headerTitle != null)
            {
                headerTitle.Text = TitleContent;
            }
        }
    }
    #endregion
    #endregion

    /// <summary>
    /// The window hosting this control. Avalonia 12's visual root is a TopLevelHost
    /// rather than the Window itself, so VisualRoot cannot be cast directly.
    /// </summary>
    private Window? HostWindow => TopLevel.GetTopLevel(this) as Window;

    #region Constructors
    public Windows_HeaderBars()
    {
        /////////////////////
        ////Initialization////
        /////////////////////
        InitializeComponent();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false)
        {
            this.IsVisible = false;
        }
        else
        {
            minimizeButton = this.FindControl<Button>("MinimizeButton");
            maximizeButton = this.FindControl<Button>("MaximizeButton");
            maximizeIcon = this.FindControl<Path>("MaximizeIcon");
            minimizeToolTip = this.FindControl<ToolTip>("MinimizeToolTip");
            maximizeToolTip = this.FindControl<ToolTip>("MaximizeToolTip");
            closeToolTip = this.FindControl<ToolTip>("CloseToolTip");
            closeButton = this.FindControl<Button>("CloseButton");
            headerTitle = this.FindControl<TextBlock>("HeaderTitle");

            // Test copies, said where every page shows it.
            if (PD2SPCompanion.Core.Saves.TestCopies.IsOn && this.FindControl<Border>("TestCopyBadge") is { } badge)
            {
                badge.IsVisible = true;
                ToolTip.SetTip(badge, PD2SPCompanion.Core.Saves.TestCopies.Describe());
            }

            ///////////////////////////////////
            ////Event Registration & Preset////
            ///////////////////////////////////

            if (minimizeButton is not null) { minimizeButton.Click += MinimizeWindow; }
            if (maximizeButton is not null) { maximizeButton.Click += MaximizeWindow; }
            if (closeButton is not null)    { closeButton.Click += CloseWindow; }

            if (minimizeToolTip is not null) { minimizeToolTip.Content = minimizeButton_Text; }
            if (maximizeToolTip is not null) { maximizeToolTip.Content = maximizeButton_Text_maximizing; }
            if (closeToolTip is not null) { closeToolTip.Content = closeButton_Text; }

            //Final step to subscribe to the top-level window state observing.
            SubscribeToWindowState();
        }
    }
    #endregion

    #region Events
    #region Min, Max, Close Buttons
    private void CloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HostWindow is not null)
        {
            Window hostWindow = HostWindow!;
            hostWindow.Close();
        }
    }

    private void MaximizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HostWindow is not null)
        {
            Window hostWindow = HostWindow!;

            if (hostWindow.WindowState == WindowState.Normal)
            {
                hostWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                hostWindow.WindowState = WindowState.Normal;
            }
        }
    }

    private void MinimizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HostWindow is not null)
        {
            Window hostWindow = HostWindow!;
            hostWindow.WindowState = WindowState.Minimized;
        }
    }
    #endregion

    #region Message Subscription
    private async void SubscribeToWindowState()
    {
        // The control can be constructed before it is attached, so wait for the window.
        Window? hostWindow = HostWindow;

        while (hostWindow is null)
        {
            await Task.Delay(50);
            hostWindow = HostWindow;
        }

        hostWindow.GetObservable(Window.WindowStateProperty).Subscribe(new AnonymousObserver<WindowState>(s =>
        {
            if (s != WindowState.Maximized)
            {
                if (maximizeIcon is not null) maximizeIcon.Data = Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");
                hostWindow.Padding = new Thickness(0, 0, 0, 0);
                if (maximizeToolTip is not null) maximizeToolTip.Content = maximizeButton_Text_maximizing;
            }
            if (s == WindowState.Maximized)
            {
                if (maximizeIcon is not null) maximizeIcon.Data = Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");
                hostWindow.Padding = new Thickness(7, 7, 7, 7);
                if (maximizeToolTip is not null) maximizeToolTip.Content = maximizeButton_Text_restoring;

                // This should be a more universal approach in both cases, but I found it to be less reliable, when for example double-clicking the title bar.
                /*hostWindow.Padding = new Thickness(
                        hostWindow.OffScreenMargin.Left,
                        hostWindow.OffScreenMargin.Top,
                        hostWindow.OffScreenMargin.Right,
                        hostWindow.OffScreenMargin.Bottom);*/
            }
        }));
    }
    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    #endregion

    #region Methods
    #endregion
}