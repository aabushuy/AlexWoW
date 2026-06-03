using AlexWoW.Database.Models;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database;

/// <summary>Доступ к данным персонажей (таблица characters).</summary>
public sealed class CharactersDatabase(string connectionString)
{
    private readonly string _connectionString = connectionString;

    /// <summary>Максимум персонажей на аккаунт (на реалм).</summary>
    public const int MaxCharactersPerAccount = 10;

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS characters (
                guid        INT UNSIGNED NOT NULL AUTO_INCREMENT,
                account_id  INT UNSIGNED NOT NULL,
                name        VARCHAR(12)  NOT NULL,
                race        TINYINT UNSIGNED NOT NULL,
                class       TINYINT UNSIGNED NOT NULL,
                gender      TINYINT UNSIGNED NOT NULL,
                skin        TINYINT UNSIGNED NOT NULL,
                face        TINYINT UNSIGNED NOT NULL,
                hair_style  TINYINT UNSIGNED NOT NULL,
                hair_color  TINYINT UNSIGNED NOT NULL,
                facial_hair TINYINT UNSIGNED NOT NULL,
                level       TINYINT UNSIGNED NOT NULL DEFAULT 1,
                zone        INT UNSIGNED NOT NULL DEFAULT 0,
                map         INT UNSIGNED NOT NULL DEFAULT 0,
                position_x  FLOAT NOT NULL DEFAULT 0,
                position_y  FLOAT NOT NULL DEFAULT 0,
                position_z  FLOAT NOT NULL DEFAULT 0,
                created_at  TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (guid),
                UNIQUE KEY uk_characters_name (name),
                KEY ix_characters_account (account_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M6.1: инвентарь персонажа. Слоты экипировки 0..18, сумки 19..22, рюкзак 23..38.
        // bag = 255 (INVENTORY_SLOT_BAG_0) — основной контейнер. item_guid — low-counter GUID предмета.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS character_items (
                item_guid   INT UNSIGNED NOT NULL AUTO_INCREMENT,
                owner_guid  INT UNSIGNED NOT NULL,
                item_entry  INT UNSIGNED NOT NULL,
                bag         TINYINT UNSIGNED NOT NULL DEFAULT 255,
                slot        TINYINT UNSIGNED NOT NULL,
                stack_count INT UNSIGNED NOT NULL DEFAULT 1,
                PRIMARY KEY (item_guid),
                KEY ix_items_owner (owner_guid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);
    }

    /// <summary>Есть ли у персонажа хоть один предмет (для выдачи стартового набора голым персонажам).</summary>
    public async Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var count = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM character_items WHERE owner_guid = @ownerGuid;", new { ownerGuid });
        return count > 0;
    }

    /// <summary>Инвентарь персонажа (все предметы во всех слотах).</summary>
    public async Task<IReadOnlyList<InventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<InventoryItem>(new CommandDefinition("""
            SELECT item_guid AS ItemGuid, owner_guid AS OwnerGuid, item_entry AS ItemEntry,
                   bag AS Bag, slot AS Slot, stack_count AS StackCount
            FROM character_items WHERE owner_guid = @ownerGuid ORDER BY bag, slot;
            """, new { ownerGuid }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Кладёт предмет в слот. Возвращает low-counter GUID нового предмета.</summary>
    public async Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot,
        uint stackCount = 1, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var guid = await db.ExecuteScalarAsync<ulong>("""
            INSERT INTO character_items (owner_guid, item_entry, bag, slot, stack_count)
            VALUES (@ownerGuid, @itemEntry, @bag, @slot, @stackCount);
            SELECT LAST_INSERT_ID();
            """, new { ownerGuid, itemEntry, bag, slot, stackCount });
        return (uint)guid;
    }

    public async Task<IReadOnlyList<Character>> GetByAccountAsync(uint accountId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<Character>("""
            SELECT guid AS Guid, account_id AS AccountId, name AS Name, race AS Race, class AS Class,
                   gender AS Gender, skin AS Skin, face AS Face, hair_style AS HairStyle,
                   hair_color AS HairColor, facial_hair AS FacialHair, level AS Level,
                   zone AS Zone, map AS Map, position_x AS X, position_y AS Y, position_z AS Z
            FROM characters WHERE account_id = @accountId ORDER BY guid;
            """, new { accountId });
        return rows.AsList();
    }

    public async Task<Character?> GetByGuidAsync(uint guid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<Character>("""
            SELECT guid AS Guid, account_id AS AccountId, name AS Name, race AS Race, class AS Class,
                   gender AS Gender, skin AS Skin, face AS Face, hair_style AS HairStyle,
                   hair_color AS HairColor, facial_hair AS FacialHair, level AS Level,
                   zone AS Zone, map AS Map, position_x AS X, position_y AS Y, position_z AS Z
            FROM characters WHERE guid = @guid;
            """, new { guid });
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var count = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM characters WHERE name = @name;", new { name });
        return count > 0;
    }

    public async Task<int> CountByAccountAsync(uint accountId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return (int)await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM characters WHERE account_id = @accountId;", new { accountId });
    }

    public async Task<uint> CreateAsync(Character character, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var guid = await db.ExecuteScalarAsync<ulong>("""
            INSERT INTO characters
                (account_id, name, race, class, gender, skin, face, hair_style, hair_color, facial_hair,
                 level, zone, map, position_x, position_y, position_z)
            VALUES
                (@AccountId, @Name, @Race, @Class, @Gender, @Skin, @Face, @HairStyle, @HairColor, @FacialHair,
                 @Level, @Zone, @Map, @X, @Y, @Z);
            SELECT LAST_INSERT_ID();
            """, character);
        return (uint)guid;
    }

    public async Task SavePositionAsync(uint guid, float x, float y, float z, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            UPDATE characters SET position_x = @x, position_y = @y, position_z = @z WHERE guid = @guid;
            """, new { guid, x, y, z });
    }

    /// <summary>Удаляет персонажа, принадлежащего аккаунту. Возвращает true, если строка удалена.</summary>
    public async Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var affected = await db.ExecuteAsync(
            "DELETE FROM characters WHERE guid = @guid AND account_id = @accountId;",
            new { guid, accountId });
        if (affected > 0)
            await db.ExecuteAsync("DELETE FROM character_items WHERE owner_guid = @guid;", new { guid });
        return affected > 0;
    }
}
