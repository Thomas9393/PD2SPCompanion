using PD2SPCompanion.Core.Localization;
using Xunit;
using static PD2SPCompanion.Core.Localization.Words;

namespace PD2SPCompanion.Tests.Diagnostics;

/// <summary>
/// The language framework: English is the code's own text, a pack's key is
/// the English with its holes named after the code, holes keep their values, plurals follow the pack's rule,
/// and a translation naming a hole the English does not have is dropped rather than shown broken.
/// The pack here holds test sentences only, so tests running beside these still read English.
/// </summary>
public sealed class LanguageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-lang-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Language.Use((LanguagePack?)null);
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private LanguagePack Pack(string plural, string strings)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "te-ST.json");
        File.WriteAllText(path, "{ \"code\": \"te-ST\", \"name\": \"Test\", \"plural\": \"" + plural + "\", \"strings\": {" + strings + "} }");
        return LanguagePack.Read(path);
    }

    [Fact]
    public void English_is_the_code_s_own_text()
    {
        Language.Use((LanguagePack?)null);
        int moved = 3;
        Assert.Equal("Test plain.", L("Test plain."));
        Assert.Equal("Test moved 3 items.", L($"Test moved {moved} items."));
        Assert.Equal("Test 1 apple", Plural(1, "Test 1 apple", "Test {n} apples"));
        int two = 2;
        Assert.Equal("Test 2 apples", Plural(two, "Test 1 apple", $"Test {two} apples"));
        Assert.Equal("A, B and C", Joined(["A", "B", "C"]));
        Assert.Equal("", Joined([]));
    }

    [Fact]
    public void A_pack_translates_by_the_named_holes()
    {
        Language.Use(Pack("en",
            "\"Test plain.\": \"Tëst pläin.\"," +
            "\"Test moved {moved} items to {tile.Name}.\": \"{tile.Name}: {moved} tëst.\"," +
            "\"Test gold {amount}.\": \"Tëst göld {amount}.\""));
        var tile = new { Name = "Shako" };
        int moved = 3, amount = 12345;

        Assert.Equal("Tëst pläin.", L("Test plain."));
        Assert.Equal("Shako: 3 tëst.", L($"Test moved {moved} items to {tile.Name}."));
        // The format stays the code's: the key names the hole, not its format.
        Assert.Equal($"Tëst göld {amount:N0}.", L($"Test gold {amount:N0}."));
        // A string the pack does not hold stays English.
        Assert.Equal("Test untranslated 3.", L($"Test untranslated {moved}."));
        // The strip reads a refusal back in English.
        Assert.Equal("Test plain.", Language.EnglishOf("Tëst pläin."));
    }

    [Fact]
    public void A_message_joined_from_sentences_reads_back_its_first_and_last()
    {
        Language.Use(Pack("en",
            "\"Test not moved.\": \"Tëst nöt mövëd.\"," +
            "\"Test {left} left.\": \"Tëst {left} lëft.\"," +
            "\"Test folder not found.\": \"Tëst földër nöt föund.\""));
        int left = 2;

        string shown = L("Test not moved.") + " Shako " + L($"Test {left} left.");
        Assert.Equal("Test not moved. Shako Test 2 left.", Language.EnglishOf(shown));
        Assert.Equal("Tëst untranslated. Test folder not found.",
                     Language.EnglishOf("Tëst untranslated. " + L("Test folder not found.")));
        // Nothing remembered at either end: the text as shown.
        Assert.Equal("Tëst other.", Language.EnglishOf("Tëst other."));
    }

    [Fact]
    public void A_translation_naming_a_hole_the_English_lacks_is_dropped()
    {
        var pack = Pack("en",
            "\"Test {count} left.\": \"Tëst {cuont} left.\"," +
            "\"Test kept {count}.\": \"Tëst kept {count}.\"," +
            "\"Test empty.\": \"\"");
        Language.Use(pack);
        int count = 4;

        Assert.Single(pack.Dropped);
        Assert.Equal("Test 4 left.", L($"Test {count} left."));
        Assert.Equal("Tëst kept 4.", L($"Test kept {count}."));
        Assert.Equal("Test empty.", L("Test empty."));
    }

    [Fact]
    public void Plurals_follow_the_pack_s_rule()
    {
        Language.Use(Pack("ru",
            "\"Test {n} files\": { \"one\": \"{n} файл\", \"few\": \"{n} файла\", \"many\": \"{n} файлов\" }"));

        string Files(int n) => Plural(n, $"Test {n} file", $"Test {n} files");
        string Fixed(int n) => Plural(n, "Test 1 file", $"Test {n} files");
        Assert.Equal("1 файл", Files(1));
        Assert.Equal("3 файла", Files(3));
        Assert.Equal("5 файлов", Files(5));
        Assert.Equal("11 файлов", Files(11));
        Assert.Equal("21 файл", Files(21));
        Assert.Equal("22 файла", Files(22));
        // A fixed singular beside a plural with holes still keys on the plural's holes.
        Assert.Equal("1 файл", Fixed(1));
        Assert.Equal("5 файлов", Fixed(5));
    }

    [Theory]
    [InlineData("en", 1, "one"), InlineData("en", 0, "other"), InlineData("en", 2, "other")]
    [InlineData("fr", 0, "one"), InlineData("fr", 1, "one"), InlineData("fr", 2, "other")]
    [InlineData("pl", 1, "one"), InlineData("pl", 4, "few"), InlineData("pl", 5, "many"), InlineData("pl", 12, "many"), InlineData("pl", 22, "few")]
    [InlineData("cs", 3, "few"), InlineData("cs", 5, "other")]
    [InlineData("ja", 1, "other")]
    [InlineData("ar", 0, "zero"), InlineData("ar", 2, "two"), InlineData("ar", 3, "few"), InlineData("ar", 11, "many"), InlineData("ar", 100, "other")]
    public void Plural_rules_by_family(string family, long count, string form) =>
        Assert.Equal(form, PluralRules.For(family)(count));

    [Fact]
    public void English_is_offered_first_and_a_missing_pack_is_English()
    {
        Assert.Equal(Language.English, Language.Available()[0].Code);
        Language.Use("zz-ZZ");
        Assert.Null(Language.Active);
        Assert.Equal(Language.English, Language.Code);
    }
}
