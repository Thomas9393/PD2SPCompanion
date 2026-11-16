using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which bases can be ethereal at all. The
/// game makes no ethereal item on a base with <c>nodurability</c> set - every bow and crossbow, the throwing potions,
/// the quivers: 0 ethereal among 799 bows and crossbows on the S13 ladder, where one sword in ten is. One way stays
/// open: a base whose tier family has durability somewhere is cubed up ethereal - an ethereal Crystal Sword or
/// Dimensional Blade becomes an ethereal Phase Blade. So a base is asked across its family.
/// <para>
/// What a unique's or set piece's own row adds - indestructible, or ethereal always - is the grail's to say
/// (GrailCatalog.HasEtherealDrop): the row, not the item's current stats, which a corruption can have added to.
/// </para>
/// </summary>
public static class Ethereals
{
    /// <summary>Whether an item on this base can be ethereal: gear, and a base its tier family gives durability somewhere.</summary>
    public static bool BaseCanBe(string? code)
    {
        string id = code?.Trim() ?? "";
        if (id.Length == 0 || !ItemTypes.IsGear(ItemTypes.TypeOf(id)))
            return false;

        return (ItemTier.Family(id) ?? [id]).Any(member => !NoDurability(member));
    }

    /// <summary>What an item can be: ethereal or not by chance, never ethereal, or always.</summary>
    public enum Form { Either, Never, Always }

    /// <summary>
    /// What this item, as it stands, can be. Past its base (<see cref="BaseCanBe"/>):
    /// <list type="bullet">
    /// <item>A unique's or set piece's row decides: ethereal always (Ethereal Edge, Spectral Plate and three more
    /// uniques; not Sazabi's Cobalt Redeemer, a set piece), else never where it is indestructible (Tyrael's Might, Stormshield and twelve more),
    /// else never where the row's own base has no durability - Lightsabre and Azurewrath drop on a Phase Blade, and
    /// nothing cubes one up into them. Ginther's Rift drops on a Dimensional Blade and can.</item>
    /// <item>Any other item on a base with no durability got there by the cube: an upgrade, which takes unique, set,
    /// rare and crafted (BaseUpgrades.GameUpgrades) - so no white, superior, magic or runeword ethereal Phase Blade.
    /// The ladders bear it out: 20 runeword Phase Blades and a rare, crafted and white one, none ethereal.</item>
    /// </list>
    /// </summary>
    public static Form Of(Item item)
    {
        string code = item.Code?.Trim() ?? "";
        if (!BaseCanBe(code))
            return Form.Never;

        if (ItemPresenter.QualityRow(item) is { } row)
        {
            var functions = Enumerable.Range(1, 12)
                .Select(n => row.Get($"prop{n}")?.Value?.Trim())
                .Where(prop => prop is { Length: > 0 })
                .Select(prop => AffixRanges.FunctionOf(prop!))
                .ToList();

            // On a set piece's row the property does nothing: three real Sazabi's Cobalt Redeemers, none ethereal (one
            // on the S14 ladder, two in the S12 pack; regress full), and the wiki's page says no ethereal
            // - its indestructible decides, below. The unique rows' does: every
            // Ghostflame, Shadow Killer and Spectral Plate on the ladders is ethereal.
            if (functions.Contains(AffixRanges.EtherealFunction) && item.Quality != ItemQuality.Set)
                return Form.Always;
            if (functions.Contains(IndestructibleFunction) || NoDurability(ItemPresenter.RowBase(item)))
                return Form.Never;
            return Form.Either;
        }

        return NoDurability(code) && !(BaseUpgrades.GameUpgrades(item.Quality) && !item.IsRuneword)
            ? Form.Never
            : Form.Either;
    }

    /// <summary>The <c>indestruct</c> property's function (AffixRanges' "20": <c>item_indesctructible</c>).</summary>
    private const string IndestructibleFunction = "20";

    /// <summary>
    /// The base's <c>nodurability</c> flag - what keeps it from ever being ethereal. Not "it has no durability": a
    /// Short Bow has 20 and the flag (ItemTypes.IsGear).
    /// </summary>
    public static bool NoDurability(string? code)
    {
        try
        {
            return D2SLib.Core.TXT.ItemsTXT.GetByCode(code?.Trim() ?? "")?.Get("nodurability")?.Value?.Trim() == "1";
        }
        catch (Exception)
        {
            // A table without the column - misc - asked by name throws rather than answering null.
            return false;
        }
    }
}
