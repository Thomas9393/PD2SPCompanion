using CommunityToolkit.Mvvm.ComponentModel;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The folder the vault page shows, for the tools that act on it: its name box while it is being named,
/// and what Move and Delete are handed. It was a card among the items, when folders were listed with them.
/// </summary>
public sealed partial class FolderCard : ObservableObject
{
    public FolderCard(VaultFolder folder)
    {
        Folder = folder;
    }

    public VaultFolder Folder { get; }

    public long Id => Folder.Id;

    public string Name => Folder.Name;

    /// <summary>
    /// The name box, while the folder is being named. Null otherwise - so the box is built
    /// when it appears rather than hidden, and can take the focus the moment it does.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    private FolderRename? _rename;

    public bool IsRenaming => Rename is not null;
}

/// <summary>The name being typed for a folder.</summary>
public sealed partial class FolderRename : ObservableObject
{
    public FolderRename(FolderCard card)
    {
        Card = card;
        _name = card.Name;
    }

    public FolderCard Card { get; }

    [ObservableProperty] private string _name;
}

/// <summary>
/// A folder as the vault page lists it - a row of the folder tree, of a list of folders under the path, a tile
/// of the strip: every folder of the vault, in the
/// tree's order, a step in for each level. A null id is the vault itself.
/// </summary>
public sealed partial class FolderPlace(long? id, string name, int depth, string path, int items = 0,
                                        long? parent = null, bool holdsFolders = false) : ObservableObject
{
    /// <summary>
    /// What stands between the steps of a folder's path, as Windows writes a path (it was
    /// " / "): "Default\Charms\Skillers".
    /// </summary>
    public const string Step = "\\";

    public long? Id { get; } = id;

    public string Name { get; } = name;

    public int Depth { get; } = depth;

    /// <summary>The whole way to it from the vault: what the box says, shut.</summary>
    public string Path { get; } = path;

    /// <summary>How many items lie in it - its own, not its folders'.</summary>
    public int Items { get; } = items;

    /// <summary>The folder it is in, or null at the top.</summary>
    public long? Parent { get; } = parent;

    /// <summary>Whether it has folders of its own to fold away.</summary>
    public bool HoldsFolders { get; } = holdsFolders;

    /// <summary>
    /// The folder shown lies under it: it stays unfolded, and its chevron does nothing -
    /// folded, the list would not hold the folder one is in. Told in
    /// place as the folder shown changes: a list made again while the box is choosing from it left the box blank.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFold))]
    [NotifyPropertyChangedFor(nameof(IsTreeHere))]
    private bool _holdsHere;

    /// <summary>The folder the page shows: its row in the tree is lit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTreeHere))]
    private bool _isHere;

    // ---- the tree -----------------------------------------------------------------------

    /// <summary>
    /// Lit in the tree and in a list of folders: the folder shown - or, where that one is out of sight inside a
    /// folded folder, the folded folder it lies in. The tree is not unfolded to the folder shown ("many people
    /// prefer to only glance over first layer of the tree, and use the folder icon in the main pane to navigate").
    /// </summary>
    public bool IsTreeHere => IsHere || (HoldsHere && !IsOpen);

    /// <summary>How many folders lie in it - its own, not theirs.</summary>
    public int Folders { get; init; }

    /// <summary>
    /// The small line under its name in the tree: how many items lie in it, and how many folders - what it has
    /// none of left unsaid, so a folder of folders does not read "0 items" over the hundreds in them.
    /// </summary>
    public string ItemsLine => (Items, Folders) switch
    {
        (0, 0) => L("Empty"),
        (_, 0) => Plural(Items, $"{Items} item", $"{Items} items"),
        (0, _) => Plural(Folders, $"{Folders} folder", $"{Folders} folders"),
        _ => Plural(Items, $"{Items} item", $"{Items} items") + " · " + Plural(Folders, $"{Folders} folder", $"{Folders} folders"),
    };

    /// <summary>Locked (Vault.IsLocked): the lock on its row, gold, and a small one on its tile.</summary>
    [ObservableProperty]
    private bool _isLocked;

    /// <summary>Something carried is over its row or its tile: let go of, it goes into this folder.</summary>
    [ObservableProperty] private bool _isDropTarget;

    /// <summary>Cut (ItemClipboard): its row and its tile faint until it is pasted or let go. Asked of the clipboard, so a row made anew says the same.</summary>
    public bool IsCut => Id is { } id && PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsCut(id);

    /// <summary>The clipboard changed: the row asks again.</summary>
    public void SayCut() => OnPropertyChanged(nameof(IsCut));

    /// <summary>Its chevron folds it - the folder shown under it or not: its row is lit in its place.</summary>
    public bool CanFold => HoldsFolders;

    /// <summary>A folder, which has a chevron's room before its name; the vault itself has none.</summary>
    public bool IsFolder => Id is not null;

    /// <summary>In a list of one folder's folders, under a crumb's chevron: no tree, so no chevron and no room for one.</summary>
    public bool IsFlat { get; init; }

    /// <summary>The room a chevron takes before the name: a folder's in a tree, so the names line up.</summary>
    public bool HasChevronRoom => IsFolder && !IsFlat;

    /// <summary>
    /// Stepped in by depth, so a list of folders reads as the tree it is - from the top folders, which stand at
    /// its edge under the vault's own name: their chevrons are what steps them in. A little a level, not a big
    /// indentation from the front - the folder tree scrolls
    /// sideways to a deep folder's name besides.
    /// </summary>
    public Avalonia.Thickness Indent => new(System.Math.Max(0, Depth - 1) * 12, 0, 0, 0);

    /// <summary>The number beside its name in the list: nothing for none.</summary>
    public string ItemsText => Items > 0 ? Items.ToString() : "";

    /// <summary>It holds items: the number has its room beside the name, and none where there is none to say.</summary>
    public bool HasItems => Items > 0;

    /// <summary>
    /// Its folders are listed under it: folded and unfolded by its chevron in the box's list, as in the move
    /// picker - and the same fold, kept.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoldHint))]
    [NotifyPropertyChangedFor(nameof(IsTreeHere))]
    private bool _isOpen = true;

    /// <summary>The chevron's tooltip: an icon button says what it is.</summary>
    public string FoldHint => IsOpen ? L("Fold") : L("Unfold");

    /// <summary>Every folder above it is open: it is in the list.</summary>
    [ObservableProperty] private bool _isShown = true;

    /// <summary>The same folder said the same way: the box keeps the one it has, and a list left open is not shut.</summary>
    public bool Same(FolderPlace other) =>
        Id == other.Id && Name == other.Name && Depth == other.Depth && Path == other.Path && Items == other.Items
        && Parent == other.Parent && HoldsFolders == other.HoldsFolders && Folders == other.Folders;
}

/// <summary>
/// A place the move picker offers. A null id is the vault itself. A folder holding folders folds: its
/// chevron puts them away and brings them back, and the picker remembers which
/// are folded while the editor runs.
/// </summary>
public sealed partial class MoveTarget(long? id, string name, int depth, long? parent, bool holdsFolders) : ObservableObject
{
    public long? Id { get; } = id;
    public string Name { get; } = name;
    public int Depth { get; } = depth;

    /// <summary>The folder it is in, or null at the top.</summary>
    public long? Parent { get; } = parent;

    /// <summary>Whether it has folders of its own to fold away.</summary>
    public bool HoldsFolders { get; } = holdsFolders;

    /// <summary>Indented by depth, so the list reads as the tree it is.</summary>
    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>Its folders are listed under it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoldHint))]
    private bool _isOpen = true;

    /// <summary>The chevron's tooltip: an icon button says what it is.</summary>
    public string FoldHint => IsOpen ? L("Fold") : L("Unfold");

    /// <summary>Every folder above it is open: it is in the list.</summary>
    [ObservableProperty] private bool _isShown = true;
}
