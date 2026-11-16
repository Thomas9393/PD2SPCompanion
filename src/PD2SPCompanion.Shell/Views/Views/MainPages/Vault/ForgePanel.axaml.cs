using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.Views;

public partial class ForgePanel : UserControl
{
    public ForgePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ForgeViewModel forge)
                forge.Clipboard = ReadClipboard;
        };
    }

    private Window? _window;

    /// <summary>The clipboard's text, for the page: a window is what has a clipboard.</summary>
    private async Task<string?> ReadClipboard() =>
        TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    /// <summary>
    /// The editor's window come to the front - the way back from the game, where an item is copied: the page
    /// looks at the clipboard, while it is the page shown. No timer does (ForgeViewModel.Peek).
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (IsEffectivelyVisible && DataContext is ForgeViewModel forge)
            _ = forge.Peek();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.Activated += OnWindowActivated;

        // Shown for the first time: the page was told before it had a window to read a clipboard from.
        if (DataContext is ForgeViewModel forge)
            _ = forge.Peek();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
            _window.Activated -= OnWindowActivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The search's list scrolled within a row or two of its end: the next finds join it.</summary>
    private void OnMatchesScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer list && DataContext is ForgeViewModel forge
            && list.Extent.Height > list.Viewport.Height
            && list.Offset.Y + list.Viewport.Height >= list.Extent.Height - 48)
            forge.LoadMore();
    }
}
