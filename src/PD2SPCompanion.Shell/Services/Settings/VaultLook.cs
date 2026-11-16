namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// How one vault page is laid out: the folder tree at
/// its left and the map in place of the scroll bar - each shown or put away by a button of the page, and both
/// away is the sheet alone. Kept a page, so the two pages of a comparison can differ
/// (<see cref="EditorSettings.VaultLooks"/>). A strip of the folder's folders over the sheet was the third, with
/// its rows, once: what a settings file still says of it is passed over.
/// </summary>
public sealed class VaultLook
{
    /// <summary>The folder tree, at the page's left.</summary>
    public bool Tree { get; set; } = true;

    /// <summary>The folder's map in place of the plain scroll bar.</summary>
    public bool Minimap { get; set; } = true;

    /// <summary>How wide the folder tree is, as its edge was last dragged.</summary>
    public double TreeWidth { get; set; } = DefaultTreeWidth;

    public const double DefaultTreeWidth = 170, NarrowestTree = 90, WidestTree = 1600;

    /// <summary>How wide the tree's edge is: the grip it is dragged by.</summary>
    public const double EdgeWidth = 8;
}
