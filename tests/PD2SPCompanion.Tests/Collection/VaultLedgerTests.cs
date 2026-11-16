using System;
using System.IO;
using System.Linq;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The ledger's trades follow the session's history: a trade undone is taken out again
/// (Vault.ForgetTrades), one row for each, and nothing that only resembles it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultLedgerTests(Game game) : IDisposable
{
    private readonly Game _game = game;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-ledger-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static TradeRecord Trade(string kind, string name, string price, string paid, int minute) =>
        new(new DateTimeOffset(2026, 10, 7, 18, minute, 0, TimeSpan.Zero), "Tester", kind, null, null, null, null, name, price, paid, "Casual");

    [GameFact]
    public void ATradeUndoneLeavesNoRecord_AndOnlyItself()
    {
        Assert.NotNull(_game);
        Directory.CreateDirectory(_folder);
        using var vault = Vault.Open(Path.Combine(_folder, "S13 Main.pd2lib"), "S13", 10);

        var bought = Trade("bought", "Harlequin Crest", "2 HR", "2 Ist Rune", 1);
        var twin = Trade("bought", "Harlequin Crest", "2 HR", "2 Ist Rune", 1);
        var sold = Trade("sold", "Harlequin Crest", "1 HR", "received 1 Ist Rune", 2);
        var other = Trade("bought", "Stone of Jordan", "2 HR", "2 Ist Rune", 1);
        vault.RecordTrades([bought, twin, sold, other]);
        Assert.Equal(4, vault.Trades().Count);

        // The sale undone: its row goes, the purchases of the same item stay.
        Assert.Equal(1, vault.ForgetTrades([sold]));
        Assert.Equal(3, vault.Trades().Count);
        Assert.DoesNotContain(vault.Trades(), trade => trade.Kind == "sold");

        // One of two that say the same: one row, not both.
        Assert.Equal(1, vault.ForgetTrades([bought]));
        Assert.Equal(1, vault.Trades().Count(trade => trade.Name == "Harlequin Crest"));
        Assert.Contains(vault.Trades(), trade => trade.Name == "Stone of Jordan");

        // One that is not there: nothing goes. And done again, it is written again.
        Assert.Equal(0, vault.ForgetTrades([sold]));
        vault.RecordTrades([sold]);
        Assert.Equal(3, vault.Trades().Count);
        Assert.Equal(0, vault.ForgetTrades([]));
    }
}
