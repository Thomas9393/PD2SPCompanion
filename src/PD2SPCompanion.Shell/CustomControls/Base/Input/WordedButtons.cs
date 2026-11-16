using System.Linq;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace PD2SPCompanion.Shell.CustomControls.Base.Input;

/// <summary>
/// A button of a glyph and a word in a panel says the word to a screen reader. Left alone, its automation name is its
/// content's type, "Avalonia.Controls.Grid" (LOAD, CANCEL, CLEAN, RESTORE and every other of that make).
/// </summary>
public static class WordedButtons
{
    /// <summary>Every button, as it comes on screen. Once, at the program's start.</summary>
    public static void Listen() => Control.LoadedEvent.AddClassHandler<Button>((button, _) => Name(button));

    /// <summary>
    /// The name, following the word as it changes. Left alone: a button with a name of its own, and one with no panel
    /// or no word in it - a glyph alone, whose tooltip says what it is. Under what the page itself binds: a bare worn
    /// slot's button is named for the item to come, its binding saying nothing yet, and bound over at the page's level
    /// the slot's tile stayed named for the count hidden in it.
    /// </summary>
    public static void Name(Button button)
    {
        if (button.Content is not Panel panel || !string.IsNullOrEmpty(AutomationProperties.GetName(button))
            || panel.Children.OfType<TextBlock>().FirstOrDefault() is not { } word)
            return;

        button.Bind(AutomationProperties.NameProperty, word.GetObservable(TextBlock.TextProperty), BindingPriority.Style);
    }
}
