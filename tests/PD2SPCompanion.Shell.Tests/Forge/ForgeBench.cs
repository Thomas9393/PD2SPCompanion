using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The Forge as a test of it finds it, and the program put back as it was when the test is over: the play mode
/// the test asks for (the fixture's own is Casual, which has no Forge), the Forge taken on the right page with
/// its boxes at nothing and its holder empty, and a clipboard of the test's own in place of the window's - the
/// view gives the page its clipboard, and here there is no view.
/// <para>
/// Made after <c>program.Ready()</c> and disposed with the test (<c>using</c>): the mode, the clipboard, the
/// search section's fold and the bookmarks go back, and the Forge is left holding nothing. Everything but the
/// constructor, <see cref="Dispose"/> and <see cref="Copied"/> is asked on the program's thread.
/// </para>
/// </summary>
internal sealed class ForgeBench : IDisposable
{
    private readonly Program _program;
    private readonly string _mode;
    private readonly Func<Task<string?>>? _clipboard;
    private readonly bool _searchOpen;

    /// <summary>What the clipboard holds, as the Forge reads it: the test's to set.</summary>
    public volatile string? Copied;

    /// <summary>How many times the Forge has read the clipboard.</summary>
    public int Reads;

    public ForgeBench(Program program, string mode = PlayPolicy.FreeName, bool shown = true)
    {
        _program = program;
        (_mode, _clipboard, _searchOpen) = program.On(() => (Settings.PlayMode, Forge.Clipboard, Forge.SearchOpen));

        program.On(() =>
        {
            Forge.Clipboard = () =>
            {
                Reads++;
                return Task.FromResult(Copied);
            };

            Settings.PlayMode = mode;
            if (shown && Main.CanForge)
            {
                Main.ShowRightCommand.Execute(MainViewModel.ForgeMark);
                Blank();
            }
        });
    }

    public MainViewModel Main => _program.Main;

    public CharacterSession Session => Main.Session;

    public EditorSettings Settings => _program.Get<EditorSettings>();

    public VP_ViewModel_Vault_R Right => _program.Get<VP_ViewModel_Vault_R>();

    public ForgeViewModel Forge => Right.Forge;

    /// <summary>The item panel under the Forge's boxes, over the holder's item.</summary>
    public VP_ViewModel_Inv_R Panel => Forge.Editor;

    /// <summary>
    /// Every picker over the item panel closed. One left open by a test held every change of the next staged and none
    /// kept (VP_ViewModel_Inv_R.KeepAtOnce waits for them all to close) - the Forge's hit-skill tests failed in the whole
    /// run only.
    /// </summary>
    public void ClosePickers()
    {
        var panel = Panel;
        panel.ClosePickerCommand.Execute(null);
        panel.Affixes.CloseAffixPickerCommand.Execute(null);
        panel.Crafting.ClosePickerCommand.Execute(null);
        panel.Automagic.ClosePickerCommand.Execute(null);
        panel.Corruption.ClosePickerCommand.Execute(null);
        panel.Desecration.ClosePickerCommand.Execute(null);
        panel.Runeword.ClosePickerCommand.Execute(null);
        panel.Superior.ClosePickerCommand.Execute(null);
        panel.StaffSkills.ClosePickerCommand.Execute(null);
        panel.MapRecipe.ClosePickerCommand.Execute(null);
        panel.RareName.ClosePickerCommand.Execute(null);
        panel.StatPicker.CloseCommand.Execute(null);
    }

    /// <summary>The Forge with nothing chosen: no text, no type, no base, a unique of no name - and so nothing in the holder.</summary>
    public void Blank()
    {
        ClosePickers();
        Forge.Search = "";
        Forge.Base = ForgeSearch.Any;
        Forge.Type = ForgeSearch.Any;

        // Through a rarity that names nothing: a name chosen would otherwise stand among all the uniques.
        Forge.Rarity = ForgeSearch.AllRarities[ForgeSearch.Rare];
        Forge.Rarity = ForgeSearch.AllRarities[ForgeSearch.Unique];
    }

    /// <summary>A find of the search box chosen: typed, and the row of that name pressed.</summary>
    public void Find(string typed, string name)
    {
        Forge.Search = typed;
        Forge.PickCommand.Execute(Forge.Matches.First(match => match.Name == name));
    }

    /// <summary>The rarity box set to one of the lines it offers, by its word.</summary>
    public void Rarity(string shown) => Forge.Rarity = Forge.RarityChoices.First(line => line.Shown == shown);

    /// <summary>The holder's item.</summary>
    public Item Held => Forge.Draft!.Item;

    /// <summary>What the holder says of its item: the lines the game would print, as the summary draws them.</summary>
    public List<string> Lines() => [.. (Panel.Summary?.Summed ?? []).Select(line => line.Text)];

    /// <summary>The one line of the holder's that begins so: "Defense: ", "Durability: ".</summary>
    public string Line(string begins) => Lines().Single(line => line.StartsWith(begins, StringComparison.Ordinal));

    /// <summary>A property's row in the item panel, by the stat's own name.</summary>
    public StatRowViewModel Row(string stat) => Panel.Stats.First(row => row.Name == stat);

    /// <summary>Waited for: what a change of the item panel's is written a moment after it is made.</summary>
    public bool Until(Func<bool> so, int milliseconds = 4000) => _program.Until(so, milliseconds);

    /// <summary>
    /// Waited for: nothing of the Forge's item panel is waiting to be written. <c>program.Settled()</c> with no page
    /// asks the character's panel, which has nothing waiting, and returns at once (CraftAffixShareTests read an unkept
    /// draft so). Not asked on the program's thread.
    /// </summary>
    public void Settled() => Assert.True(_program.Until(() => !Panel.HasChanges), "the change was not kept");

    public void Dispose()
    {
        _program.On(() =>
        {
            Blank();

            // Nothing selected on either left page, whatever the test clicked.
            Main.Inventory?.ClearSelection();
            Main.Vault?.ClearSelection();

            Forge.Clipboard = _clipboard;
            Forge.SearchOpen = _searchOpen;
            Settings.PlayMode = _mode;

            if (Session.IsLoaded)
            {
                Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            }
        });

        // What was looked at with the test's clipboard is forgotten: the page looks again when it is next shown.
        _program.On(() => _ = Forge.Peek());
    }
}
