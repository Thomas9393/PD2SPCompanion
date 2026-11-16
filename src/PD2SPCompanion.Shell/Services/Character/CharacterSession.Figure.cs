using D2SLib.Model.Save;
using PD2SPCompanion.Core.Diagnostics;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.Services.Character;

public sealed partial class CharacterSession
{
    /// <summary>What the open card's figure was last drawn from, so a rebuild that changed no gear draws nothing.</summary>
    private string _figureDrawnFrom = "";

    /// <summary>A fingerprint of what the figure depends on, as of the last rebuild that worked the look out.</summary>
    private int _figureInputs;

    /// <summary>
    /// The open character's figure in its gear, from the save in
    /// memory: a helm put on in the editor shows on the card before anything is saved. Asked after every
    /// rebuild of the grids; works the look out again only when what it depends on changed, and composes
    /// again only when the look did.
    /// </summary>
    private void ShowFigureSoon()
    {
        if (OpenCard is not { } card || _save is not { } save)
            return;

        // Measured: 0.4-0.7 ms when nothing the figure depends on changed, 4-5 ms when it did.
        int inputs = FigureInputs(save);
        if (inputs == _figureInputs && card.Figure is not null)
            return;
        _figureInputs = inputs;

        FigureLook look;
        try
        {
            look = CharacterFigure.LookOf(save);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            card.AwaitsFigure = false;
            return;
        }

        string key = $"{card.Path}|{look.Token}|{look.WeaponClass}|"
                     + string.Join(",", look.Parts.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) + "|"
                     + string.Join(",", look.Tints.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value.Table}/{t.Value.Colour}")) + "|"
                     + string.Join(",", look.Ethereal.Order());
        if (key == _figureDrawnFrom && card.Figure is not null)
            return;
        _figureDrawnFrom = key;
        _ = ShowFigureAsync(card, look, key);
    }

    /// <summary>
    /// What the figure depends on, cheaply: the worn items and the inventory's charms as they would be
    /// written (their strength and dexterity count toward what can be used), the class, level, base
    /// strength and dexterity, and the weapon set in hand. Every move on the inventory page - the stash,
    /// the cube, the shared stash, a store to the vault - rebuilds the tiles, and working the look out
    /// each time cost 3-4 ms on the UI thread for a figure that had not changed.
    /// </summary>
    private static int FigureInputs(D2S save)
    {
        var inputs = new HashCode();
        inputs.Add(save.ClassId);
        inputs.Add(save.Level);
        inputs.Add(save.ActiveWeapon);
        inputs.Add(save.Attributes.Stats.GetValueOrDefault("strength"));
        inputs.Add(save.Attributes.Stats.GetValueOrDefault("dexterity"));
        foreach (var item in save.PlayerItemList.Items)
        {
            bool worn = item.Mode == ItemMode.Equipped;
            bool charm = item.Mode == ItemMode.Stored && item.Page == PD2SPCompanion.Core.Saves.InventoryLayout.InventoryPage
                         && PD2SPCompanion.Core.Characters.Requirements.IsCharm(item);
            if (!worn && !charm)
                continue;
            inputs.Add((int)item.Location);
            inputs.AddBytes(Item.Write(item, save.Header.Version));
        }
        return inputs.ToHashCode();
    }

    private async Task ShowFigureAsync(PD2SPCompanion.Shell.ViewModels.CharacterCard card, FigureLook look, string key)
    {
        // The one large card: enlarged twice, the smoothest.
        var figure = await FigureBitmaps.ComposeAsync(look, smooth: 2);
        // Another look asked for meanwhile, or another character opened: this one is not wanted.
        if (!ReferenceEquals(OpenCard, card) || _figureDrawnFrom != key)
            return;
        if (figure is not null)
            card.Figure = figure;
        card.AwaitsFigure = false;
    }
}
