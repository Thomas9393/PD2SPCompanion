using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// What the Items page had of sockets and the vault's page had not - the opaque socket
/// view and the socket guard, in sync with the inventory's, as any
/// inconsistency frustrates:
/// the sockets shown or faded and the socket guard are one state for both pages; a gem, a rune or a jewel
/// carried onto a card with an empty socket goes into it - the item rewritten, the gem's row gone, one of a stack
/// taken - unless the guard is on, or what is carried is no such thing; and the tools' Equip for the card selected.
/// <para>
/// What the pages decide, asked as the views ask. That a drag of the mouse ends on the card under it, and that the
/// card is framed while a gem is over it, are the UI cases' to show.
/// </para>
/// In the pad's empty folder: a Cap of two sockets, an El Rune, three Tir Runes in one stack, and a ring.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultSocketingTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;
    private PlayRules Rules => program.Get<PlayRules>();

    public void Dispose()
    {
        if (program.Available)
        {
            program.On(() =>
            {
                Rules.SocketGuard = false;
                Rules.SocketsFaded = false;
            });
        }

        _book.PutAway();
    }

    private long Add(Item item, long folder)
    {
        item.Id = item.IsCompact ? 0 : (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        return _book.Store.Add(item, folder, out string? refused) ?? throw new InvalidOperationException(refused);
    }

    /// <summary>The folder of the pad that holds nothing, filled for these tests and shown on the left page.</summary>
    private void Shelf()
    {
        _book.OpenLeft();
        program.On(() =>
        {
            string era = _book.Session.ItemEra!;
            long folder = _book.Store.Folders().First(each => each.Name == "New folder").Id;
            Add(ItemForge.PlainBase("cap", 2, era).Item, folder);
            Add(ItemForge.Material("r01", 1, era), folder);
            Add(ItemForge.Material("r03", 3, era), folder);   // Season 14's runes stack as themselves; r03s is "Tir Rune (old)"
            Add(ItemForge.PlainBase("rin", 0, era).Item, folder);
            _book.Vaults.SayChanged();
            VaultBook.GoTo(Page, "New folder");
        });
        _book.Until(() => VaultBook.HasCard(Page, "Cap") && VaultBook.HasCard(Page, "El Rune"), "the shelf's cards");
    }

    private Item Stored(string name) => ItemBlob.Restore(_book.Held(name).Blob, _book.Store.StatIdBits);

    [CorpusFact]
    public void The_sockets_look_and_the_socket_guard_are_one_state_for_the_vaults_page_and_the_Items_page()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            var items = program.Main.Inventory!;
            Assert.False(Page.SocketsFaded || items.SocketsFaded);
            Assert.Equal(1.0, Page.SocketOpacity);

            // Faded on the vault's page: faded on the Items page - and back from there.
            Page.ToggleSocketsCommand.Execute(null);
            Assert.True(Page.SocketsFaded && items.SocketsFaded);
            Assert.Equal(items.SocketOpacity, Page.SocketOpacity);
            Assert.True(Page.SocketOpacity < 1);
            items.ToggleSocketsCommand.Execute(null);
            Assert.False(Page.SocketsFaded || items.SocketsFaded);

            // The same of the guard, which is kept in the settings.
            Assert.False(Page.SocketGuard || items.SocketGuard);
            Page.ToggleSocketGuardCommand.Execute(null);
            Assert.True(Page.SocketGuard && items.SocketGuard && program.Get<EditorSettings>().SocketGuard);
            Assert.Equal(items.SocketGuardHint, Page.SocketGuardHint);
            items.ToggleSocketGuardCommand.Execute(null);
            Assert.False(Page.SocketGuard || items.SocketGuard);

            // One button, one key, on both pages.
            Assert.EndsWith("(X)", items.SocketHint);
            Assert.Equal(items.SocketHint, Page.SocketHint);
        });
    }

    /// <summary>
    /// A row marked to leave, whose save has not gone through: a gem put into it would go with it at the next good save,
    /// and one that is itself leaving would be on the character and in the socket both.
    /// </summary>
    [CorpusTheory]
    [InlineData("Cap")]
    [InlineData("El Rune")]
    public void Nothing_is_socketed_where_either_card_is_on_its_way_out_of_the_vault(string leaving)
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        program.On(() =>
        {
            var moves = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultMoves>();
            var saveNow = moves.SaveNow;
            int asked = 0;
            moves.SaveNow = () =>
            {
                asked++;
                return false;
            };

            try
            {
                int held = _book.Holds("New folder");
                var cap = VaultBook.Card(Page, "Cap");
                var rune = VaultBook.Card(Page, "El Rune");
                var going = leaving == "Cap" ? cap : rune;
                moves.Withdrew(going.Key, ItemBlob.Restore(going.Row.Blob, _book.Store.StatIdBits));

                Assert.True(Page.BeginCarry(rune));
                Assert.False(Page.WouldSocket(cap));

                Assert.True(Page.SocketCarried(cap));

                Assert.Equal("Saving. Try again.", _book.Session.Status);
                Assert.Equal(1, asked);
                Assert.False(Page.IsCarrying);
                Assert.Equal(held, _book.Holds("New folder"));
                Assert.Empty(Stored("Cap").SocketedItems);
            }
            finally
            {
                moves.SaveNow = saveNow;
            }
        });
    }

    [CorpusFact]
    public void A_rune_carried_onto_a_card_with_an_empty_socket_goes_into_it_and_leaves_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        program.On(() =>
        {
            int held = _book.Holds("New folder");
            var cap = VaultBook.Card(Page, "Cap");

            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "El Rune")));
            Assert.True(Page.WouldSocket(cap));
            Page.HoverCard(cap);
            Assert.True(cap.IsSocketTarget);

            Assert.True(Page.SocketCarried(cap));
            Assert.False(Page.IsCarrying);
            Assert.False(cap.IsSocketTarget);
            Assert.Equal(held - 1, _book.Holds("New folder"));
        });

        _book.Until(() => !VaultBook.HasCard(Page, "El Rune"), "the rune's card gone");
        program.On(() =>
        {
            var cap = Stored("Cap");
            Assert.Single(cap.SocketedItems);
            Assert.Equal("r01", cap.SocketedItems[0].Code.Trim());
            Assert.Equal(2, cap.TotalNumberOfSockets);
        });
    }

    [CorpusFact]
    public void One_of_a_stack_goes_in_and_the_stack_is_one_less()
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        program.On(() =>
        {
            int held = _book.Holds("New folder");
            var cap = VaultBook.Card(Page, "Cap");
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "Tir Rune")));
            Assert.True(Page.SocketCarried(cap));

            // The stack is there still: nothing left the folder, and the stack holds two.
            Assert.Equal(held, _book.Holds("New folder"));
            Assert.Equal(2, Stacks.Shown(Stored("Tir Rune").Quantity));
            Assert.Single(Stored("Cap").SocketedItems);
        });
    }

    [CorpusFact]
    public void The_socket_guard_and_what_is_no_socketable_leave_the_card_as_it_is()
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        program.On(() =>
        {
            var cap = VaultBook.Card(Page, "Cap");

            // A ring: not this gesture's at all.
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "Ring")));
            Assert.False(Page.WouldSocket(cap));
            Assert.False(Page.SocketCarried(cap));
            Page.EndCarry();
            Assert.Empty(Stored("Cap").SocketedItems);

            // The guard on: the card does not light, and the drop says so and sockets nothing.
            Rules.SocketGuard = true;
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "El Rune")));
            Assert.False(Page.WouldSocket(cap));
            Assert.True(Page.SocketCarried(cap));
            Assert.False(Page.IsCarrying);
            Assert.Empty(Stored("Cap").SocketedItems);
            Assert.True(VaultBook.HasCard(Page, "El Rune"));
            Rules.SocketGuard = false;

            // A card onto itself, and onto one with no socket: neither.
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "El Rune")));
            Assert.False(Page.WouldSocket(VaultBook.Card(Page, "El Rune")));
            Assert.False(Page.WouldSocket(VaultBook.Card(Page, "Ring")));
            Page.EndCarry();
        });
    }

    [CorpusFact]
    public void The_tools_Equip_says_why_it_cannot_where_no_Items_page_is_beside()
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        program.On(() =>
        {
            // The vault on the left, the vault's Edit page beside it: no Items page for the item to be put on in.
            Page.ClickTileCommand.Execute(VaultBook.Card(Page, "Cap"));
            Assert.False(Page.CanEquip || Page.CanEquipMerc);
            Assert.Equal("Equip selected item" + Environment.NewLine + "Require inventory page open.", Page.EquipHint);
            Assert.StartsWith("Equip selected item on mercenary" + Environment.NewLine, Page.EquipMercHint);

            // Nothing selected: what it is for, and no more.
            Page.ClearSelection();
            Assert.False(Page.CanEquip);
            Assert.Equal("Equip selected item", Page.EquipHint);
        });
    }
}
