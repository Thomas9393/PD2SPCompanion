using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// Tints the Windows 11 frame border so it does not sit as a bright line around a dark
/// window.
/// <para>
/// The border comes from DWM and appears because the window keeps its platform
/// decorations - which is deliberate, since that is what provides native dragging and the
/// minimize/maximize animations. Rather than remove the decorations to hide it, the border
/// is recoloured to match the frame.
/// </para>
/// </summary>
public static class Win11Frame
{
    private const int DwmwaBorderColour = 34;

    /// <summary>A COLORREF is 0x00BBGGRR, so the byte order is reversed from HTML.</summary>
    private const uint FrameColour = 0x0000062F;   // #2F0600, the panel header red

    /// <summary>The frame's gold, #A79066 (the first stop of GradBrush - Frame - Golden#02): the splash's border.</summary>
    public const uint Gold = 0x006690A7;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    private const int DwmwaCloak = 13;

    /// <summary>
    /// The window there and not in sight, or in sight again: cloaked, it is shown, sized, laid out
    /// and drawn as any window - maximized where it comes back maximized - and nothing of it is seen or
    /// clicked. Start-up lays every page out so, behind the splash. False where the platform cannot: the
    /// window is then simply in sight while it is laid out.
    /// </summary>
    public static bool Cloak(Window window, bool cloaked)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            return false;

        try
        {
            if (window.TryGetPlatformHandle() is not { } handle)
                return false;
            uint value = cloaked ? 1u : 0u;
            return DwmSetWindowAttribute(handle.Handle, DwmwaCloak, ref value, sizeof(uint)) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const int ExStyle = -20, Layered = 0x80000, ByAlpha = 2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>How long a revealed window takes to fade in.</summary>
    private static readonly TimeSpan RevealLength = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// A cloaked window brought into sight with a quick fade, rather than there at once (after
    /// the splash, a screen just showing up looks abrupt; kept small and cheap). Windows plays its own opening
    /// only at a window's first show, over what the window has drawn by then - nothing yet, for an Avalonia window
    /// (filmed: an empty outline). Here the whole window is faded by the platform, a step each frame the desktop
    /// draws, from a thread of its own: nothing is drawn again for it, and the start's work on the UI thread does
    /// not hold it back - a timer there stepped every 31 ms, its first step 65 ms late. False where the platform
    /// cannot; the window is then still cloaked.
    /// </summary>
    public static bool Reveal(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            return false;

        IntPtr hwnd, style;
        try
        {
            if (window.TryGetPlatformHandle() is not { } handle)
                return false;
            hwnd = handle.Handle;
            style = GetWindowLongPtr(hwnd, ExStyle);
            SetWindowLongPtr(hwnd, ExStyle, new IntPtr(style.ToInt64() | Layered));
            SetLayeredWindowAttributes(hwnd, 0, 0, ByAlpha);
            uint seen = 0;
            DwmSetWindowAttribute(hwnd, DwmwaCloak, ref seen, sizeof(uint));
        }
        catch (Exception)
        {
            return false;
        }

        new System.Threading.Thread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double done;
            do
            {
                if (DwmFlush() != 0)
                    System.Threading.Thread.Sleep(15);
                done = Math.Min(1, clock.Elapsed / RevealLength);
                double eased = 1 - (1 - done) * (1 - done);
                SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(255 * eased), ByAlpha);
            }
            while (done < 1);

            // Its style as it was: a layered window is drawn through a copy of itself.
            SetWindowLongPtr(hwnd, ExStyle, style);
        }) { IsBackground = true, Name = "Window reveal" }.Start();
        return true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private const int CaptionHeightMetric = 4, SizeFrameMetric = 33, PaddedBorderMetric = 92;

    /// <summary>
    /// How much taller the window stands than the height asked of it, in the window's own units: the
    /// platform's caption, which the client is extended over (<c>ExtendClientAreaToDecorationsHint</c>) -
    /// asked for 720 the window is 750.4 at 125% (measured: caption 28, frame 5, padding 5
    /// pixels). A least height asked is as much taller. Nought where the platform cannot say.
    /// </summary>
    public static double CaptionHeight(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            return 0;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero)
                return 0;

            uint dpi = GetDpiForWindow(handle.Handle);
            if (dpi == 0)
                return 0;

            int pixels = GetSystemMetricsForDpi(CaptionHeightMetric, dpi)
                         + GetSystemMetricsForDpi(SizeFrameMetric, dpi)
                         + GetSystemMetricsForDpi(PaddedBorderMetric, dpi);
            return pixels * 96.0 / dpi;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public static void Apply(Window window) => Apply(window, FrameColour);

    /// <summary>The border in the colour given, a COLORREF (0x00BBGGRR).</summary>
    public static void Apply(Window window, uint colour)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero)
                return;

            DwmSetWindowAttribute(handle.Handle, DwmwaBorderColour, ref colour, sizeof(uint));
        }
        catch (Exception)
        {
            // Purely cosmetic: an older or non-Windows host simply keeps its own border.
        }
    }
}
