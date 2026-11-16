using System;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// One map seed worth keeping, and what the person wrote about it.
/// <para>
/// The seed is the <c>.d2s</c> header's Map ID at offset 0xAB - a plain 32-bit number that
/// says nothing about the world it produces, which is why the remark carries the meaning.
/// </para>
/// <para>
/// A plain class with settable properties rather than a record, because it is serialised to
/// and from <c>settings.json</c> and read back by the same JSON that wrote it.
/// </para>
/// </summary>
public sealed class MapSeedNote
{
    public uint Seed { get; set; }

    public string Remark { get; set; } = "";

    /// <summary>
    /// When this was added, so the list can be put back in the order it was built up.
    /// <para>
    /// Default on anything written before the field existed, and on the seeds the list starts
    /// with - none of those has a real moment of creation, and grouping them at the top is the
    /// honest answer rather than inventing dates for them.
    /// </para>
    /// </summary>
    public DateTimeOffset Created { get; set; }
}
