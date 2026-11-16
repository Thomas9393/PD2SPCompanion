using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An affix rolled before a patch closed it to a base stays what that build allowed: S14's second patch took one of the
/// Celestial rows off staves. A staff carrying it is flagged by this build's rules alone (Legitimacy.Judge) and silent
/// where the build before is kept (Legitimacy.Check, which asks the earlier builds too).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class AffixEarlierBuildTests(Game game)
{
    [GameFact]
    public void A_staff_with_a_Celestial_rolled_before_the_patch_is_silent_where_the_build_before_is_kept()
    {
        var builds = Game.EarlierAffixTables().ToList();
        if (!game.Available || builds.Count == 0)
            return;

        var earlier = builds[0];

        try
        {
            MagicAffixes.LoadEarlier([earlier]);

            // The Celestial the build before let a staff roll and this one does not.
            int closed = AffixNames.MagicIdsNamed(true, "Celestial")
                .FirstOrDefault(id =>
                {
                    var staff = Staff(id);
                    if (Legitimacy.Judge(staff).Count == 0)
                        return false;
                    using (MagicAffixes.Judging(0))
                        return Legitimacy.Judge(staff).Count == 0;
                });
            Assert.True(closed > 0, "no Celestial row was closed to staves by the patch");

            Assert.Empty(Legitimacy.Check(Staff(closed)));

            var rolledThen = Staff(closed);
            MagicAffixes.LoadEarlier([]);
            Assert.Contains(Legitimacy.Judge(rolledThen), finding => finding.Certainty == Certainty.Impossible);
        }
        finally
        {
            Game.UseEarlierBuilds();
        }
    }

    /// <summary>A rare war staff carrying prefix <paramref name="id"/> alone, rolled as the build before rolled it (Celestial is rare-only).</summary>
    private static Item Staff(int id)
    {
        var staff = ItemForge.BaseAs("6ws", ItemQuality.Rare, Game.Stamp, new Random(5)).Item;
        staff.ItemLevel = 99;
        Array.Clear(staff.MagicPrefixIds!);
        Array.Clear(staff.MagicSuffixIds!);
        ItemStats.Home(staff).Stats.Clear();
        using (MagicAffixes.Judging(0))
        {
            MagicAffixes.Apply(staff,
                new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, true)] = new(id, MagicAffixes.Roll(MagicAffixes.Find(id, prefix: true)!, new Random(3))) },
                new Random(3));
        }
        return staff;
    }
}
