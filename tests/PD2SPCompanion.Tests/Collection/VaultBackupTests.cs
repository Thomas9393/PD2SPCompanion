using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault's copies are held to a count (Vault.PruneBackups; they never were).
/// By count alone, each vault by its name - a copy is the last sight of something, and none stands for another.
/// And the copies no count reaches - those of a vault that is no more - go after a month (Vault.PruneOrphans), against
/// uncontrolled growth of data. Each season's mode has a folder of copies, so
/// softcore's and hardcore's "S14 Main" are two vaults, and a session's copies are counted
/// apart from the ones taken once; those from before the folders stay where they are, listed and thinned as they were.
/// </summary>
public class VaultBackupTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0);

    private static string Folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Copy(string folder, string vault, string why, DateTime at, string suffix = "") =>
        File.WriteAllText(Path.Combine(folder, $"{vault}.{why}.{at:yyyyMMdd-HHmmss}{suffix}.pd2lib"), "copy");

    private static string[] Left(string folder) =>
        [.. Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Fact]
    public void TheNewestOfEachVaultStay_AndEveryRecentOne()
    {
        string folder = Folder();
        try
        {
            // Five of one vault from days past, three of them on one day; two of another; three just taken.
            Copy(folder, "S13", "v5", Now.AddDays(-9));
            Copy(folder, "S13", "grail-forgotten", Now.AddDays(-3).AddHours(1));
            Copy(folder, "S13", "deleted", Now.AddDays(-3).AddHours(2));
            Copy(folder, "S13", "deleted", Now.AddDays(-3).AddHours(3));
            Copy(folder, "S13", "v6", Now.AddDays(-1));
            Copy(folder, "Item Library", "v13", Now.AddDays(-8));
            Copy(folder, "Item Library", "v14", Now.AddDays(-2));
            Copy(folder, "S13", "before-clean", Now.AddMinutes(-5));
            Copy(folder, "S13", "before-clean", Now.AddMinutes(-4));
            Copy(folder, "S13", "before-clean", Now.AddMinutes(-4), "-2");

            int removed = Vault.PruneCopies(folder, 2, null, Now);

            Assert.Equal(3, removed);
            string[] left = Left(folder);

            // The two newest older ones of S13 - the day's three deletions are not one day's worth.
            Assert.Contains(left, name => name.StartsWith("S13.v6."));
            Assert.Contains(left, name => name == $"S13.deleted.{Now.AddDays(-3).AddHours(3):yyyyMMdd-HHmmss}.pd2lib");
            Assert.DoesNotContain(left, name => name.StartsWith("S13.v5.") || name.StartsWith("S13.grail-forgotten."));
            Assert.DoesNotContain(left, name => name == $"S13.deleted.{Now.AddDays(-3).AddHours(2):yyyyMMdd-HHmmss}.pd2lib");

            // The other vault has its own count, and the quarter hour's are not counted at all.
            Assert.Equal(2, left.Count(name => name.StartsWith("Item Library.")));
            Assert.Equal(3, left.Count(name => name.StartsWith("S13.before-clean.")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CopiesOfAVaultThatIsNoMoreGoAfterAMonth_AndTheRetiredCollectionsWithThem()
    {
        string folder = Folder();
        try
        {
            // A vault there is, old copies and all; one that is no more, a copy within the month and two past it -
            // the one taken as it was deleted among them; a copy named while the vault's file was named whole.
            Copy(folder, "S13 Main", "v15", Now.AddDays(-200));
            Copy(folder, "S13 Main.pd2lib", "v15", Now.AddDays(-90));
            Copy(folder, "Softcore", "v9", Now.AddDays(-45));
            Copy(folder, "Softcore", "deleted", Now.AddDays(-31));
            Copy(folder, "Softcore", "before-merge", Now.AddDays(-29));
            File.WriteAllText(Path.Combine(folder, "mine.pd2lib"), "not a copy");
            Directory.CreateDirectory(Path.Combine(folder, $"Collections merged {Now.AddDays(-40):yyyyMMdd-HHmmss}"));
            Directory.CreateDirectory(Path.Combine(folder, $"Collections merged {Now.AddDays(-3):yyyyMMdd-HHmmss}"));
            Directory.CreateDirectory(Path.Combine(folder, "Kept by hand"));

            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "s13 main" };
            int removed = Vault.PruneOrphans(folder, live, Vault.OrphanAge, Now);

            Assert.Equal(3, removed);
            Assert.Equal(
                [
                    "S13 Main.pd2lib.v15." + Now.AddDays(-90).ToString("yyyyMMdd-HHmmss") + ".pd2lib",
                    "S13 Main.v15." + Now.AddDays(-200).ToString("yyyyMMdd-HHmmss") + ".pd2lib",
                    "Softcore.before-merge." + Now.AddDays(-29).ToString("yyyyMMdd-HHmmss") + ".pd2lib",
                    "mine.pd2lib",
                ],
                Left(folder));
            Assert.Equal(
                [$"Collections merged {Now.AddDays(-3):yyyyMMdd-HHmmss}", "Kept by hand"],
                Directory.GetDirectories(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            // Nothing more to take: a second pass removes nothing.
            Assert.Equal(0, Vault.PruneOrphans(folder, live, Vault.OrphanAge, Now));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OneVaultAlone_NoCount_AndWhatIsNotACopy()
    {
        string folder = Folder();
        try
        {
            Copy(folder, "S13", "v5", Now.AddDays(-9));
            Copy(folder, "S13", "v6", Now.AddDays(-8));
            Copy(folder, "Softcore", "v9", Now.AddDays(-9));
            Copy(folder, "Softcore", "v10", Now.AddDays(-8));
            File.WriteAllText(Path.Combine(folder, "mine.pd2lib"), "not a copy");
            Directory.CreateDirectory(Path.Combine(folder, "Collections merged 20261001-120000"));

            // No count kept: nothing goes.
            Assert.Equal(0, Vault.PruneCopies(folder, null, null, Now));
            Assert.Equal(5, Left(folder).Length);

            // One vault's, after a copy of it: the other's are not looked at.
            Assert.Equal(1, Vault.PruneCopies(folder, 1, "s13", Now));
            Assert.Equal(2, Left(folder).Count(name => name.StartsWith("Softcore.")));
            Assert.Single(Left(folder), name => name.StartsWith("S13."));

            // "Never" keeps none of the older ones - and never a file that is not a copy, nor a folder.
            Assert.Equal(3, Vault.PruneCopies(folder, 0, null, Now));
            Assert.Equal(["mine.pd2lib"], Left(folder));
            Assert.True(Directory.Exists(Path.Combine(folder, "Collections merged 20261001-120000")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AModesSessionsLeaveTheOtherModesCopiesOfTheSameNameAlone()
    {
        string folder = Folder();
        try
        {
            string soft = Path.Combine(folder, "S14", "Softcore"), hard = Path.Combine(folder, "S14", "Hardcore");
            Directory.CreateDirectory(soft);
            Directory.CreateDirectory(hard);

            for (int day = 1; day <= 5; day++)
                Copy(soft, "S14 Main", "session", Now.AddDays(-day));
            Copy(hard, "S14 Main", "session", Now.AddDays(-20));
            Copy(hard, "S14 Main", "session", Now.AddDays(-30));

            Assert.Equal(3, Vault.PruneCopies(folder, 2, null, Now));

            Assert.Equal(2, Left(soft).Length);
            Assert.Equal(2, Left(hard).Length);
            Assert.Equal(4, Vault.CopiesIn(folder).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASessionsCopiesDoNotPushOutTheOnesTakenOnce()
    {
        string folder = Folder();
        try
        {
            Copy(folder, "S14 Main", "cleared", Now.AddDays(-40));
            Copy(folder, "S14 Main", "v19", Now.AddDays(-50));
            Copy(folder, "S14 Main", "emptied", Now.AddDays(-60));
            for (int day = 1; day <= 4; day++)
                Copy(folder, "S14 Main", "session", Now.AddDays(-day));

            // Of the sessions' the newest two; of the others too, however many sessions came after.
            Assert.Equal(3, Vault.PruneCopies(folder, 2, "S14 Main", Now));

            string[] left = Left(folder);
            Assert.Equal(2, left.Count(name => name.Contains(".session.")));
            Assert.Contains(left, name => name.Contains(".cleared."));
            Assert.Contains(left, name => name.Contains(".v19."));
            Assert.DoesNotContain(left, name => name.Contains(".emptied."));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CopiesFromBeforeTheFoldersStayListedAndNothingElseBelowIsACopy()
    {
        string folder = Folder();
        try
        {
            // Two of the old layout at the top, one in a mode's folder; what lies in other folders is no copy of ours.
            Copy(folder, "S14 Main", "session", Now.AddDays(-2));
            Copy(folder, "S14 Main", "cleared", Now.AddDays(-1));
            string soft = Path.Combine(folder, "S14", "Softcore");
            Directory.CreateDirectory(soft);
            Copy(soft, "S14 Main", "session", Now.AddHours(-1));

            string retired = Path.Combine(folder, $"Collections merged {Now.AddDays(-3):yyyyMMdd-HHmmss}", "Softcore");
            Directory.CreateDirectory(retired);
            Copy(retired, "Default", "v15", Now.AddDays(-3));
            string byHand = Path.Combine(folder, "Kept by hand");
            Directory.CreateDirectory(byHand);
            Copy(byHand, "S14 Main", "session", Now.AddDays(-9));

            var copies = Vault.CopiesIn(folder);
            Assert.Equal(["session", "cleared", "session"], copies.Select(copy => copy.Why));
            Assert.Equal(Path.Combine(soft, $"S14 Main.session.{Now.AddHours(-1):yyyyMMdd-HHmmss}.pd2lib"), copies[0].Path);

            // Thinned as they were: the count reaches none of those left alone.
            Assert.Equal(3, Vault.PruneCopies(folder, 0, null, Now));
            Assert.Empty(Vault.CopiesIn(folder));
            Assert.Single(Left(retired));
            Assert.Single(Left(byHand));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AModesCopiesGoWithItsOwnVault_ThoughTheOtherModeHoldsTheName()
    {
        string folder = Folder();
        try
        {
            string soft = Path.Combine(folder, "S14", "Softcore"), hard = Path.Combine(folder, "S14", "Hardcore");
            Directory.CreateDirectory(soft);
            Directory.CreateDirectory(hard);
            Copy(soft, "S14 Main", "session", Now.AddDays(-40));
            Copy(hard, "S14 Main", "deleted", Now.AddDays(-40));
            Copy(folder, "S14 Main", "session", Now.AddDays(-40));

            // Softcore's is there; hardcore's was deleted. The old copy says no mode, so softcore's name keeps it.
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S14 Main", Path.Combine("S14", "Softcore", "S14 Main") };

            Assert.Equal(1, Vault.PruneOrphans(folder, live, Vault.OrphanAge, Now));
            Assert.Single(Left(soft));
            Assert.Empty(Left(hard));
            Assert.Single(Left(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheCopiesAreListedNewestFirst_EachWithItsVaultWhyAndWhen()
    {
        string folder = Folder();
        try
        {
            Copy(folder, "S13 Main", "deleted", Now.AddDays(-3));
            Copy(folder, "Default", "v15", Now.AddDays(-1));
            Copy(folder, "Default", "v15", Now.AddDays(-1), "-2");
            Copy(folder, "S13 Main", "cleared", Now.AddHours(-2));

            // What is no copy is left out: another file, a folder of retired collections, a staged import's.
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(folder, "Collections merged 20261007-084310"));
            Copy(folder, Vault.StagedPrefix + "0123abcd", "v15", Now);

            var copies = Vault.CopiesIn(folder);
            Assert.Equal(["S13 Main", "Default", "Default", "S13 Main"], copies.Select(copy => copy.Vault));
            Assert.Equal(["cleared", "v15", "v15", "deleted"], copies.Select(copy => copy.Why));
            Assert.Equal(Now.AddHours(-2), copies[0].At);
            Assert.All(copies, copy => Assert.True(File.Exists(copy.Path) && copy.Bytes == 4));

            Assert.Empty(Vault.CopiesIn(Path.Combine(folder, "none")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
