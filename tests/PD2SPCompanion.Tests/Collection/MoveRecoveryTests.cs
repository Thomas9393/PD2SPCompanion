using D2SLib;
using D2SLib.Model.Save;
using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// Saves cut short, each made by hand on a copy of a corpus character and a vault of the test's own (the rule:
/// a duplicate is acceptable, a loss is
/// not). What <see cref="MoveRecovery.Recover"/> says, what it puts back, and which moves it keeps for the next start.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MoveRecoveryTests(Game game) : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "pd2spc-recovery-" + Guid.NewGuid().ToString("N"));

    // Reading a character installs its layout, and the caller puts its own back.
    private readonly LayoutScope _layout = Globals.Use(Globals.Layout);

    public void Dispose()
    {
        _layout.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
    }

    private static string Values(Item item) =>
        item.Code.Trim() + ":" + string.Join(";", ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"));

    /// <summary>A copy of a character and an empty vault: the two sides of a move.</summary>
    private sealed class Scene : IDisposable
    {
        public string Save { get; }
        public Vault Vault { get; }
        public Item Item { get; }
        public uint Id { get; }
        public VaultCapture Captured { get; }

        public Scene(string scratch)
        {
            Save = Game.CopyOfCharacter(scratch);
            Vault = Vault.Open(Path.Combine(scratch, "v.pd2lib"), Game.Season, 10);

            var (character, _) = SaveVerifier.Load(Save);
            Item = character.PlayerItemList.Items.First(each => !each.IsCompact && each.Mode != ItemMode.Equipped);
            Id = Item.Id;
            Captured = Vault.Capture(Item, out string? refused) ?? throw new InvalidOperationException(refused);
        }

        public void Dispose() => Vault.Dispose();

        public long InFile() => SaveCounts.Read(Save).Count(Id, Item.Code);

        public long Held() => Vault.CountHeld(Id, null);

        public long Purse() => SaveCounts.Read(Save).Purse(MoveRecovery.CarriedPurse);

        /// <summary>The save written with the item, or without it: the file's side of a move, reached.</summary>
        public void WriteFile(bool withItem)
        {
            var (fresh, _) = SaveVerifier.Load(Save);
            var list = fresh.PlayerItemList;
            list.Items.RemoveAll(each => !each.IsCompact && each.Id == Id);
            if (withItem)
                list.Items.Add(Item);
            list.Count = (ushort)list.Items.Count;
            File.WriteAllBytes(Save, SaveWriter.Prepared(fresh));
        }

        /// <summary>The save written with this much in the purse.</summary>
        public void WritePurse(long gold)
        {
            var (fresh, _) = SaveVerifier.Load(Save);
            fresh.Attributes.Stats[AttributeEditor.Carried] = (int)gold;
            File.WriteAllBytes(Save, SaveWriter.Prepared(fresh));
        }

        public PendingMove StoreMove(byte[]? blob = null) => new(MoveRecovery.Store, Save, "Tester", "Test item", Id, Item.Code,
            blob ?? Captured.Blob, "c", null, null, "", 0, InFile(), InFile() - 1, Held(), Held() + 1);

        public PendingMove WithdrawMove(VaultRow row) => new(MoveRecovery.Withdraw, Save, "Tester", "Test item", Id, Item.Code,
            row.Blob, "c", row.Key, row.FolderId, "", 0, InFile(), InFile() + 1, 1, 0);

        public PendingMove GoldMove(long amount) => new(MoveRecovery.Gold, Save, "Tester", "gold", null, "", null, "c", null, null,
            MoveRecovery.CarriedPurse, amount, Purse(), Purse() - amount, Vault.GoldWrites(), Vault.GoldWrites() + 1);

        /// <summary>The item in the vault, its row: what a withdrawal starts from.</summary>
        public VaultRow Stored(long? folder = null)
        {
            long key = Vault.AddCaptured([(Captured, folder)])[0];
            return Vault.RowsOf([key])[0];
        }
    }

    /// <summary>A journal's batches are stamped to the millisecond: two written at once would be one.</summary>
    private static long JournalAlone(Vault vault, params PendingMove[] moves)
    {
        Thread.Sleep(2);
        return vault.JournalMoves(moves);
    }

    [CorpusFact]
    public void A_store_cut_short_after_the_vault_is_in_both_said_and_left()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long inFile = scene.InFile();
        scene.Vault.JournalMoves([scene.StoreMove()]);
        scene.Vault.AddCaptured([(scene.Captured, null)]);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("in both", Assert.Single(said));
        Assert.Equal(1, scene.Held());
        Assert.Equal(inFile, scene.InFile());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_store_with_the_file_written_first_is_put_back_in_the_vault_whole()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long inFile = scene.InFile();
        scene.Vault.JournalMoves([scene.StoreMove()]);
        scene.WriteFile(withItem: false);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("put back", Assert.Single(said));
        Assert.Equal(inFile - 1, scene.InFile());
        Assert.Equal(1, scene.Held());
        Assert.Empty(scene.Vault.Journal());

        var row = Assert.Single(scene.Vault.RowsOf(scene.Vault.AllKeys()));
        Assert.Equal(scene.Id, row.OriginId);
        Assert.Equal(Values(scene.Item), Values(ItemBlob.Restore(row.Blob, 10)));
    }

    [CorpusFact]
    public void A_withdrawal_cut_short_after_the_file_is_in_both_and_the_row_stays()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        var row = scene.Stored();
        scene.WriteFile(withItem: false);
        long inFile = scene.InFile();

        scene.Vault.JournalMoves([scene.WithdrawMove(row)]);
        scene.WriteFile(withItem: true);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("in both", Assert.Single(said));
        Assert.True(scene.Vault.HasRow(row.Key));
        Assert.Equal(1, scene.Held());
        Assert.Equal(inFile + 1, scene.InFile());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_withdrawal_with_the_row_removed_first_goes_back_to_its_folder()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long folder = scene.Vault.CreateFolder(null, "Kept");
        var row = scene.Stored(folder);
        scene.WriteFile(withItem: false);
        long inFile = scene.InFile();

        scene.Vault.JournalMoves([scene.WithdrawMove(row)]);
        scene.Vault.Remove([row.Key]);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("put back", Assert.Single(said));
        Assert.Equal(inFile, scene.InFile());
        var back = Assert.Single(scene.Vault.RowsOf(scene.Vault.AllKeys()));
        Assert.Equal(folder, back.FolderId);
        Assert.Equal(scene.Id, back.OriginId);
        Assert.Equal(Values(scene.Item), Values(ItemBlob.Restore(back.Blob, 10)));
    }

    [CorpusFact]
    public void A_store_with_no_blob_says_the_loss_and_is_cleared()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        scene.Vault.JournalMoves([scene.StoreMove() with { Blob = null }]);
        scene.WriteFile(withItem: false);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("not in", Assert.Single(said));
        Assert.Equal(0, scene.Held());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_compact_item_is_counted_by_its_code_and_put_back_by_its_blob()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        var (character, _) = SaveVerifier.Load(scene.Save);
        var compact = character.PlayerItemList.Items.First(each => each.IsCompact);
        var captured = scene.Vault.Capture(compact, out string? refused) ?? throw new InvalidOperationException(refused);
        string code = compact.Code;

        long inFile = SaveCounts.Read(scene.Save).Count(null, code);
        Assert.True(inFile > 0);
        scene.Vault.JournalMoves([new PendingMove(MoveRecovery.Store, scene.Save, "Tester", "Test gem", null, code, captured.Blob, "c", null, null,
            "", 0, inFile, inFile - 1, 0, 1)]);

        // Written without one copy of it.
        character.PlayerItemList.Items.Remove(compact);
        character.PlayerItemList.Count = (ushort)character.PlayerItemList.Items.Count;
        File.WriteAllBytes(scene.Save, SaveWriter.Prepared(character));

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("put back", Assert.Single(said));
        Assert.Equal(1, scene.Vault.CountHeld(null, captured.Blob));
    }

    [CorpusFact]
    public void Gold_into_the_vault_cut_short_in_each_place()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long purse = scene.Purse();
        Assert.True(purse >= 100, $"only {purse} gold on the corpus character");

        // The vault written, the purse not: in both, said.
        scene.Vault.JournalMoves([scene.GoldMove(100)]);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, 100)]);
        Assert.Contains("in both", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(100, scene.Vault.GoldBalance());
        Assert.Equal(purse, scene.Purse());
        Assert.Empty(scene.Vault.Journal());

        // Neither written: nothing to say.
        scene.Vault.JournalMoves([scene.GoldMove(100)]);
        Assert.Empty(MoveRecovery.Recover(scene.Vault));
        Assert.Equal(100, scene.Vault.GoldBalance());
        Assert.Empty(scene.Vault.Journal());

        // The purse written, the vault not: the wrong order, a loss, put back.
        scene.Vault.JournalMoves([scene.GoldMove(100)]);
        scene.WritePurse(purse - 100);
        Assert.Contains("put back", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(200, scene.Vault.GoldBalance());
        Assert.Equal(purse - 100, scene.Purse());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void Gold_out_of_the_vault_cut_short_in_each_place()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long purse = scene.Purse();
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, 500)]);

        // The vault written first, the purse not: the wrong order, a loss, paid back.
        scene.Vault.JournalMoves([scene.GoldMove(-60)]);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, -60)]);
        Assert.Contains("put back", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(500, scene.Vault.GoldBalance());
        Assert.Equal(purse, scene.Purse());

        // The purse written, the vault not: in both, said.
        scene.Vault.JournalMoves([scene.GoldMove(-60)]);
        scene.WritePurse(purse + 60);
        Assert.Contains("in both", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(500, scene.Vault.GoldBalance());
        Assert.Equal(purse + 60, scene.Purse());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_shared_stash_is_counted_by_its_own_purse_and_items()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        string stash = Path.Combine(_scratch, "pd2_shared.stash");
        File.Copy(Path.Combine(Game.Corpus, "saves", "pd2_shared.stash"), stash);
        var read = SharedStash.Read(stash);
        long held = read.Gold;
        var item = read.Items.Items.First(each => !each.IsCompact);
        var captured = scene.Vault.Capture(item, out string? refused) ?? throw new InvalidOperationException(refused);
        long copies = SaveCounts.Read(stash).Count(item.Id, item.Code);
        Assert.True(copies > 0);

        // Gold out, the vault written first; and a store with the stash written first. One save's two moves.
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.SharedPurse, 500)]);
        long writes = scene.Vault.GoldWrites();
        scene.Vault.JournalMoves([
            new PendingMove(MoveRecovery.Gold, stash, "", "gold", null, "", null, "c", null, null,
                MoveRecovery.SharedPurse, -60, held, held + 60, writes, writes + 1),
            new PendingMove(MoveRecovery.Store, stash, "", "Stash item", item.Id, item.Code, captured.Blob, "c", null, null,
                "", 0, copies, copies - 1, 0, 1),
        ]);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.SharedPurse, -60)]);
        read.Items.Items.Remove(item);
        File.WriteAllBytes(stash, read.Write());

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Equal(2, said.Count);
        Assert.All(said, line => Assert.Contains("put back", line));
        Assert.Equal(500, scene.Vault.GoldBalance());
        Assert.Equal(1, scene.Vault.CountHeld(item.Id, null));
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_save_that_finished_but_was_not_cleared_says_nothing()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        scene.Vault.JournalMoves([scene.StoreMove()]);
        scene.Vault.AddCaptured([(scene.Captured, null)]);
        scene.WriteFile(withItem: false);

        Assert.Empty(MoveRecovery.Recover(scene.Vault));
        Assert.Equal(1, scene.Held());
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_batch_cleared_is_nothing_to_check()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        long batch = scene.Vault.JournalMoves([scene.StoreMove()]);
        scene.Vault.ClearJournal(batch);
        scene.WriteFile(withItem: false);

        Assert.Empty(MoveRecovery.Recover(scene.Vault));
        Assert.Equal(0, scene.Held());
    }

    [CorpusFact]
    public void A_file_there_but_unreadable_keeps_its_moves_for_the_next_start()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        byte[] whole = File.ReadAllBytes(scene.Save);
        scene.Vault.JournalMoves([scene.StoreMove()]);
        File.WriteAllBytes(scene.Save, whole[..64]);

        Assert.Contains("not checked", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Single(scene.Vault.Journal());

        File.WriteAllBytes(scene.Save, whole);
        Assert.Empty(MoveRecovery.Recover(scene.Vault));
        Assert.Empty(scene.Vault.Journal());
    }

    [CorpusFact]
    public void A_file_gone_is_said_and_cleared()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        scene.Vault.JournalMoves([scene.StoreMove()]);
        File.Delete(scene.Save);

        Assert.Contains("not checked", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Empty(scene.Vault.Journal());
    }

    /// <summary>
    /// What could not be put back stays in the journal, the only copy of it, for a later build that may read it - and only
    /// its batch: another save's moves are done with.
    /// </summary>
    [CorpusFact]
    public void A_refused_put_back_keeps_its_batch_and_only_its_batch()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        byte[] unreadable = [1, 2, 3, 4, 5];

        long kept = JournalAlone(scene.Vault, scene.StoreMove(unreadable));
        long other = JournalAlone(scene.Vault, scene.GoldMove(100));
        Assert.NotEqual(kept, other);
        scene.WriteFile(withItem: false);

        var said = MoveRecovery.Recover(scene.Vault);

        Assert.Contains("not put back", Assert.Single(said));
        Assert.Equal(0, scene.Held());

        var left = Assert.Single(scene.Vault.Journal());
        Assert.Equal(kept, left.Batch);
        Assert.Equal(unreadable, left.Blob);

        // Said again at the next start, and still kept.
        Assert.Contains("not put back", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(unreadable, Assert.Single(scene.Vault.Journal()).Blob);
    }

    /// <summary>
    /// A batch kept comes back at every start. A row put back has a new key, and gold paid back leaves the vault's write
    /// count where it stood - so what its other moves had put right must not be put right again, one more copy a start.
    /// </summary>
    [CorpusFact]
    public void What_a_kept_batch_put_right_is_not_put_right_again()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, 500)]);
        var row = scene.Stored();
        scene.WriteFile(withItem: false);

        // One save: a withdrawal whose row went first, gold out whose vault side went first, and a store refused for good.
        var (other, _) = SaveVerifier.Load(scene.Save);
        var refusedItem = other.PlayerItemList.Items.First(each => !each.IsCompact && each.Mode != ItemMode.Equipped && each.Id != scene.Id);
        long copies = SaveCounts.Read(scene.Save).Count(refusedItem.Id, refusedItem.Code);
        scene.Vault.JournalMoves([
            scene.WithdrawMove(row),
            scene.GoldMove(-60),
            new PendingMove(MoveRecovery.Store, scene.Save, "Tester", "Refused item", refusedItem.Id, refusedItem.Code, [1, 2, 3, 4, 5], "c", null, null,
                "", 0, copies, copies - 1, 0, 1),
        ]);
        scene.Vault.Remove([row.Key]);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, -60)]);
        other.PlayerItemList.Items.Remove(refusedItem);
        other.PlayerItemList.Count = (ushort)other.PlayerItemList.Items.Count;
        File.WriteAllBytes(scene.Save, SaveWriter.Prepared(other));

        Assert.Equal(3, MoveRecovery.Recover(scene.Vault).Count);
        Assert.Equal(1, scene.Held());
        Assert.Equal(500, scene.Vault.GoldBalance());

        // The next start, and the one after: only the refused move is left to say.
        for (int start = 0; start < 2; start++)
        {
            Assert.Contains("not put back", Assert.Single(MoveRecovery.Recover(scene.Vault)));
            Assert.Equal(1, scene.Held());
            Assert.Equal(500, scene.Vault.GoldBalance());
            Assert.Single(scene.Vault.Journal());
        }
    }

    /// <summary>The same with a file there but not read: the other file's moves are not put right twice.</summary>
    [CorpusFact]
    public void A_batch_kept_for_an_unread_file_does_not_put_the_other_files_moves_right_again()
    {
        if (!game.Available)
            return;

        using var scene = new Scene(_scratch);
        string stash = Path.Combine(_scratch, "pd2_shared.stash");
        File.Copy(Path.Combine(Game.Corpus, "saves", "pd2_shared.stash"), stash);
        long held = SharedStash.Read(stash).Gold;

        scene.Vault.RecordGold([new GoldMove(MoveRecovery.SharedPurse, 500)]);
        long writes = scene.Vault.GoldWrites();
        byte[] whole = File.ReadAllBytes(stash);

        // Gold out of the vault into the character's purse, the vault written first; and a store from the stash, being
        // written by the game this moment.
        scene.Vault.JournalMoves([
            scene.GoldMove(-60),
            new PendingMove(MoveRecovery.Store, stash, "", "Stash item", 1, "abc", [1, 2, 3], "c", null, null, "", 0, 1, 0, 0, 1),
        ]);
        scene.Vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, -60)]);
        File.WriteAllBytes(stash, whole[..64]);

        var said = MoveRecovery.Recover(scene.Vault);
        Assert.Equal(2, said.Count);
        Assert.Equal(500, scene.Vault.GoldBalance());
        Assert.True(scene.Vault.GoldWrites() >= writes + 1);

        Assert.Contains("not checked", Assert.Single(MoveRecovery.Recover(scene.Vault)));
        Assert.Equal(500, scene.Vault.GoldBalance());
    }
}
