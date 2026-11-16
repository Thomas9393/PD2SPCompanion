using Avalonia.Controls.Chrome;
using Avalonia.Controls.Templates;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// A <see cref="WindowDrawnDecorations"/> template that draws nothing.
/// <para>
/// Avalonia 11 expressed this as <c>ExtendClientAreaChromeHints="NoChrome"</c>: keep the
/// platform's window decorations - and with them native dragging, snapping and the
/// minimize/maximize animations - while stopping Avalonia from painting its own title and
/// caption buttons on top of yours. Avalonia 12 removed that hint, and the replacement is
/// to give <c>Window.WindowDecorationsTheme</c> a template with empty content.
/// </para>
/// <para>
/// Setting <c>WindowDecorations="BorderOnly"</c> also removes the duplicate chrome, but it
/// takes the platform caption with it, and the native animations go too.
/// </para>
/// </summary>
public sealed class EmptyWindowDecorations : IWindowDrawnDecorationsTemplate
{
    public TemplateResult<WindowDrawnDecorationsContent> Build() =>
        new(new WindowDrawnDecorationsContent(), new NameScope());

    // It also inherits Avalonia.Styling.ITemplate, whose Build() returns object.
    object Avalonia.Styling.ITemplate.Build() => Build();
}
