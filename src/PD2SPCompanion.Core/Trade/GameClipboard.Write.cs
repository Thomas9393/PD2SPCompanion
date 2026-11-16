using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// An item written out as text: PD2's clipboard JSON, the mirror of Paste from game.
/// <para>
/// Two things in one object. <b>The game's shape</b> - quality, type, name, item level, sockets and what is in
/// them, the stats by id and value - as far as the editor can say it: what another tool reads, and what a
/// person reads. And <b>a key of the editor's own</b> (<see cref="OwnKey"/>) holding the whole item as the
/// vault holds one: the game's text has no durability, no affix ids and drops a second stat under one name, so
/// an item made again from it is a likeness; from the key it is the item. Paste reads the key first
/// (<see cref="FromOwnKey"/>) and falls back on the lines where the key is another season's - an item's
/// encoding is its season's - or cannot be read.
/// </para>
/// </summary>
public static partial class GameClipboard
{
    /// <summary>The property of a copied item's text that is the editor's own: the whole item, its season and its stat-id width.</summary>
    public const string OwnKey = "pd2spc";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The item as text. Null, and <paramref name="refused"/> why, where the item cannot be written and read
    /// back the same - the proof a vault deposit passes (<see cref="ItemBlob.TryCapture"/>).
    /// </summary>
    public static string? Write(Item item, int statIdBits, string season, out string? refused)
    {
        // A copy: capturing strips where an item lies, and this one stays where it is.
        if (!ItemBlob.TryCapture(ItemBlob.Copy(item, statIdBits), statIdBits, out byte[] blob, out _, out refused))
            return null;

        var text = Shape(item);
        text[OwnKey] = new JsonObject
        {
            ["season"] = season,
            ["bits"] = statIdBits,
            ["item"] = Convert.ToBase64String(blob),
        };

        return text.ToJsonString(Indented);
    }

    /// <summary>The game's shape of an item, as far as the editor says it. A socketed jewel is the same, nested.</summary>
    private static JsonObject Shape(Item item)
    {
        string code = item.Code.Trim();
        var view = ItemPresenter.Describe(item);

        var text = new JsonObject
        {
            ["quality"] = item.Quality switch
            {
                ItemQuality.Unique => "Unique",
                ItemQuality.Set => "Set",
                ItemQuality.Rare => "Rare",
                ItemQuality.Craft => "Crafted",
                ItemQuality.Magic => "Magic",
                ItemQuality.Superior => "Superior",
                ItemQuality.Inferior => "Low Quality",
                _ => "Normal",
            },
            ["type"] = ItemPresenter.BaseName(code),
        };

        // A unique and a set piece by their row's index, as the game names them; a magic, rare or crafted item
        // by the name shown. A runeword has its word instead, and a plain item none.
        if (item.IsRuneword)
        {
            text["isRuneword"] = true;
            text["runeword"] = view.Name;
        }
        else if (item.Quality is ItemQuality.Unique or ItemQuality.Set)
        {
            text["name"] = ItemPresenter.QualityIndex(item) ?? view.Name;
        }
        else if (item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Craft)
        {
            text["name"] = view.Name;
        }

        text["iLevel"] = item.ItemLevel;
        text["location"] = "Stash";

        if (item.IsEthereal)
            text["isEthereal"] = true;

        if (Stacks.IsStackable(code))
            text["quantity"] = Stacks.Shown(item.Quantity);

        if (item.TotalNumberOfSockets > 0)
            text["sockets"] = (int)item.TotalNumberOfSockets;

        if (item.SocketedItems.Count > 0)
        {
            var inside = new JsonArray();
            foreach (var child in item.SocketedItems)
            {
                var line = ItemTypes.LineageOfBase(child.Code);
                bool rune = line.Contains("rune", StringComparer.OrdinalIgnoreCase);
                bool gem = line.Contains("gem", StringComparer.OrdinalIgnoreCase);

                // A rune and a gem by what they are; a jewel whole, with its own lines.
                var held = rune || gem
                    ? new JsonObject { ["type"] = ItemPresenter.BaseName(child.Code.Trim()), ["quality"] = "Normal", ["iLevel"] = child.ItemLevel }
                    : Shape(child);
                if (rune)
                    held["isRune"] = true;
                else if (gem)
                    held["isGem"] = true;

                inside.Add(held);
            }

            text["socketed"] = inside;
        }

        // Every stat by its id and its number, under the words the game's table gives it - a runeword's and a
        // set's own lists too, which the game leaves to the reader's tables.
        var stats = new JsonArray();
        foreach (var stat in ItemStats.Every(item))
        {
            if (stat.Id is not { } id)
                continue;

            string words = D2SLib.Core.TXT.ItemStatCostTXT[stat.Stat ?? ""]?["descstrpos"]?.Value?.Trim() is { Length: > 0 } key
                ? StringTable.Current?.Get(key) ?? stat.Stat ?? ""
                : stat.Stat ?? "";

            var line = new JsonObject { ["name"] = words, ["stat_id"] = (int)id, ["value"] = stat.Value };
            if (stat.SkillId is { } skill)
                line["skill_id"] = skill;
            if (stat.SkillLevel is { } level)
                line["level"] = level;
            if (stat.MaxCharges is { } most)
                line["max_charges"] = most;
            if (stat.Param is { } param)
                line["param"] = param;

            stats.Add(line);
        }

        if (stats.Count > 0)
            text["stats"] = stats;

        return text;
    }

    /// <summary>
    /// The item under the editor's own key, where the text has one this season wrote: the item itself, not a
    /// likeness made from its lines. Null where there is no key, it is another season's, or it does not read -
    /// the lines are then what there is.
    /// </summary>
    private static Forged? FromOwnKey(JsonElement pasted, string stampAs)
    {
        if (pasted.ValueKind != JsonValueKind.Object || !pasted.TryGetProperty(OwnKey, out var own) || own.ValueKind != JsonValueKind.Object)
            return null;

        if (Text(own, "season") is not { } season || !Vault.TryInstalledSeason(out string now, out _) || !Same(season, now))
            return null;

        if (Number(own, "bits") is not { } bits || Text(own, "item") is not { Length: > 0 } packed)
            return null;

        try
        {
            var item = ItemBlob.Restore(Convert.FromBase64String(packed), bits);

            // A copy is a new item, and a new item is never a legacy one: a copy of one is fitted to this season as an edit
            // makes it (ItemSeason.MakeCurrent), its numbers and its runeword's string. By stamp alone: one the repair marked
            // keeps its record by its id, which this copy still has, and the record is the original's.
            if (LegacyRecord.SeasonOf(item) is null)
                ItemSeason.MakeCurrent(item);
            item.Version = stampAs;
            return new Forged(item, [], true);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
