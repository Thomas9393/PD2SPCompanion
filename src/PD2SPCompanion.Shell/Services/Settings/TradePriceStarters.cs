using System.Collections.Generic;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The prices Virtual Trading's price table starts with, in HR - the rates commonly traded at in
/// S13. A starting point only: every one can be changed or
/// cleared, and the table is the player's from then on.
/// </summary>
public static class TradePriceStarters
{
    /// <summary>
    /// Raise this when prices are added, and they reach people who have already run the editor.
    /// Only a currency with no price of its own gets one - a price someone set, or cleared, is
    /// theirs.
    /// </summary>
    public const int Version = 1;

    public static IReadOnlyDictionary<string, decimal> All { get; } = new Dictionary<string, decimal>
    {
        ["wss"] = 0.01m,
        ["lbox"] = 0.5m,
        ["r20"] = 0.02m,   // Lem
        ["r21"] = 0.03m,   // Pul
        ["r22"] = 0.05m,   // Um
        ["r23"] = 0.1m,    // Mal
        ["r24"] = 0.15m,   // Ist
        ["r25"] = 0.25m,   // Gul
        ["r26"] = 0.5m,    // Vex
        ["r27"] = 0.75m,   // Ohm
        ["r28"] = 1.5m,    // Lo
        ["r29"] = 1.75m,   // Sur
        ["r30"] = 3.5m,    // Ber
        ["r31"] = 2.5m,    // Jah
        ["r32"] = 1.5m,    // Cham
        ["r33"] = 3m,      // Zod
    };
}
