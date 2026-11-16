namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Whether this run works on test copies: <c>PD2SPC_SAVES</c> points the save folder, and
/// <c>PD2SPC_VAULTS</c> the vaults, somewhere other than the player's own. Said on screen, because a
/// test instance looked exactly like the player's editor and was used as one (a
/// staff duplicated on Shared Page 8 there never reached the real stash).
/// </summary>
public static class TestCopies
{
    public static string? Saves => Environment.GetEnvironmentVariable("PD2SPC_SAVES") is { Length: > 0 } path ? path : null;

    public static string? Vaults => Environment.GetEnvironmentVariable("PD2SPC_VAULTS") is { Length: > 0 } path ? path : null;

    /// <summary>True when either points away from the player's own files.</summary>
    public static bool IsOn => Saves is not null || Vaults is not null;

    /// <summary>Which folders are copies, one per line - for a tooltip.</summary>
    public static string Describe() =>
        string.Join(Environment.NewLine, new[]
        {
            Saves is { } saves ? $"Saves: {saves}" : null,
            Vaults is { } vaults ? $"Vaults: {vaults}" : null,
        }.OfType<string>());
}
