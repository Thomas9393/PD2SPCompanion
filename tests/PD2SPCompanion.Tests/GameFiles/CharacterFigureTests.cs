using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The character drawn in its gear: the archive reads a file stored uncompressed, the DCC
/// decoder reads a layer to the frame sizes the game's own file holds, and a real Amazon composes in
/// full with nothing missing.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterFigureTests(Game game)
{
    [CorpusFact]
    public void An_uncompressed_file_reads_whole()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var spear = data.Read(@"data\global\chars\AM\RH\AMRHAM3TN2HT.dcc");
        Assert.NotNull(spear);
        Assert.Equal(84046, spear.Value.Data.Length);
        Assert.Equal(0x74, spear.Value.Data[0]);
    }

    [CorpusFact]
    public void A_layer_decodes_to_its_frames()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var arm = data.Read(@"data\global\chars\AM\RA\AMRALITTNHTH.dcc")!.Value.Data;
        Assert.Equal(16, Dcc.Directions(arm));
        var frames = Dcc.Read(arm, 0);
        Assert.Equal(16, frames.Count);
        Assert.Equal((-16, -54, 9, 29), (frames[0].Left, frames[0].Top, frames[0].Width, frames[0].Height));
        Assert.Equal(123, frames[0].Indices.Count(index => index != 0));
    }

    [CorpusFact]
    public void A_real_character_composes_in_full()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var palette = Palette.Load(data);
        ColourMaps.Load(data);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));

        var look = CharacterFigure.LookOf(save);
        Assert.Equal("AM", look.Token);
        var figure = CharacterFigure.Compose(data, palette, look, 0);
        Assert.NotNull(figure);
        Assert.Empty(figure.Missing);
        Assert.Equal(16, figure.Frames.Count);
        Assert.All(figure.Frames, frame => Assert.Equal(figure.Width * figure.Height, frame.Bgra.Length));
    }

    // PhySpearZon: a crafted spear (two coloured prefixes, a coloured suffix), unique helm and armour.
    // FigureTest (loaded in game): a magic sword with a coloured prefix and
    // suffix, a rare Breast Plate with a coloured prefix and two coloured suffixes - the suffix first.
    // OrbAlone, OrbSuffix, ShieldPrefix (loaded in game): a coloured automagic (Drake's, Knight's) alone, under a
    // coloured suffix, under a coloured prefix - it colours only where no affix does.
    [CorpusTheory]
    [InlineData("PhySpearZon.d2s")]
    [InlineData("FigureTest.d2s")]
    [InlineData("OrbAlone.d2s")]
    [InlineData("OrbSuffix.d2s")]
    [InlineData("ShieldPrefix.d2s")]
    public void Tints_from_the_items_are_the_games_own(string file)
    {
        if (!game.Available)
            return;

        // The game writes each part's tint into the save (Transform x 32 + colour row + 1). Worked out
        // from the worn items instead - so an edit shows before the game saves - they must agree.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", file));
        var look = CharacterFigure.LookOf(save);
        var bytes = typeof(D2SLib.Model.Save.Appearances).GetProperties()
            .Select(p => ((D2SLib.Model.Save.Appearance)p.GetValue(save.Appearances)!).Tint).ToList();
        int compared = 0;
        for (int i = 0; i < CharacterFigure.Parts.Length; i++)
        {
            if (bytes[i] == 255 || (bytes[i] & 31) == 0)
                continue;
            Assert.True(look.Tints.TryGetValue(CharacterFigure.Parts[i], out var tint), $"{CharacterFigure.Parts[i]} has no tint");
            // Three bits hold the table: an auric shield's Transform 8 is written as 0.
            Assert.Equal((bytes[i] >> 5, (bytes[i] & 31) - 1), (tint.Table & 7, tint.Colour));
            compared++;
        }
        Assert.True(compared > 0);
    }

    /// <summary>
    /// The weapons in hand are the main hand slots', whichever tab the save names for them (WeaponSets): the
    /// Barbarian, a weapon in each hand, saved on its II tab (switch 1) - drawn bare-handed if
    /// the switch is read as "the swap slots are in hand". Both weapons, in the two-weapon pose, and
    /// the same whatever the switch says.
    /// </summary>
    [CorpusFact]
    public void The_hands_are_the_main_slots_whatever_tab_the_save_names()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var palette = Palette.Load(data);
        ColourMaps.Load(data);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "GoldFind-two-weapons.d2s"));
        Assert.Equal(1u, save.ActiveWeapon);
        Assert.Equal(2, PD2SPCompanion.Core.Characters.WeaponSets.TabInHand(save));

        var look = CharacterFigure.LookOf(save);
        Assert.Equal("1SS", look.WeaponClass);
        Assert.True(look.Parts.ContainsKey("RH") && look.Parts.ContainsKey("LH"));
        var figure = CharacterFigure.Compose(data, palette, look, 0);
        Assert.NotNull(figure);
        Assert.Empty(figure.Missing);

        save.ActiveWeapon = 0;
        var other = CharacterFigure.LookOf(save);
        Assert.Equal(look.WeaponClass, other.WeaponClass);
        Assert.Equal(look.Parts, other.Parts);

        // The weapon put on first - the first of the two in the save - is the right hand's, whichever slot
        // each is in (in game): the other one first, and the hands change places.
        var hands = save.PlayerItemList.Items.Where(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped
            && item.Location is D2SLib.Model.Save.ItemLocation.RightHand or D2SLib.Model.Save.ItemLocation.LeftHand).ToList();
        Assert.Equal(2, hands.Count);
        save.PlayerItemList.Items.Remove(hands[0]);
        save.PlayerItemList.Items.Add(hands[0]);
        var turned = CharacterFigure.LookOf(save);
        Assert.Equal(look.Parts["RH"], turned.Parts["LH"]);
        Assert.Equal(look.Parts["LH"], turned.Parts["RH"]);
    }

    [CorpusFact]
    public void An_edit_shows_before_the_game_saves()
    {
        if (!game.Available)
            return;

        // The helm taken off in memory, as the editor takes one off: the head is bare and plain at once,
        // though the save's appearance bytes still say helm until the game writes them again.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        Assert.NotEqual("LIT", CharacterFigure.LookOf(save).Parts["HD"]);
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped
                                                    && item.Location == D2SLib.Model.Save.ItemLocation.Head);
        var look = CharacterFigure.LookOf(save);
        Assert.Equal("LIT", look.Parts["HD"]);
        Assert.False(look.Tints.ContainsKey("HD"));
    }

    /// <summary>
    /// A circlet is not drawn on the head, and not dyed on it either (the circlet family does not
    /// show on the sprite in game): the game writes no head part and no tint for one - graphic and tint 255 in
    /// ShockSacri, LightFury and SkeleMerc - and the circlets' art is the bare head (alternategfx lit).
    /// </summary>
    [CorpusFact]
    public void A_circlet_leaves_the_head_bare_and_plain()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var helm = save.PlayerItemList.Items.Single(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped
                                                             && item.Location == D2SLib.Model.Save.ItemLocation.Head);
        Assert.True(CharacterFigure.LookOf(save).Tints.ContainsKey("HD"), "the helm has no colour: the test proves nothing");

        var circlet = ItemForge.Unique("Kira's Guardian", "103").Item;
        (circlet.Mode, circlet.Location) = (helm.Mode, helm.Location);
        save.PlayerItemList.Items[save.PlayerItemList.Items.IndexOf(helm)] = circlet;

        var look = CharacterFigure.LookOf(save);
        Assert.Equal("LIT", look.Parts["HD"]);
        Assert.False(look.Tints.ContainsKey("HD"));
    }
    [CorpusFact]
    public void Gear_the_character_cannot_use_is_not_drawn()
    {
        if (!game.Available)
            return;

        // FigureTest's Short Sword asks level 38; at level 30 it is worn and useless - drawn without it,
        // the Barbarian stands empty-handed.
        var (barbarian, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "FigureTest.d2s"));
        Assert.Equal("SSD", CharacterFigure.LookOf(barbarian).Parts["RH"]);
        barbarian.Level = 30;
        var weak = CharacterFigure.LookOf(barbarian);
        Assert.False(weak.Parts.ContainsKey("RH"));
        Assert.Equal("HTH", weak.WeaponClass);

        // His Jawbone Cap on an Amazon: another class's helm, which the game would never let her wear.
        var helm = barbarian.PlayerItemList.Items.First(item => item.Location == D2SLib.Model.Save.ItemLocation.Head);
        var (amazon, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        amazon.PlayerItemList.Items.RemoveAll(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped
                                                      && item.Location == D2SLib.Model.Save.ItemLocation.Head);
        amazon.PlayerItemList.Items.Add(helm);
        Assert.Equal("LIT", CharacterFigure.LookOf(amazon).Parts["HD"]);
    }

    [CorpusFact]
    public void An_automagic_asks_its_level()
    {
        if (!game.Available)
            return;

        // The game's tooltips: Wyrm's Targe - prefix 22, Knight's 42 - asks 42; the
        // Eagle Orbs - prefix 27, Drake's 26 - ask 27.
        var (paladin, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "ShieldPrefix.d2s"));
        var targe = paladin.PlayerItemList.Items.First(item => item.Code.Trim() == "pa1");
        Assert.Equal(42, Requirements.Of(targe, "pal").Level);

        var (sorceress, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "OrbSuffix.d2s"));
        var orb = sorceress.PlayerItemList.Items.First(item => item.Code.Trim() == "ob1");
        Assert.Equal(27, Requirements.Of(orb, "sor").Level);
    }

    [CorpusFact]
    public void Only_an_ethereal_weapon_is_see_through()
    {
        if (!game.Available)
            return;

        // The game's world (screenshot): the ethereal spear see-through, the
        // ethereal armour solid. Every worn item made ethereal: only the weapon's hand is.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        foreach (var item in save.PlayerItemList.Items.Where(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped))
            item.IsEthereal = true;
        Assert.Equal(["RH"], CharacterFigure.LookOf(save).Ethereal.Order());
    }
}
