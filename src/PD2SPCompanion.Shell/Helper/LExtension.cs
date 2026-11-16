using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

// {L 'text'} in any view without an xmlns of its own: this namespace joins Avalonia's.
[assembly: XmlnsDefinition("https://github.com/avaloniaui", "PD2SPCompanion.Shell.Helper.Words")]

namespace PD2SPCompanion.Shell.Helper.Words;

/// <summary>
/// A view's fixed text in the language in use: <c>Text="{L 'No properties.'}"</c>, the English being the
/// key as in code (<see cref="PD2SPCompanion.Core.Localization.Words"/>). Read once, when the view is built:
/// a language takes effect on restart. Build guard PD2UI011 fails a view
/// that writes its text without it.
/// </summary>
public sealed class LExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(System.IServiceProvider serviceProvider) =>
        PD2SPCompanion.Core.Localization.Words.L(Text);
}
