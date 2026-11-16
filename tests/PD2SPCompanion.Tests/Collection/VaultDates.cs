namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A row marked by when it was added: the one thing a row says of itself besides the item, so what a test
/// follows a row by - across a merge, an import, a season's migration - is its date, set here to a day no
/// storing of a test's own could give.
/// </summary>
internal static class VaultDates
{
    public static readonly DateTimeOffset LongAgo = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

    /// <summary>Says a row has lain in the vault since <see cref="LongAgo"/>.</summary>
    public static void StoredLongAgo(this Vault vault, long key)
    {
        using var write = vault.Connection.CreateCommand();
        write.CommandText = "UPDATE item SET added_at = $at WHERE key = $key;";
        write.Parameters.AddWithValue("$at", LongAgo.ToUnixTimeSeconds());
        write.Parameters.AddWithValue("$key", key);
        write.ExecuteNonQuery();
    }
}
