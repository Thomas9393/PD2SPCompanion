using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The socket count of the item on the Edit page: its stepper, its box and its die.
/// The die says and rolls the range of what gave
/// the item its sockets - its own row's, a corruption's - and Shift one to the most the base takes; the count is
/// the item's at once; Shift on + and - goes to the ends. And a count set under what is socketed turns the rest
/// out, whichever way it is set - rolled by the die, typed into the box, rolled by the Demonic Cube, which
/// rolls the count with the numbers here - the gems landing beside the item. On the vault's Edit page a gem
/// cannot come out, and the stepper stops where the sockets are filled.
/// <para>
/// What the section decides, asked as the view asks (its commands, its box's text, Jump and RollWhole for the
/// Shift clicks) and read back from the item and, for a vault item, from the vault's own row. That a Shift
/// click reaches the stepper and the die, and that the box's text reaches the count, is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SocketCountTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private SocketsEditor Sockets => Edit.Sockets;

    private string Said => program.Main.Session.RightMessages.Text;

    [CorpusFact]
    public void The_die_names_where_its_range_comes_from()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // A corruption's sockets on armor: one to three.
            program.Select("Duriel's Shell");
            Assert.StartsWith("Reroll within 1-3 (corruption)", Sockets.RollHint);

            // A unique's own row, the whole of it with three socketed.
            program.Select("Witchwild String", "Personal Stash");
            Assert.Equal("3", Sockets.Count);
            Assert.StartsWith("Reroll within 2-4", Sockets.RollHint);
            Assert.DoesNotContain("(", Sockets.RollHint!.Split('\n')[0]);

            // What takes no sockets has no stepper, so no die.
            program.Select("Horadric Cube", "Inventory");
            Assert.False(Sockets.CanSocket);
            Assert.False(Sockets.CanRoll);
            Assert.Null(Sockets.RollHint);

            program.Deselect();
        });
    }

    [CorpusFact]
    public void A_roll_under_what_is_socketed_turns_the_rest_out_beside_the_item()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var bow = program.Select("Witchwild String", "Personal Stash");
            int stash = program.CountIn("Personal Stash");
            Assert.Equal(3, bow.Filled());

            for (int press = 0; press < 60 && Sockets.Count != "2"; press++)
                Sockets.RollSocketsCommand.Execute(null);

            Assert.Equal("2", Sockets.Count);
            Assert.Equal("Witchwild String: 2 sockets.", Said);
            Assert.Equal(2, bow.TotalNumberOfSockets);
            Assert.Equal(2, bow.Filled());
            Assert.Equal(stash + 1, program.CountIn("Personal Stash"));
            Assert.False(Edit.HasChanges);
        });
    }

    [CorpusFact]
    public void The_Demonic_Cube_leaves_the_count_which_the_sections_own_die_rolls()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var bow = program.Select("Witchwild String", "Personal Stash");
            Sockets.Count = "2";
            Assert.Equal(2, bow.TotalNumberOfSockets);
            int stash = program.CountIn("Personal Stash");

            // The game's cube leaves the count, and so does this one (in game the Demonic Cube rerolls the
            // stats alone).
            Assert.True(Edit.CanReroll);
            for (int press = 0; press < 20; press++)
                Edit.RerollItemCommand.Execute(null);

            Assert.Equal("2", Sockets.Count);
            Assert.Equal(2, bow.TotalNumberOfSockets);
            Assert.Equal(2, bow.Filled());
            Assert.Equal(stash, program.CountIn("Personal Stash"));
        });
    }

    [CorpusFact]
    public void None_typed_turns_every_rune_out_and_leaves_no_sockets()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var bow = program.Select("Witchwild String", "Personal Stash");
            int stash = program.CountIn("Personal Stash");

            Sockets.Count = "0";

            Assert.Equal(0, bow.TotalNumberOfSockets);
            Assert.Equal(0, bow.Filled());
            Assert.False(bow.IsSocketed);
            Assert.Equal(stash + 3, program.CountIn("Personal Stash"));
            Assert.Equal("0", Sockets.Count);
            Assert.Equal("Sockets  (0 / 0)", Sockets.Header);
        });
    }

    [CorpusFact]
    public void The_die_lands_on_both_ends_of_the_items_own_range_and_nowhere_else_and_the_roll_is_the_items_at_once()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            // Two of them: the one not rolled is left as it was.
            var blade = program.Give("ali baba", "Blade of Ali Baba")!;
            var other = program.Give("ali baba", "Blade of Ali Baba")!;
            int others = other.TotalNumberOfSockets;
            program.Main.Session.SelectedItem = program.TileOf(blade);

            Assert.Matches(@"^Reroll within 2-3\s+Shift \+ LMB: within 1-3$", Sockets.RollHint);

            // Forty presses miss an end once in 2^39.
            var seen = new HashSet<int>();
            for (int press = 0; press < 40 && !(seen.Contains(2) && seen.Contains(3)); press++)
            {
                Sockets.RollSocketsCommand.Execute(null);
                var said = System.Text.RegularExpressions.Regex.Match(Said, @"^Blade of Ali Baba: (\d) sockets\.$");
                Assert.True(said.Success, Said);
                seen.Add(int.Parse(said.Groups[1].Value));

                // Kept as it is rolled: nothing waits on the page.
                Assert.Equal(int.Parse(said.Groups[1].Value), blade.TotalNumberOfSockets);
                Assert.False(Edit.HasChanges);
            }

            Assert.Equal([2, 3], seen.Order());
            Assert.Equal(others, other.TotalNumberOfSockets);
        });
    }

    [CorpusFact]
    public void Shift_on_the_die_rolls_one_to_the_most_the_base_takes()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            var blade = program.Give("ali baba", "Blade of Ali Baba")!;
            program.Main.Session.SelectedItem = program.TileOf(blade);

            // A one, which the blade's own range never gives.
            var seen = new HashSet<int>();
            for (int press = 0; press < 60 && !seen.Contains(1); press++)
            {
                Sockets.RollWhole();
                seen.Add(blade.TotalNumberOfSockets);
            }

            Assert.Contains(1, seen);
            Assert.All(seen, count => Assert.InRange(count, 1, 3));
        });
    }

    [CorpusFact]
    public void A_count_typed_into_the_box_is_the_items_at_once_held_to_what_the_base_takes()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            var blade = program.Give("ali baba", "Blade of Ali Baba")!;
            program.Main.Session.SelectedItem = program.TileOf(blade);

            Sockets.Count = "2";
            Assert.Equal(2, blade.TotalNumberOfSockets);
            Assert.Equal("2", Sockets.Count);

            Sockets.Count = "3";
            Assert.Equal(3, blade.TotalNumberOfSockets);

            // Past the most the base takes: held there. A box emptied on the way to another number is no count.
            Sockets.Count = "9";
            Assert.Equal(Sockets.Maximum, blade.TotalNumberOfSockets);
            Sockets.Count = "";
            Assert.Equal(Sockets.Maximum, blade.TotalNumberOfSockets);
        });
    }

    /// <summary>The box as the section's markup binds it: the range first, then the count, two ways.</summary>
    private static Avalonia.Controls.Window BoxOver(SocketsEditor sockets, out CustomControls.Base.NumericTextBox box)
    {
        box = new CustomControls.Base.NumericTextBox { DataContext = sockets };
        box.Bind(CustomControls.Base.NumericTextBox.MinimumProperty, new Avalonia.Data.Binding(nameof(SocketsEditor.Minimum)));
        box.Bind(CustomControls.Base.NumericTextBox.MaximumProperty, new Avalonia.Data.Binding(nameof(SocketsEditor.BoxMaximum)));
        box.Bind(Avalonia.Controls.TextBox.TextProperty, new Avalonia.Data.Binding(nameof(SocketsEditor.Count))
        {
            Mode = Avalonia.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = Avalonia.Data.UpdateSourceTrigger.PropertyChanged,
        });
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100, Content = box };
        window.Show();
        return window;
    }

    /// <summary>
    /// Selecting an item never changes it: an item holding more sockets than the tables allow -
    /// a level lowered in Free, a Legacy item, a base the tables do not know - came to the box as its count against the
    /// limit alone, the box wrote the limit back as if it had been typed, and the rest were kept off with what was in them.
    /// </summary>
    [CorpusFact]
    public void An_item_over_its_socket_limit_is_left_as_it_is_by_being_shown_in_the_box()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item over = null!;
        Avalonia.Controls.Window window = null!;
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var plain = program.GivePlain("cap", 1);
            over = program.GivePlain("cap", 2);
            over.TotalNumberOfSockets = 9;
            window = BoxOver(Sockets, out var box);
            program.Main.Session.SelectedItem = program.TileOf(plain);
            Assert.Equal("1", box.Text);

            program.Main.Session.SelectedItem = program.TileOf(over);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(Sockets.Maximum < 9, $"the base takes {Sockets.Maximum}: the item is not over it");
            Assert.Equal(9, Sockets.BoxMaximum);
            Assert.Equal("9", box.Text);
            Assert.Equal("9", Sockets.Count);
            Assert.Equal(9, over.TotalNumberOfSockets);
            Assert.False(Sockets.AreChanged);
            Assert.False(Edit.HasChanges);

            // Typed: past what it holds is held there, and under it is a count like any other.
            Sockets.Count = "12";
            Assert.Equal(9, over.TotalNumberOfSockets);
            Sockets.Count = "2";
        });

        program.Settled();
        program.On(() =>
        {
            Assert.Equal(2, over.TotalNumberOfSockets);
            window.Close();
        });
    }

    [CorpusFact]
    public void A_socket_added_on_the_vaults_Edit_page_is_written_to_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 2, "Crystal Sword");
        var sockets = program.On(() => program.VaultEditPage().Sockets);

        program.On(() =>
        {
            Assert.Equal("Sockets  (0 / 2)", sockets.Header);
            Assert.True(sockets.CanAdd);

            sockets.AddCommand.Execute(null);
            Assert.Equal("Sockets  (0 / 3)", sockets.Header);
        });

        // Read back from the vault's own row, not from the page.
        Assert.True(program.Until(() => program.Stored().Item.TotalNumberOfSockets == 3), "the vault's row has not three sockets");
        program.On(() => Assert.Equal("Crystal Sword", program.Stored().Row.Name));
    }

    [CorpusFact]
    public void Shift_on_the_stepper_goes_to_the_most_the_item_takes_and_to_none()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 3, "Crystal Sword");
        var sockets = program.On(() => program.VaultEditPage().Sockets);

        program.On(() =>
        {
            Assert.Equal("Sockets  (0 / 3)", sockets.Header);

            sockets.Jump(up: true);
            Assert.False(sockets.CanAdd);
            Assert.NotEqual("Sockets  (0 / 3)", sockets.Header);
            Assert.Equal($"Sockets  (0 / {sockets.Maximum})", sockets.Header);

            sockets.Jump(up: false);
            Assert.False(sockets.CanRemove);
            Assert.Equal("Sockets  (0 / 0)", sockets.Header);
            Assert.True(sockets.CanAdd);
        });

        Assert.True(program.Until(() => program.Stored().Item.TotalNumberOfSockets == 0), "the vault's row still has sockets");
    }
}
