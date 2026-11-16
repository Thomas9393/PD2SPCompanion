using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// What the two socket toggles say of themselves, on whichever page they stand: the Items page and the vault's
/// have the same two buttons over the same two states (PlayRules.SocketsFaded, PlayRules.SocketGuard), and say
/// them in the same words, so the two pages are not inconsistent.
/// </summary>
public static class SocketWords
{
    /// <summary>Says which way the press goes, since the icon alone cannot - and its key, X, on both pages.</summary>
    public static string Look(bool faded) =>
        ShortcutText.With(faded ? L("Transparent sockets") : L("Opaque sockets"), L("X"));

    public static string Guard(bool on) => on
        ? L("Socket guard on") + Environment.NewLine + L("Nothing dropped on an item is socketed")
        : L("Socket guard off") + Environment.NewLine + L("A gem, rune or jewel dropped on an item is socketed");
}
