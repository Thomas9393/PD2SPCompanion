namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// How one list was last ordered. Kept by a key so every list that offers an order keeps its
/// own, rather than each one adding a pair of fields to the settings file.
/// <para>
/// A plain class with settable properties rather than a record, because it is serialised to
/// and from <c>settings.json</c>.
/// </para>
/// </summary>
public sealed class StoredSort
{
    /// <summary>The chosen mode, by the name the list offered.</summary>
    public string By { get; set; } = "";

    public bool Descending { get; set; }
}
