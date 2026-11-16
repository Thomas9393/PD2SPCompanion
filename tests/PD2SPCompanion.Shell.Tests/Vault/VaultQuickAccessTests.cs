using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// Quick access, a section for fast access with a toggle in the tool strip, just
/// like Windows Explorer: the folder shown is pinned by the tools' star and taken off by it; the folders pinned
/// are rows of their own over the tree, on both vault pages, each going to its folder, lit while it is the one
/// shown and wearing its lock; the vault's top is not pinned - it is the tree's first row.
/// <para>
/// What the page decides, asked as the star and the rows ask. That the section stands over the tree and stays
/// there while the tree scrolls is the UI cases' to show. The list itself, kept in the vault's file, is Core's
/// (VaultQuickAccessTests there).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultQuickAccessTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void The_star_pins_the_folder_shown_and_takes_it_off_and_its_row_goes_to_it()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenBoth();
        program.On(() =>
        {
            // The vault's top: nothing to pin.
            Assert.False(Page.CanQuick);
            Assert.False(Page.HasQuick);
            Assert.Equal("Pin to quick access" + Environment.NewLine + "Top level cannot be pinned.", Page.QuickHint);

            // Nothing selected: the tools that take a selection are dimmed, and each says so under its name.
            Assert.False(Page.CanMove || Page.CanCut || Page.CanCopy || Page.CanRenameFolder);
            Assert.Equal("Move to folder" + Environment.NewLine + "Nothing selected.", Page.MoveHint);
            Assert.Equal("Cut (Ctrl + X)" + Environment.NewLine + "Nothing selected.", Page.CutHint);
            Assert.Equal("Copy (Ctrl + C)" + Environment.NewLine + "No item selected.", Page.CopyHint);
            Assert.Equal("Rename folder (F2)" + Environment.NewLine + "Require one folder selected.", Page.RenameFolderHint);

            VaultBook.GoTo(Page, "Small");
            Assert.Equal("Pin to quick access", Page.QuickHint);
            Assert.True(Page.CanQuick);
            Assert.False(Page.IsQuick);

            Page.ToggleQuickCommand.Execute(null);
            Assert.True(Page.IsQuick);
            Assert.True(Page.HasQuick);
            Assert.Equal("Unpin from quick access", Page.QuickHint);
            var row = Assert.Single(Page.QuickPlaces);
            Assert.Equal("Small", row.Name);
            Assert.Equal(@"Vault\Charms\Small", row.Path);
            Assert.Equal("2 items", row.ItemsLine);
            Assert.True(row.IsHere);
            Assert.False(row.HasChevronRoom);

            // The other page's tree has it too: it is the vault's, not the page's.
            Assert.Equal("Small", Assert.Single(_book.Right.QuickPlaces).Name);
            Assert.False(_book.Right.QuickPlaces[0].IsHere);

            // A second, after it; and the first wears its folder's lock.
            VaultBook.GoTo(Page, "Jewels");
            Assert.False(Page.IsQuick);
            Page.ToggleQuickCommand.Execute(null);
            Assert.Equal(["Small", "Jewels"], Page.QuickPlaces.Select(place => place.Name));
            Assert.False(Page.QuickPlaces[0].IsHere);
            VaultBook.ToggleLock(Page, "Small");
            Assert.True(Page.QuickPlaces[0].IsLocked);
            VaultBook.ToggleLock(Page, "Small");

            // Its row goes to its folder, from anywhere.
            Page.GoTo(null);
            Page.GoToPlace(Page.QuickPlaces[0]);
            Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(Page));
            Assert.True(Page.QuickPlaces[0].IsHere);

            // And the star takes the folder shown off again.
            Page.ToggleQuickCommand.Execute(null);
            Assert.False(Page.IsQuick);
            Assert.Equal("Jewels", Assert.Single(Page.QuickPlaces).Name);
            VaultBook.GoTo(Page, "Jewels");
            Page.ToggleQuickCommand.Execute(null);
            Assert.False(Page.HasQuick);
            Assert.Empty(_book.Right.QuickPlaces);
        });
    }
}
