using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// A vault search worth keeping, under a name the person gave it - the map seed list's idea,
/// applied to the search panel.
/// <para>
/// The filter's values as plain numbers and text, not the pickers' choices, so it survives a
/// change in how a picker words its lines. A plain class with settable properties because it is
/// written to and read back from <c>settings.json</c>. Null is "Any" throughout, as in
/// <see cref="PD2SPCompanion.Core.Collection.VaultQuery"/>.
/// </para>
/// </summary>
public sealed class SearchPreset
{
    public string Name { get; set; } = "";

    /// <summary>When it was kept, so the list can be put back in the order it was built up.</summary>
    public DateTimeOffset Created { get; set; }

    public string? ItemName { get; set; }
    public int? Rarity { get; set; }
    public int? TypeFrom { get; set; }
    public int? TypeTo { get; set; }
    public string? Base { get; set; }
    public bool? Ethereal { get; set; }
    public bool? Corrupted { get; set; }
    public bool? Desecrated { get; set; }
    public string? SocketsMin { get; set; }
    public string? SocketsMax { get; set; }

    /// <summary>The craft family - "Blood", "Caster" - a crafted item was made by.</summary>
    public string? CraftFamily { get; set; }

    /// <summary>Only items that have a copy somewhere. Null is any.</summary>
    public bool? OnlyDuplicates { get; set; }

    /// <summary>The property rows, in the order they were added. Null on a preset from before them.</summary>
    public List<PresetProperty>? Properties { get; set; }

    /// <summary>Whether two presets ask for the same items, whatever they are called.</summary>
    public bool AsksTheSameAs(SearchPreset other) =>
        string.Equals(Tidy(ItemName), Tidy(other.ItemName), StringComparison.OrdinalIgnoreCase)
        && Rarity == other.Rarity
        && string.Equals(Tidy(CraftFamily), Tidy(other.CraftFamily), StringComparison.OrdinalIgnoreCase)
        && TypeFrom == other.TypeFrom && TypeTo == other.TypeTo
        && string.Equals(Tidy(Base), Tidy(other.Base), StringComparison.OrdinalIgnoreCase)
        && Ethereal == other.Ethereal
        && Corrupted == other.Corrupted
        && Desecrated == other.Desecrated
        && Tidy(SocketsMin) == Tidy(other.SocketsMin)
        && Tidy(SocketsMax) == Tidy(other.SocketsMax)
        && OnlyDuplicates == other.OnlyDuplicates
        && (Properties ?? []).SequenceEqual(other.Properties ?? []);

    private static string? Tidy(string? text) => text?.Trim() is { Length: > 0 } kept ? kept : null;
}

/// <summary>
/// One property row of a preset: the option's key, the skill picked beside it where it picks
/// one, and the bounds as typed. A record, so two presets compare row by row by value.
/// </summary>
public sealed record PresetProperty(string Key, int? Skill = null, string? Min = null, string? Max = null);
