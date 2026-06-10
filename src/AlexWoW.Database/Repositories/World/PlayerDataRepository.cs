using System.Globalization;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>
/// Статические данные игрока из БД мира: стартовый набор (playercreateinfo_item/spell) + прогрессия
/// (player_levelstats/classlevelstats/xp_for_level). SRP-репозиторий (#25), Dapper read-only.
/// </summary>
public sealed class PlayerDataRepository(string connectionString)
    : MangosRepositoryBase(connectionString), IPlayerDataRepository
{
    public async Task<IReadOnlyList<StartingItem>> GetStartingItemsAsync(byte race, byte cls, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<StartingItem>(new CommandDefinition("""
            SELECT p.itemid AS ItemId, p.amount AS Amount,
                   t.InventoryType AS InventoryType, t.stackable AS Stackable
            FROM playercreateinfo_item p
            JOIN item_template t ON t.entry = p.itemid
            WHERE p.race = @race AND p.class = @cls;
            """, new { race, cls }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<uint>> GetStartSpellsAsync(byte race, byte cls, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT Spell FROM playercreateinfo_spell WHERE race = @race AND class = @cls;",
            new { race, cls }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyDictionary<(byte Class, byte Level), (uint Hp, uint Mana)>>
        GetClassLevelStatsAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT class, level, basehp, basemana FROM player_classlevelstats;", cancellationToken: ct));
        var map = new Dictionary<(byte, byte), (uint, uint)>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var cls = Convert.ToByte(d["class"], CultureInfo.InvariantCulture);
            var lvl = Convert.ToByte(d["level"], CultureInfo.InvariantCulture);
            map[(cls, lvl)] = (Convert.ToUInt32(d["basehp"], CultureInfo.InvariantCulture),
                               Convert.ToUInt32(d["basemana"], CultureInfo.InvariantCulture));
        }
        return map;
    }

    public async Task<IReadOnlyDictionary<(byte Race, byte Class, byte Level), (uint Str, uint Agi, uint Sta, uint Int, uint Spi)>>
        GetLevelStatsAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT race, class, level, str, agi, sta, inte, spi FROM player_levelstats;", cancellationToken: ct));
        var map = new Dictionary<(byte, byte, byte), (uint, uint, uint, uint, uint)>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var race = Convert.ToByte(d["race"], CultureInfo.InvariantCulture);
            var cls = Convert.ToByte(d["class"], CultureInfo.InvariantCulture);
            var lvl = Convert.ToByte(d["level"], CultureInfo.InvariantCulture);
            map[(race, cls, lvl)] = (
                Convert.ToUInt32(d["str"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["agi"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["sta"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["inte"], CultureInfo.InvariantCulture),
                Convert.ToUInt32(d["spi"], CultureInfo.InvariantCulture));
        }
        return map;
    }

    public async Task<IReadOnlyDictionary<uint, uint>> GetXpForLevelTableAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT lvl, xp_for_next_level FROM player_xp_for_level;", cancellationToken: ct));
        var map = new Dictionary<uint, uint>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var lvl = Convert.ToUInt32(d["lvl"], CultureInfo.InvariantCulture);
            map[lvl] = Convert.ToUInt32(d["xp_for_next_level"], CultureInfo.InvariantCulture);
        }
        return map;
    }
}
