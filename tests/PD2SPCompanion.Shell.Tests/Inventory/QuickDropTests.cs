using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Quick in-grid drops are not put back (grids kept across rebuilds): a ring in the personal
/// stash carried a cell over and let go of at once, ten times, back and forth, lands every time - and the
/// container it lies in is the same grid through every rebuild, which is what leaves the view its cells to
/// find under a release that comes before the next drawing.
/// <para>
/// The page's half of it, asked as the view asks at the press and the release (BeginCarry, DropCarried). The
/// race itself - a release before the grid is drawn again finding its cell - is the window's, and the UI
/// case's (units/quick-drop.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class QuickDropTests(Program program)
{
    private const string Stash = "Personal Stash", Ring = "Steel Ring of Skill";

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    [CorpusFact]
    public void Carried_a_cell_over_and_let_go_of_at_once_ten_times_the_ring_lands_every_time_on_the_same_grid()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            var grid = program.Grid(Stash);
            int[] columns = [0, 1];
            Assert.Equal((1, 1), ((int)program.Tile(Ring, Stash).Item.X, (int)program.Tile(Ring, Stash).Item.Y));
            Assert.Null(program.At(Stash, 0, 1));

            for (int drop = 0; drop < 10; drop++)
            {
                int to = columns[drop % 2];
                Assert.True(Page.BeginCarry(program.Tile(Ring, Stash)));
                Assert.True(Session.IsHolding, $"drop {drop}: the ring was not lifted");

                Page.DropCarried(program.Cell(Stash, to, 1), pack: false);
                Assert.False(Session.IsHolding, $"drop {drop}: the ring is still carried");
                Assert.Equal("Moved Steel Ring of Skill to stash.", Session.Status);

                var ring = program.Tile(Ring, Stash);
                Assert.Equal((to, 1), ((int)ring.Item.X, (int)ring.Item.Y));

                // Looked at where it landed, as a click there shows it.
                Assert.Same(ring, Session.SelectedItem);
                Assert.Same(grid, program.Grid(Stash));
                Assert.Same(grid, program.Section().Grid);
            }
        });
    }

    [CorpusFact]
    public void Let_go_of_on_a_cell_another_item_lies_on_the_ring_is_put_back_and_the_strip_says_so()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                program.Panes(Stash);
                string cells = program.Cells(Stash);
                Assert.NotNull(program.At(Stash, 1, 2));

                Assert.True(Page.BeginCarry(program.Tile(Ring, Stash)));
                Page.DropCarried(program.Cell(Stash, 1, 2), pack: false);

                Assert.False(Session.IsHolding);
                Assert.EndsWith(" Put back.", Session.Status);
                Assert.Equal(cells, program.Cells(Stash));
                Assert.False(program.Get<SaveSet>().HasChanges);
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
