using System.Text.Json;
using System.Text.RegularExpressions;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// A listing off the PD2 trade site, found by the link someone pasted.
/// <para>
/// <b>Cache first.</b> A listing is fetched once and kept as a file; every later look reads the
/// file. The site's by-id route is gated and its collection query is not, which says volume is
/// watched somewhere - one request per listing, ever, is the most this asks of it.
/// </para>
/// </summary>
public static partial class TradeListings
{
    /// <summary>
    /// Only the trade site's own listing page: <c>projectdiablo2.com/market/listing/&lt;id&gt;</c>,
    /// with or without the scheme and <c>www.</c>, and the id the 24 hex digits every listing has.
    /// Nothing else is taken - any other address would be a request to a site we did not mean.
    /// </summary>
    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?projectdiablo2\.com/market/listing/(?<id>[0-9a-f]{24})/?(?:[?#].*)?$",
                    RegexOptions.IgnoreCase)]
    private static partial Regex ListingLink();

    /// <summary>The listing's id in a link, or null when the link is not a PD2 trade listing.</summary>
    public static string? IdIn(string? link) =>
        link is not null && ListingLink().Match(link.Trim()) is { Success: true } found
            ? found.Groups["id"].Value.ToLowerInvariant()
            : null;

    /// <summary>Where fetched listings are kept, one file each, named by id.</summary>
    public static string CacheFolder => EditorData.TradeCache;

    private static string CacheFileFor(string id) => Path.Combine(CacheFolder, $"{id}.json");

    /// <summary>The listings kept on disk: how many, and how many bytes they take.</summary>
    public static (int Count, long Bytes) CacheSize()
    {
        if (!Directory.Exists(CacheFolder))
            return (0, 0);

        var files = Directory.GetFiles(CacheFolder, "*.json");
        return (files.Length, files.Sum(file => new FileInfo(file).Length));
    }

    /// <summary>
    /// Deletes every listing kept on disk, and the gate's answers held in memory with them; says how
    /// many files went. A live listing is fetched again when next opened; one sold or expired since
    /// is gone for good, the site handing those only to a logged-in user.
    /// </summary>
    public static int ClearCache()
    {
        TradeGate.Forget();

        if (!Directory.Exists(CacheFolder))
            return 0;

        int removed = 0;
        foreach (string file in Directory.GetFiles(CacheFolder, "*.json"))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // Open elsewhere: left for next time rather than failing the rest.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>
    /// The collection query, not the by-id get: the get answers 401 and the query hands out the
    /// same record with no credentials. A live listing only - one that has sold or expired has
    /// moved to <c>market/listingArchive</c>, which needs a logged-in user.
    /// </summary>
    private static string AddressFor(string id) => $"https://api.projectdiablo2.com/market/listing?_id={id}";

    /// <summary>A listing as the site hands it out, and whether it came off disk or the site.</summary>
    /// <param name="Record">The listing: its <c>item</c>, <c>price</c> and the rest.</param>
    public sealed record Listing(string Id, JsonElement Record, bool FromCache)
    {
        public JsonElement Item => Record.GetProperty("item");

        /// <summary>
        /// The site's id for the item, as it gives it - the same across relists of one item, which a
        /// listing's own id is not. Empty when the listing has none.
        /// </summary>
        public string ItemId => Item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
            ? id.GetRawText()
            : "";

        /// <summary>The site's hash of the item: what one item posted twice shares. Empty when absent.</summary>
        public string ItemHash => Item.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.String
            ? hash.GetString() ?? ""
            : "";

        /// <summary>What the seller asks, as they typed it - free text, not a number.</summary>
        public string Price => Record.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.String
            ? price.GetString() ?? ""
            : "";
    }

    /// <summary>Why a listing could not be had, in words to show.</summary>
    public sealed class TradeListingException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// The listing of this id: the cached copy when there is one, otherwise fetched once and
    /// cached. Only a listing that came back is cached - "none" may be a listing not yet live.
    /// </summary>
    public static async Task<Listing> Get(string id, CancellationToken cancel = default)
    {
        string file = CacheFileFor(id);

        if (File.Exists(file))
        {
            try
            {
                if (FirstIn(await File.ReadAllTextAsync(file, cancel)) is { } kept)
                    return new Listing(id, kept, FromCache: true);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                // Unreadable - fetched again below, and the file written over.
            }
        }

        // Through the gate, as every request to the site is - see TradeGate. Not kept in memory:
        // a fetched listing is kept on disk, which is better.
        string body;
        try
        {
            body = await TradeGate.Get(AddressFor(id), keep: false, cancel);
        }
        catch (TradeGate.TradeGateException refused)
        {
            throw new TradeListingException(refused.Message, refused);
        }

        JsonElement? found;
        try
        {
            found = FirstIn(body);
        }
        catch (JsonException exception)
        {
            throw new TradeListingException(L("Trade site answer unreadable."), exception);
        }

        if (found is not { } record)
            throw new TradeListingException(L("Listing not found: sold or expired."));

        Directory.CreateDirectory(CacheFolder);
        await File.WriteAllTextAsync(file, body, cancel);

        return new Listing(id, record, FromCache: false);
    }

    /// <summary>
    /// The first record of a query's answer - <c>{ total, data: [ ... ] }</c> - or null for none: an answer of any other
    /// shape is the same as none, whatever the network or a file on disk held.
    /// </summary>
    /// <exception cref="JsonException">It is not JSON.</exception>
    public static JsonElement? FirstIn(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty("data", out var data)
               && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0
               && data[0].ValueKind == JsonValueKind.Object
               && data[0].TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
            ? data[0].Clone()
            : null;
    }
}
