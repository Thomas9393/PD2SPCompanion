using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// PD2's special skins - the 5th Anniversary set of Season 12 first, the Special Edition set of Season 14 after it:
/// uniques that can drop, very rarely, looking different. The white Shako is Harlequin Crest's. Stored as
/// <c>item_shiny_appearance</c> (stat 270, 7 bits), which holds 5 on the anniversary ones and reads "5th
/// Anniversary Edition", 1 on the Special Editions and reads nothing (<see cref="Edition"/>).
/// <para>
/// <b>A skin is a picture of its own</b>, drawn as it is - not a tint (a white tint washed out the Shako's lining;
/// a screenshot). Season 12's five are the item's own picture name with
/// <see cref="SpriteSuffix"/> after it; Season 14's eighteen are named after the unique - `invburiza_se`, with no
/// `invburiza` of its own - so they are a list (<see cref="Pictures"/>). <b>Which uniques can drop skinned is a
/// list too</b> (<see cref="Uniques"/>) - nothing in the tables names them.
/// </para>
/// </summary>
public static class Skins
{
    public const string Stat = "item_shiny_appearance";

    /// <summary>
    /// What a skinned item's picture ends in. Season 12's are the item's own name with it - `invcap_se`, `inv8wsu_se`,
    /// `invchn_se`, `invkitu_se`, `invwolfboss_se` in pd2assets.mpq.
    /// </summary>
    public const string SpriteSuffix = "_se";

    /// <summary>
    /// The skin's tooltip line where the tables do not word it - Season 14's descfunc 30, with no string: the anniversary
    /// five (5) still read "5th Anniversary Edition" in orange, a Special Edition (1) reads <b>nothing</b>; both seen in
    /// game on a test Harlequin Crest and Buriza-Do Kyanon. S14's new <c>StrSpecialEdition</c> ("Alternative
    /// Edition") is not printed.
    /// </summary>
    public static string? Edition(int value) =>
        value == SpecialEdition ? null : StringTable.Current?.Get("StrAnniversaryEdition")?.Replace("%d", value.ToString()).Trim();

    /// <summary>The value every Season 14 skin holds (135 market listings); Season 12's hold 5.</summary>
    public const int SpecialEdition = 1;

    /// <summary>Whether this item carries a skin.</summary>
    public static bool Has(Item item) =>
        ItemStats.Every(item).Any(stat =>
            string.Equals(stat.Stat, Stat, StringComparison.OrdinalIgnoreCase) && stat.Value > 0);

    /// <summary>
    /// Season 14's Special Edition skins, by the unique's <c>uniqueitems.txt</c> index: the picture each is drawn with,
    /// from pd2assets.mpq (the 18 `_se` files Season 14 added, matched to the wiki's Cosmetics page).
    /// Ormus' Robes has three, one an element (<see cref="OrmusPicture"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Pictures =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Buriza-Do Kyanon"] = "invburiza_se",
            ["Chance Guards"] = "invchanceguards_se",
            ["Earthshifter"] = "invearthshifter_se",
            ["Fleshripper"] = "invfleshripper_se",
            ["Giantskull"] = "invgiantskull_se",
            ["The Grandfather"] = "invgrandfather_se",
            ["Guardian Angel"] = "invguardianangel_se",
            ["Headhunter's Glory"] = "invheadhunter_se",
            ["Hellslayer"] = "invhellslayer_se",
            ["Herald of Zakarum"] = "invheraldofzakarum_se",
            ["Medusa's Gaze"] = "invmedusa_se",
            ["Ormus' Robes"] = "invormus_f_se",
            ["Ribcracker"] = "invribcracker_se",
            ["The Spirit Shroud"] = "invspiritshroud_se",
            ["Stormspire"] = "invstormspire_se",
            ["String of Ears"] = "invstringofears_se",
            ["Venom Grip"] = "invvenomgrip_se",
            ["Wartraveler"] = "invwartravellers_se",
        };

    /// <summary>
    /// The uniques that can drop skinned, by their <c>uniqueitems.txt</c> index - from the wiki's Cosmetics page
    /// (Season 14's included), since no table names them. <b>Not derivable from the pictures</b>:
    /// Season 12's are per picture, and pictures are shared - Sparking Mail is `invchn` like Shaftstop, Peasant Crown
    /// `invcap` like Harlequin Crest. Update this at a new season; new `_se` files are the tell (the new-season
    /// checklist says so).
    /// </summary>
    public static readonly IReadOnlySet<string> Uniques =
        new HashSet<string>(
            ["Mang Song's Lesson", "Stormshield", "Shaftstop", "Harlequin Crest", "Fenris", .. Pictures.Keys],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this item is one of the uniques that can drop skinned.</summary>
    public static bool CanHave(Item item) =>
        item.Quality is ItemQuality.Unique
        && ItemPresenter.QualityIndex(item) is { } name
        && Uniques.Contains(name);

    /// <summary>
    /// The picture a skinned item is drawn with, or null where none is found: Season 14's by the unique, Season 12's
    /// as <paramref name="drawn"/> - the item's own picture - with <see cref="SpriteSuffix"/>. <paramref name="has"/>
    /// says whether the archives hold a picture.
    /// </summary>
    public static string? PictureOf(Item item, string drawn, Func<string, bool> has)
    {
        if (item.Quality is ItemQuality.Unique && ItemPresenter.QualityIndex(item) is { } name
            && Pictures.TryGetValue(name, out string? own))
        {
            string picture = name.Equals("Ormus' Robes", StringComparison.OrdinalIgnoreCase) ? OrmusPicture(item) : own;
            return has(picture) ? picture : null;
        }

        return has(drawn + SpriteSuffix) ? drawn + SpriteSuffix : null;
    }

    /// <summary>
    /// Ormus' Robes' skin of three - cold, fire, lightning: the element of the robe's random Sorceress skill (its
    /// <c>item_singleskill</c>). <b>Seen in game</b>: three test robes, Frost Nova blue, Nova gold, Meteor
    /// orange. Fire where the skill says no element.
    /// </summary>
    private static string OrmusPicture(Item item)
    {
        var skill = ItemStats.Every(item).FirstOrDefault(stat =>
            string.Equals(stat.Stat, "item_singleskill", StringComparison.OrdinalIgnoreCase));
        // The skill is the stat's param (encode 1): read as SkillId alone, every robe took fire.
        string element = (skill?.SkillId ?? skill?.Param) is { } id ? Characters.SkillTips.ElementOf(id) : "";
        return element.ToLowerInvariant() switch
        {
            "cold" => "invormus_c_se",
            "ltng" => "invormus_l_se",
            _ => "invormus_f_se",
        };
    }
}
