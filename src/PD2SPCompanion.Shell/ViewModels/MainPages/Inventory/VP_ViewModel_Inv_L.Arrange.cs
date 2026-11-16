using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.ViewModels;

// A stash page laid out again, stash only: the vault's
// Sort menu and its Close gaps button, on the personal stash and on a shared page - never the inventory, whose
// places are the player's own, nor the cube. Core's PageArrange has the orders and the cells.
public partial class VP_ViewModel_Inv_L
{
    private SortChoice? _firstOrder, _secondOrder;

    /// <summary>The first pane's Sort menu: an order picked lays the page it shows out again in it.</summary>
    public SortChoice FirstOrder => _firstOrder ??= OrderOfPane("StashPane", second: false);

    /// <summary>The second pane's, with an order remembered apart.</summary>
    public SortChoice SecondOrder => _secondOrder ??= OrderOfPane("StashPane2", second: true);

    private SortChoice OrderOfPane(string key, bool second)
    {
        var order = new SortChoice(Rules.Settings, key, PageArrange.Orders);

        // An action, as the vault's: the same order picked again is asked for again. A locked page refuses
        // before the order is taken.
        order.Allowed = () => PaneOf(second) is not { CanArrange: true } page || MayArrange(page);

        // And said before it is asked: the button is dimmed on a locked page, its tooltip saying why.
        order.Blocked = () => PaneOf(second)?.ArrangeBlocked;
        order.Picked += () => Arrange(second, order.By, order.Descending);
        return order;
    }

    /// <summary>
    /// The button beside a pane's Sort menu, as the vault page has it: the page's gaps closed, the order its
    /// items lie in kept. The pane - "second" for the second - is the button's parameter.
    /// </summary>
    [RelayCommand]
    private void CloseGaps(string? pane) => Arrange(second: pane == "second", by: null, descending: false);

    /// <summary>A pane shows another page, or a page's lock was turned: the Sort menus' tooltips are read again.</summary>
    private void SayArrangeBlocked()
    {
        _firstOrder?.SayBlocked();
        _secondOrder?.SayBlocked();
    }

    /// <summary>The page a pane shows.</summary>
    private StashPageChoice? PaneOf(bool second)
    {
        var section = Sections.FirstOrDefault(each => each.Pages is not null);
        return second ? section?.SecondPage : section?.SelectedPage;
    }

    /// <summary>Whether a page may be laid out again: not a locked one, which says so.</summary>
    private bool MayArrange(StashPageChoice page)
    {
        if (!page.IsLocked)
            return true;

        _sfxManager?.Method_ButtonSFX_Press();
        Session.Status = L("Page locked.");
        return false;
    }

    /// <summary>
    /// The page a pane shows, laid out again: in an order, or - <paramref name="by"/> null - as it lies with its
    /// gaps closed. Every item gives up its cell and takes the first free room in turn. A locked page is left
    /// as it lies, as Store all and Select all leave it; an order the page has no room for moves nothing. One
    /// step of the session's undo, written by the autosave like any move.
    /// </summary>
    private void Arrange(bool second, string? by, bool descending)
    {
        var page = PaneOf(second);
        if (page is not { CanArrange: true, Grid: { } grid })
            return;

        if (!MayArrange(page))
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        var items = grid.Items.Select(tile => tile.Item).Distinct(ReferenceEqualityComparer.Instance).Cast<Item>().ToList();
        if (items.Count == 0)
        {
            Session.Status = L($"{page.Label} is empty.");
            return;
        }

        // Close gaps: the holes filled, each by the last item of the page that fits it - only those move. An
        // order: every item, into the first free room in turn.
        IReadOnlyList<Item> ordered;
        IReadOnlyList<(int X, int Y)> cells;
        if (by is null)
        {
            var moves = PageArrange.CloseGaps(
                [.. items.Select(item =>
                {
                    var (width, height) = ItemPresenter.FootprintOf(item);
                    return new PageArrange.Piece(item.X, item.Y, width, height);
                })],
                grid.Columns);
            if (moves.Count == 0)
            {
                Session.Status = L($"{page.Label}: no gaps to close.");
                return;
            }

            ordered = [.. moves.Select(move => items[move.Index])];
            cells = [.. moves.Select(move => (move.X, move.Y))];
        }
        else
        {
            ordered = PageArrange.InOrder(items, by, descending);
            if (PageArrange.LayOut([.. ordered.Select(ItemPresenter.FootprintOf)], grid.Columns, grid.Rows) is not { } laid)
            {
                Session.Status = L("No room in this order. Nothing moved.");
                return;
            }

            cells = laid;
        }

        // Each as it stands now, so it can be put back if a file objects. One it will not let go of stops it all: laid out
        // with it in place, the others could be given its cell.
        var lifts = ItemLift.OfAll(ordered.Select(item => (Holder(page), item)));
        string? refused = lifts.Count == ordered.Count ? null : L("Item not moved.");

        // Its id kept: it is the same item in another cell.
        for (int each = 0; each < lifts.Count && refused is null; each++)
            refused = lifts[each].Into(Holder(page), page.Grid!.Page, cells[each].X, cells[each].Y, keepId: true);

        if (refused is not null)
        {
            ItemLift.Back(lifts);
            Session.Status = L($"{refused} Nothing moved.");
        }
        else
        {
            ItemLift.Done(lifts);
            Session.Status = by is null ? L($"{page.Label}: gaps closed.") : L($"{page.Label} sorted by {L(by)}.");
        }

        Session.RefreshContainers();
        Stash.RefreshPages();
    }
}
