namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// Paying for a Virtual Trading purchase out of the materials tab, the way a cash machine counts
/// out notes: the price is met with the fewest, largest pieces the player holds, and whatever is
/// paid over it comes back as change, in the largest pieces that fit.
/// <para>
/// Every figure is in HR, at the player's own price table. The player can hold any currency at a
/// count of their choosing - "no Vex" - and the rest is counted around it. The price itself is
/// theirs too: an honor system, nothing here judges it.
/// </para>
/// </summary>
public static class TradePayment
{
    /// <summary>A currency: what it is worth in HR, and how many the materials tab holds.</summary>
    public sealed record Currency(string Code, decimal Worth, int Stock);

    /// <summary>
    /// How a price is met.
    /// </summary>
    /// <param name="Paid">How many of each currency leave the materials tab.</param>
    /// <param name="Change">How many of each come back.</param>
    /// <param name="Short">
    /// What the materials tab cannot cover at all, in HR - above nought, the payment cannot be made.
    /// </param>
    /// <param name="Lost">
    /// Change smaller than the smallest currency, which cannot be given back - kept by the seller.
    /// </param>
    public sealed record Plan(
        IReadOnlyDictionary<string, int> Paid, IReadOnlyDictionary<string, int> Change,
        decimal PaidWorth, decimal ChangeWorth, decimal Short, decimal Lost)
    {
        /// <summary>What a currency's count moves by: less what is paid, plus what comes back.</summary>
        public int NetOf(string code) =>
            Change.GetValueOrDefault(code) - Paid.GetValueOrDefault(code);
    }

    /// <summary>
    /// Meets <paramref name="price"/> out of <paramref name="currencies"/>.
    /// </summary>
    /// <param name="currencies">
    /// Every priced currency. Where two are worth the same, the one listed first is spent first -
    /// a caller that wants a Vex before a Puzzlebox at 0.5 each lists the runes first.
    /// </param>
    /// <param name="held">
    /// Counts the player has set by hand. Kept as set (held to what the tab holds), and every other
    /// currency counted around them.
    /// </param>
    public static Plan Make(
        IReadOnlyList<Currency> currencies, decimal price, IReadOnlyDictionary<string, int> held)
    {
        var usable = currencies.Where(currency => currency.Worth > 0).ToList();
        var largestFirst = usable
            .Select((currency, order) => (currency, order))
            .OrderByDescending(each => each.currency.Worth)
            .ThenBy(each => each.order)
            .Select(each => each.currency)
            .ToList();

        var paid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        decimal remaining = Math.Max(0, price);

        foreach (var currency in usable)
        {
            if (!held.TryGetValue(currency.Code, out int count))
                continue;

            count = Math.Clamp(count, 0, currency.Stock);
            paid[currency.Code] = count;
            remaining -= count * currency.Worth;
        }

        var free = largestFirst.Where(currency => !held.ContainsKey(currency.Code)).ToList();

        // The largest pieces first, as many as fit.
        foreach (var currency in free)
        {
            int count = remaining > 0 ? (int)Math.Min(currency.Stock, Math.Floor(remaining / currency.Worth)) : 0;
            paid[currency.Code] = count;
            remaining -= count * currency.Worth;
        }

        // What is left is smaller than every piece still in the tab: one more of the smallest
        // piece that covers it, and change for the difference.
        if (remaining > 0
            && free.Where(currency => paid[currency.Code] < currency.Stock && currency.Worth >= remaining)
                   .MinBy(currency => currency.Worth) is { } cover)
        {
            paid[cover.Code]++;
            remaining -= cover.Worth;

            // The larger piece may make smaller ones already counted unnecessary: 0.5 HR out of
            // two Ist, sixteen shards and a Sur came to all of them and 1.71 back, where the Sur
            // alone does it with 1.25 back. Take back what the overpayment covers, largest first
            // so the change left is least - never a count the player set.
            foreach (var currency in free)
            {
                while (paid[currency.Code] > 0 && -remaining >= currency.Worth
                       && !(currency.Code == cover.Code && paid[currency.Code] == 1))
                {
                    paid[currency.Code]--;
                    remaining += currency.Worth;
                }
            }
        }

        decimal shortBy = Math.Max(0, remaining);

        // Change, in the largest pieces that fit. The seller's own purse, so no stock to run out of.
        var change = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        decimal owed = Math.Max(0, -remaining);
        decimal given = 0;

        foreach (var currency in largestFirst)
        {
            int count = (int)Math.Floor((owed - given) / currency.Worth);
            if (count <= 0)
                continue;

            change[currency.Code] = count;
            given += count * currency.Worth;
        }

        decimal paidWorth = usable.Sum(currency => paid.GetValueOrDefault(currency.Code) * currency.Worth);

        return new Plan(paid, change, paidWorth, given, shortBy, owed - given);
    }

    /// <summary>
    /// A sale: <paramref name="price"/> counted out <b>to</b> the player, in the largest pieces that
    /// fit - the buyer's purse has no bottom, so nothing is short. What comes in is the plan's
    /// <see cref="Plan.Change"/>, nothing is paid, and a remainder smaller than every piece is
    /// <see cref="Plan.Lost"/>: kept by the buyer.
    /// </summary>
    /// <param name="currencies">
    /// Every priced currency, with <see cref="Currency.Stock"/> standing for <b>room</b> - how many
    /// more the materials tab can hold.
    /// </param>
    /// <param name="held">Counts the player has set by hand, kept as set within the room.</param>
    public static Plan Receive(
        IReadOnlyList<Currency> currencies, decimal price, IReadOnlyDictionary<string, int> held)
    {
        var usable = currencies.Where(currency => currency.Worth > 0).ToList();
        var received = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        decimal remaining = Math.Max(0, price);

        foreach (var currency in usable)
        {
            if (!held.TryGetValue(currency.Code, out int count))
                continue;

            count = Math.Clamp(count, 0, currency.Stock);
            received[currency.Code] = count;
            remaining -= count * currency.Worth;
        }

        var free = usable
            .Select((currency, order) => (currency, order))
            .Where(each => !held.ContainsKey(each.currency.Code))
            .OrderByDescending(each => each.currency.Worth)
            .ThenBy(each => each.order)
            .Select(each => each.currency);

        foreach (var currency in free)
        {
            int count = remaining > 0 ? (int)Math.Min(currency.Stock, Math.Floor(remaining / currency.Worth)) : 0;
            received[currency.Code] = count;
            remaining -= count * currency.Worth;
        }

        decimal worth = usable.Sum(currency => received.GetValueOrDefault(currency.Code) * currency.Worth);

        return new Plan(new Dictionary<string, int>(), received, 0, worth, 0, Math.Max(0, remaining));
    }

    /// <summary>
    /// A starting figure from a listing's price as its seller typed it - "18 HR", "2,5", ".5",
    /// "5wss" - or null where it does not start with one ("OFFER", "GG Rathma - 3hr or offer").
    /// Only a starting point: the player sets what they pay.
    /// </summary>
    /// <param name="wssWorth">What a Worldstone Shard is worth in HR, for a price given in them.</param>
    public static decimal? Guess(string? text, decimal? wssWorth)
    {
        if (text is null)
            return null;

        var found = System.Text.RegularExpressions.Regex.Match(
            text.Trim(), @"^(?<n>\d+(?:[.,]\d+)?|[.,]\d+)\s*(?<unit>hrs?|wss)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!found.Success
            || !decimal.TryParse(found.Groups["n"].Value.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                                 System.Globalization.CultureInfo.InvariantCulture, out decimal number))
        {
            return null;
        }

        return found.Groups["unit"].Value.Equals("wss", StringComparison.OrdinalIgnoreCase)
            ? wssWorth is { } worth ? number * worth : null
            : number;
    }
}
