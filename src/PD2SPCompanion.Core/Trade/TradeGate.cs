using System.Net;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// The one way to the PD2 trade site. Every request passes through here, so the manners are kept
/// in one place rather than by each caller remembering them:
/// <list type="bullet">
/// <item><b>One at a time, five seconds apart.</b> A request that comes sooner waits its turn -
/// it is never sent early, and a cancelled one is never sent at all.</item>
/// <item><b>No automatic retries.</b> A failure is said, and the player decides.</item>
/// <item><b>Backing off.</b> "Too many requests" or a server error closes the gate for a minute,
/// or for as long as the site asks, whichever is longer.</item>
/// <item><b>Remembering.</b> An answer asked to be kept is kept in memory for ten minutes, and
/// the same question in that time is answered from it without a request.</item>
/// <item><b>An honest name</b>, <c>PD2SPCompanion/&lt;version&gt;</c>, and <b>nothing in the
/// background</b> - every request is something the player just asked for.</item>
/// <item><b>A ceiling</b> of 120 requests an hour: a guard against a bug that loops, not a limit
/// a player should ever meet.</item>
/// </list>
/// The site states no limits; these are what a polite client does anyway.
/// </summary>
public static class TradeGate
{
    /// <summary>The least time between two requests.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(5);

    /// <summary>The least time the gate stays shut after the site says it is busy or failing.</summary>
    public static readonly TimeSpan BackOff = TimeSpan.FromSeconds(60);

    /// <summary>How long a kept answer is good for.</summary>
    public static readonly TimeSpan Keeping = TimeSpan.FromMinutes(10);

    /// <summary>The most requests in any hour.</summary>
    public static int HourlyCeiling { get; private set; } = 120;

    /// <summary>
    /// A higher hourly ceiling for one command-line run. The ladder audit was agreed
    /// at 240 an hour - one every 12-18 s, what one attentive person browsing the armory could
    /// plausibly do - with the spacing and the back-off unchanged. The editor never calls this.
    /// </summary>
    public static void AllowPerHour(int requests) => HourlyCeiling = Math.Clamp(requests, 1, 240);

    /// <summary>The name this client gives: the editor and its version.</summary>
    public static string UserAgent { get; } = MakeUserAgent();

    // Before the client, which reads it: static fields are set in the order they are written.
    private static string MakeUserAgent() => $"PD2SPCompanion/{AppVersion.Number}";

    private static readonly HttpClient Http = MakeClient();
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static readonly Queue<DateTimeOffset> LastHour = new();
    private static readonly Dictionary<string, (DateTimeOffset At, string Body)> Kept = [];

    private static DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private static DateTimeOffset _shutUntil = DateTimeOffset.MinValue;

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    private static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// The body at <paramref name="address"/>, through the gate. With <paramref name="keep"/>, an
    /// answer from the last ten minutes is given without asking, and a new one is kept.
    /// </summary>
    /// <exception cref="TradeGateException">Refused, failed or unreachable - worded for showing.</exception>
    /// <param name="missing">
    /// Says whether an error answer's body means "no such thing" rather than a server in trouble: the
    /// armory answers 500 "Failed to parse character" for a name it does not know (measured).
    /// Such an answer is thrown as <see cref="TradeGateException.IsMissing"/> and does not
    /// shut the gate, which would otherwise hold the trade pages for a mistyped name.
    /// </param>
    public static async Task<string> Get(string address, bool keep = false, CancellationToken cancel = default,
                                         Func<string, bool>? missing = null)
    {
        if (keep && Remembered(address) is { } answer)
            return answer;

        await OneAtATime.WaitAsync(cancel);
        try
        {
            // Asked again while this one waited for its turn: the one before may have fetched it.
            if (keep && Remembered(address) is { } meanwhile)
                return meanwhile;

            var now = Now();

            if (now < _shutUntil)
            {
                int seconds = (int)Math.Ceiling((_shutUntil - now).TotalSeconds);
                throw new TradeGateException(L($"Trade site busy. Retry in {seconds} s."));
            }

            while (LastHour.Count > 0 && now - LastHour.Peek() >= TimeSpan.FromHours(1))
                LastHour.Dequeue();
            if (LastHour.Count >= HourlyCeiling)
                throw new TradeGateException(L("Hourly request limit reached. Retry later."));

            var due = _lastSent + Spacing;
            if (now < due)
                await Task.Delay(due - now, cancel);

            cancel.ThrowIfCancellationRequested();

            _lastSent = Now();
            LastHour.Enqueue(_lastSent);

            string body;
            try
            {
                using var response = await Http.GetAsync(address, cancel);

                if (missing is not null && !response.IsSuccessStatusCode
                    && response.StatusCode is not HttpStatusCode.TooManyRequests
                    && missing(await response.Content.ReadAsStringAsync(cancel)))
                    throw new TradeGateException(L("Not found.")) { IsMissing = true };

                if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    var asked = response.Headers.RetryAfter?.Delta
                                ?? (response.Headers.RetryAfter?.Date is { } until ? until - Now() : null);
                    _shutUntil = Now() + (asked is { } longer && longer > BackOff ? longer : BackOff);
                    Log.Warning("trade gate", $"{(int)response.StatusCode} from the trade site; shut until {_shutUntil:HH:mm:ss}"
                                              + (asked is { } wait ? $" (it asked for {wait.TotalSeconds:0} s)" : ""));

                    throw new TradeGateException(response.StatusCode is HttpStatusCode.TooManyRequests
                        ? L("Trade site busy. Retry in a minute.")
                        : L($"Trade site error {(int)response.StatusCode}. Retry in a minute."));
                }

                if (!response.IsSuccessStatusCode)
                    throw new TradeGateException(L($"Trade site error {(int)response.StatusCode}."));

                body = await response.Content.ReadAsStringAsync(cancel);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              && !cancel.IsCancellationRequested)
            {
                throw new TradeGateException(L("Trade site unreachable."), exception);
            }

            if (keep)
            {
                lock (Kept)
                    Kept[address] = (Now(), body);
            }

            return body;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Lets go of every answer held in memory - the trade cache cleared in Settings.</summary>
    public static void Forget()
    {
        lock (Kept)
            Kept.Clear();
    }

    private static string? Remembered(string address)
    {
        lock (Kept)
        {
            if (!Kept.TryGetValue(address, out var kept))
                return null;

            if (Now() - kept.At < Keeping)
                return kept.Body;

            Kept.Remove(address);
            return null;
        }
    }

    /// <summary>Why the trade site could not be asked, or what it said - in words to show.</summary>
    public sealed class TradeGateException(string message, Exception? inner = null) : Exception(message, inner)
    {
        /// <summary>The site answered that the thing asked for does not exist (see <c>missing</c> on Get).</summary>
        public bool IsMissing { get; init; }
    }
}
