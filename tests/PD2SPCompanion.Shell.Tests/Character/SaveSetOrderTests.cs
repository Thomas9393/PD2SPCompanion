using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The order the save set writes its files in: the one gaining an item before the one
/// losing it, decided at each save from what moved, whichever way it moved and whoever joined first - so a write that
/// fails leaves a duplicate, not a loss. The files here are pretended; what the real ones say of themselves is
/// <c>GrailCompendiumTests</c>'.
/// </summary>
public sealed class SaveSetOrderTests
{
    private sealed class File(string name, List<string> written, int order = 0) : ISaveParticipant
    {
        public string What => name;
        public bool HasChanges => true;
        public int Order => order;
        public string? Fails { get; init; }
        public SaveHoldings? Moved { get; set; }

        public void UseMyLayout() { }
        public string? Prepare() => null;
        public void Revert() { }
        public SaveHoldings? Holdings() => Moved;

        public string? Write(string moment, SaveReport report)
        {
            if (Fails is not null)
                return Fails;

            written.Add(name);
            report.Backup = name;
            return null;
        }
    }

    private static IReadOnlyDictionary<string, long> Holding(params string[] kinds) =>
        kinds.ToDictionary(kind => kind, _ => 1L);

    /// <summary>A file losing <paramref name="kind"/>, and one gaining it.</summary>
    private static SaveHoldings Loses(string kind) => new(Holding(kind), Holding());

    private static SaveHoldings Gains(string kind) => new(Holding(), Holding(kind));

    /// <summary>The report is of that save alone, and however far it got: the files that wrote are on it, and it is heard.</summary>
    [Fact]
    public void A_save_says_what_it_did_and_the_next_save_starts_afresh()
    {
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("first", written, order: 0));
        var fails = new File("second", written, order: 1) { Fails = "No room." };
        set.Takes(fails);
        var heard = new List<SaveReport>();
        set.Written += heard.Add;

        Assert.Equal("No room. first already written. Previous versions kept beside them.", set.Commit(out var cut));
        Assert.Equal("first", cut.Backup);
        Assert.Same(cut, Assert.Single(heard));

        // Nothing kept from the save before: a file that takes no copy says none.
        var quiet = new SaveSet();
        Assert.Null(quiet.Commit(out var none));
        Assert.Null(none.Backup);
    }

    [Fact]
    public void A_save_refused_before_any_write_is_heard_of_by_no_one()
    {
        var set = new SaveSet();
        set.Takes(new File("first", []));
        set.Refuse = _ => "Game running.";
        var heard = new List<SaveReport>();
        set.Written += heard.Add;

        Assert.Equal("Game running.", set.Commit());
        Assert.Empty(heard);
    }

    [Fact]
    public void The_file_gaining_an_item_goes_first_whichever_joined_first()
    {
        foreach (bool gainerFirst in new[] { true, false })
        {
            var written = new List<string>();
            var gaining = new File("gaining", written) { Moved = Gains("id 7") };
            var losing = new File("losing", written) { Moved = Loses("id 7") };
            var set = new SaveSet();
            set.Takes(gainerFirst ? gaining : losing);
            set.Takes(gainerFirst ? losing : gaining);

            Assert.Null(set.Commit());
            Assert.Equal(["gaining", "losing"], written);
        }
    }

    [Fact]
    public void A_gaining_file_that_fails_leaves_the_losing_file_unwritten()
    {
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("losing", written) { Moved = Loses("id 7") });
        set.Takes(new File("gaining", written) { Moved = Gains("id 7"), Fails = "No room." });

        Assert.Equal("No room.", set.Commit());
        Assert.Empty(written);
    }

    [Fact]
    public void Three_files_follow_each_item_where_they_move_on_in_a_line()
    {
        // One item from the stash to the compendium, another from the compendium to the character.
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("stash", written) { Moved = Loses("id 1") });
        set.Takes(new File("compendium", written) { Moved = new SaveHoldings(Holding("id 2"), Holding("id 1")) });
        set.Takes(new File("character", written) { Moved = Gains("id 2") });

        Assert.Null(set.Commit());
        Assert.Equal(["character", "compendium", "stash"], written);
    }

    [Fact]
    public void Nothing_moved_between_them_keeps_the_order_they_joined()
    {
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("first", written) { Moved = Gains("id 1") });
        set.Takes(new File("second", written) { Moved = Gains("id 2") });
        set.Takes(new File("third", written));

        Assert.Null(set.Commit());
        Assert.Equal(["first", "second", "third"], written);
    }

    [Fact]
    public void Items_moved_both_ways_are_all_written_in_the_order_they_joined()
    {
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("one", written) { Moved = new SaveHoldings(Holding("id 1"), Holding("id 2")) });
        set.Takes(new File("two", written) { Moved = new SaveHoldings(Holding("id 2"), Holding("id 1")) });

        Assert.Null(set.Commit());
        Assert.Equal(["one", "two"], written);
    }

    [Fact]
    public void The_vaults_own_order_stands_before_what_moved()
    {
        // The vault's gains and losses keep their places about the files, whatever the files say.
        var written = new List<string>();
        var set = new SaveSet();
        set.Takes(new File("vault's losses", written, order: 1) { Moved = Gains("id 1") });
        set.Takes(new File("file", written) { Moved = Loses("id 1") });
        set.Takes(new File("vault's gains", written, order: -1));

        Assert.Null(set.Commit());
        Assert.Equal(["vault's gains", "file", "vault's losses"], written);
    }
}
