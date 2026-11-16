namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Where a speed stat buys a frame: an animation takes a whole number of frames, so more of the stat does
/// nothing until it shortens it by one. Counted from the installed game's own animations
/// (<see cref="AnimData"/>) by the engine's arithmetic - no row is written down in the program.
/// <para>
/// <b>Cast rate, hit recovery</b>: the stat counts for less the more there is - floor(120 x v / (120 + v)) - on
/// top of the mode's base (casting 100 and at most 75 more; hit recovery 50), and the animation takes
/// ceil(256 x frames / floor(speed x that / 100)) - 1. The Amazon's rows came out as the game's own page lists
/// them (7 / 14 / 22 / 32 / 48 / 68 / 99 / 152 and 6 / 13 / 20 / 32 / 52 / 86 /
/// 174 / 600).
/// </para>
/// <para>
/// <b>Attack speed</b>, as the game's own page counts it (PD2's stat page, read for how it counts):
/// the animation is sped by the <i>attack rate</i> - 100, less the
/// weapon's speed, plus what skills add - and by the items' attack speed counted for less, floor(120 x v /
/// (120 + v)); together between 15 and 175, 30 less for a skill that is a sequence. At a speed the attack takes
/// ceil((frames - start) x 256 / floor(animation speed x that / 100)) - 1; the item speed a step needs is
/// ceil(120 x e / (120 - e)), e being the steps above the attack rate. Proven on a sequence (Frenzy, sequence
/// of 17 frames, attack rate 132: "0 / 6 / 14 / 24 / 39 / 60 / 95 / 163", a screenshot).
/// </para>
/// </summary>
public static class Breakpoints
{
    /// <summary>
    /// A row: each amount of the stat that buys a frame with the frames it leaves, from 0 up, and what the
    /// character has. <paramref name="Hits"/>, for a skill that strikes more than once in one go, is each point's
    /// frames strike by strike - the game's "8 (9|6|10)".
    /// </summary>
    public sealed record Row(IReadOnlyList<(int Value, int Frames)> Points, int Current, IReadOnlyList<IReadOnlyList<int>>? Hits = null)
    {
        /// <summary>The point the character is at - the last it has enough for - or -1 for an empty row.</summary>
        public int Reached => Points.Count == 0 ? -1 : Math.Max(0, Points.ToList().FindLastIndex(point => point.Value <= Current));
    }

    /// <summary>A mode's rule: the animation it is counted on, the speed it has before the stat, and the most the stat adds.</summary>
    public sealed record Rule(string Mode, int Base, int? Cap);

    public static readonly Rule CastRate = new("SC", 100, 75);
    public static readonly Rule HitRecovery = new("GH", 50, null);

    /// <summary>
    /// Blocking: the class's block animation from 50, as
    /// hit recovery - every row of PD2's wiki table (Faster Block Rate) is it: the Sorceress's 5 frames 9 at nothing,
    /// the Amazon's one-handed swing (speed 88) 17, the bear's 12. With Holy Shield up a Paladin blocks from 100: 2.
    /// </summary>
    public static readonly Rule BlockRate = new("BL", 50, null);
    public static readonly Rule HolyShieldBlockRate = new("BL", 100, null);

    /// <summary>Every amount of the stat that buys a frame under a rule, from 0, with the frames each leaves.</summary>
    public static IReadOnlyList<(int Value, int Frames)> Of(AnimData.Animation animation, Rule rule, int upTo = 1000)
    {
        var found = new List<(int, int)>();
        int last = -1;

        for (int value = 0; value <= upTo; value++)
        {
            int frames = FramesAt(animation, rule, value);
            if (frames != last)
                found.Add((value, frames));
            last = frames;
        }

        return found;
    }

    /// <summary>The frames an animation takes with this much of the stat.</summary>
    public static int FramesAt(AnimData.Animation animation, Rule rule, int value)
    {
        int effective = 120 * value / (120 + value);
        if (rule.Cap is { } cap)
            effective = Math.Min(effective, cap);

        int speed = animation.Speed * (rule.Base + effective) / 100;
        return speed <= 0 ? int.MaxValue : (int)Math.Ceiling(256.0 * animation.Frames / speed) - 1;
    }

    /// <summary>The least and the most an attack's animation is sped, in percent.</summary>
    public const int SlowestAttack = 15, FastestAttack = 175;

    /// <summary>
    /// Charge's row, which the game's page counts its own way: the last 8 frames of the run, sped by the attack
    /// rate and the skill's own <c>par6</c> with no penalty for a sequence, up to 256% - "IAS (Frames): 0 / 14 /
    /// 40 (9) / 95 / 250" at attack rate 110 with Charge's 65 (a level 96 Paladin, a screenshot).
    /// </summary>
    public static IReadOnlyList<(int Value, int Frames)> ChargeRow(int attackRate, int skillSpeed) =>
        AttackRow(frames: 8, animationSpeed: 100, start: 0, attackRate + skillSpeed, sequence: false, fastest: 256);

    /// <summary>What a sequence skill's speed is less by.</summary>
    public const int SequencePenalty = 30;

    /// <summary>
    /// An attack's row: the item attack speed at which it loses a frame, from 0. <paramref name="frames"/> and
    /// <paramref name="animationSpeed"/> are the animation's (a sequence's frames, at speed 256),
    /// <paramref name="start"/> the frames it skips at its start, <paramref name="attackRate"/> the character's -
    /// 100, less the weapon's speed, plus what skills add - and <paramref name="sequence"/> whether the skill is
    /// one.
    /// </summary>
    public static IReadOnlyList<(int Value, int Frames)> AttackRow(int frames, int animationSpeed, int start, int attackRate, bool sequence,
                                                                   int fastest = FastestAttack)
    {
        var found = new List<(int, int)>();
        int from = attackRate - (sequence ? SequencePenalty : 0);
        if (from > fastest)
            from = fastest - 1;
        if (animationSpeed == 0)
            animationSpeed = 256;

        int last = -1;
        for (int sped = from; sped <= fastest; sped++)
        {
            int taken = AttackFrames(frames, animationSpeed, start, sped, fastest);
            if (taken == last)
                continue;

            int steps = sped - from;
            if (steps >= 120)
                break;

            found.Add(((int)Math.Ceiling(120.0 * steps / (120 - steps)), taken));
            last = taken;
        }

        return found;
    }

    /// <summary>
    /// The row of a skill that strikes more than once in one go - Fend, Zeal, Strafe, Dragon Talon: after each
    /// strike the animation is put back <paramref name="rollback"/> percent of the way and runs on to the frame
    /// it acts on again, and after the last strike to its end. At each speed the frames each strike takes; a
    /// point where any of them changes. Counted as the game's own page counts it, in 256ths of a frame; proven
    /// on Fend - three strikes, put back 60%, an Amazon's two-handed thrust of 18 frames acting on the 11th,
    /// two start frames, attack rate 110: "0 / 4 / 8 (9, 6, 10) / 22 / 23 / 42 / 60 / 75 / 142" (SpearZon).
    /// </summary>
    public static (IReadOnlyList<(int Value, int Frames)> Points, IReadOnlyList<IReadOnlyList<int>> Hits) MultiHitRow(
        int strikes, int actsOn, int frames, int animationSpeed, int start, int attackRate, int rollback)
    {
        var points = new List<(int, int)>();
        var hits = new List<IReadOnlyList<int>>();
        if (strikes < 1)
            return (points, hits);

        int from = Math.Min(attackRate, FastestAttack - 1);
        if (animationSpeed == 0)
            animationSpeed = 256;

        int[]? last = null;
        for (int sped = from; sped <= FastestAttack; sped++)
        {
            int steps = sped - from;
            if (steps >= 120)
                break;

            int rate = animationSpeed * Math.Clamp(sped, SlowestAttack, FastestAttack) / 100;
            if (rate <= 0)
                continue;

            int[] taken = StrikeFrames(strikes, actsOn, frames, start, rate, rollback);
            if (last is not null && taken.SequenceEqual(last))
                continue;

            points.Add(((int)Math.Ceiling(120.0 * steps / (120 - steps)), taken.Sum()));
            hits.Add(taken);
            last = taken;
        }

        return (points, hits);
    }

    /// <summary>The frames each strike of a multi-strike skill takes at an animation rate (256ths of a frame a tick).</summary>
    private static int[] StrikeFrames(int strikes, int actsOn, int frames, int start, int rate, int rollback)
    {
        // Where the animation stands, in 256ths, as a strike lands: the first tick at or past the acting frame.
        static int Reach(int from, int target, int rate) => from * 256 + (int)Math.Ceiling((target - from) * 256.0 / rate) * rate;

        var taken = new int[strikes];
        int landed = Reach(start, actsOn, rate);
        taken[0] = (int)Math.Ceiling((landed - start * 256) / (double)rate) + 1;

        int back = 0, at = 0;
        for (int strike = 1; strike < strikes; strike++)
        {
            // Put back so many percent of the way, in whole frames, and run to the acting frame again.
            back = (landed >> 8) * (100 - rollback) / 100;
            at = back * 256 + rate;
            landed = Reach(back, actsOn, rate);
            taken[strike] = 1;
            for (; at < landed; at += rate)
                taken[strike]++;
        }

        // After the last strike the animation runs out, two ticks short of its end.
        int end = Reach(back, frames, rate) - rate * 2;
        for (; at < end; at += rate)
            taken[^1]++;

        return taken;
    }

    /// <summary>The frames an attack takes with its animation sped by so many percent.</summary>
    public static int AttackFrames(int frames, int animationSpeed, int start, int sped, int fastest = FastestAttack)
    {
        int rate = animationSpeed * Math.Clamp(sped, SlowestAttack, fastest) / 100;
        return rate <= 0 ? int.MaxValue : (int)Math.Ceiling((frames - start) * 256.0 / rate) - 1;
    }

    /// <summary>What an amount of item attack speed counts for: less the more there is.</summary>
    public static int Effective(int itemSpeed) => 120 * itemSpeed / (120 + itemSpeed);
}
