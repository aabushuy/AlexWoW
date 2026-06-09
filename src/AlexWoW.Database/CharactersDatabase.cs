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

        // ruRU-клиент: склонения имени персонажа (5 падежей). Без них клиент спрашивает каждый вход.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS character_declined_names (
                owner_guid INT UNSIGNED NOT NULL,
                n0 VARCHAR(24) NOT NULL DEFAULT '',
                n1 VARCHAR(24) NOT NULL DEFAULT '',
                n2 VARCHAR(24) NOT NULL DEFAULT '',
                n3 VARCHAR(24) NOT NULL DEFAULT '',
                n4 VARCHAR(24) NOT NULL DEFAULT '',
                PRIMARY KEY (owner_guid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M6.10 (добивка M6.5): персист квестов. status: 0=активен (в журнале), 1=сдан (rewarded).
        // counter0..3 — прогресс целей-существ (kill/talk). Complete/HasItemObjectives рекомпьютятся
        // при входе из quest_template (не храним). PK (owner_guid, quest_id) — один статус на квест.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS character_queststatus (
                owner_guid INT UNSIGNED NOT NULL,
                quest_id   INT UNSIGNED NOT NULL,
                slot       TINYINT UNSIGNED NOT NULL DEFAULT 0,
                status     TINYINT UNSIGNED NOT NULL DEFAULT 0,
                counter0   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
                counter1   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
                counter2   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
                counter3   SMALLINT UNSIGNED NOT NULL DEFAULT 0,
                PRIMARY KEY (owner_guid, quest_id),
                KEY ix_qs_owner (owner_guid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M9.3: изученные спеллы персонажа (стартовые по классу + купленные у тренера).
        // Стартовые спеллы из playercreateinfo_spell в эту таблицу НЕ пишем (выдаём по классу при входе);
        // храним только то, что выучено сверх стартового набора (у тренера). PK (owner, spell).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS character_spell (
                owner_guid INT UNSIGNED NOT NULL,
                spell      INT UNSIGNED NOT NULL,
                PRIMARY KEY (owner_guid, spell),
                KEY ix_spell_owner (owner_guid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M7 #17: ярлыки панелей действий (action buttons). packed_data: action(24)|type(8) — как у клиента.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS character_action (
                owner_guid  INT UNSIGNED NOT NULL,
                button      TINYINT UNSIGNED NOT NULL,
                packed_data INT UNSIGNED NOT NULL,
                PRIMARY KEY (owner_guid, button)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M7 #17: account-data блобы (раскладка панелей, биндинги, макросы, чат, конфиг UI).
        // Сервер — кросс-девайс хранилище: держит СЖАТЫЙ блоб как есть (decompressed_size+zlib) и отдаёт
        // обратно (не распаковывает). owner_id = account_id (глобальные типы) либо guid персонажа
        // (per-character типы), is_char различает. data_type 0..7 (ACCOUNT_DATA_TYPES).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS account_data (
                owner_id    INT UNSIGNED NOT NULL,
                is_char     TINYINT UNSIGNED NOT NULL,
                data_type   TINYINT UNSIGNED NOT NULL,
                update_time INT UNSIGNED NOT NULL DEFAULT 0,
                data        LONGBLOB,
                PRIMARY KEY (owner_id, is_char, data_type)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """);

        // M6.2: деньги персонажа (медь). Стартовый баланс — тестовый (100g), чтобы было что тратить.
        // MySQL не поддерживает ADD COLUMN IF NOT EXISTS — глушим ошибку «дубликат столбца» (1060).
        try
        {
            await db.ExecuteAsync(
                "ALTER TABLE characters ADD COLUMN money INT UNSIGNED NOT NULL DEFAULT 1000000;");
        }
        catch (MySqlException ex) when (ex.Number == 1060) { /* столбец уже есть */ }

        // M9.1: текущий опыт на уровне (xp_for_next_level в player_xp_for_level).
        try
        {
            await db.ExecuteAsync("ALTER TABLE characters ADD COLUMN xp INT UNSIGNED NOT NULL DEFAULT 0;");
        }
        catch (MySqlException ex) when (ex.Number == 1060) { /* столбец уже есть */ }

        // M7 #17: маска видимых доп. панелей (PLAYER_FIELD_BYTES[2]) — персист отображения панелей.
        try
        {
            await db.ExecuteAsync("ALTER TABLE characters ADD COLUMN action_bars TINYINT UNSIGNED NOT NULL DEFAULT 0;");
        }
        catch (MySqlException ex) when (ex.Number == 1060) { /* столбец уже есть */ }
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
                   hair_color AS HairColor, facial_hair AS FacialHair, level AS Level, xp AS Xp,
                   zone AS Zone, map AS Map, position_x AS X, position_y AS Y, position_z AS Z,
                   money AS Money, action_bars AS ActionBars
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
                   hair_color AS HairColor, facial_hair AS FacialHair, level AS Level, xp AS Xp,
                   zone AS Zone, map AS Map, position_x AS X, position_y AS Y, position_z AS Z,
                   money AS Money, action_bars AS ActionBars
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

    /// <summary>Сохраняет 5 склонений имени персонажа (ruRU). Перезаписывает существующие.</summary>
    public async Task SetDeclinedNamesAsync(uint ownerGuid, string[] names, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            REPLACE INTO character_declined_names (owner_guid, n0, n1, n2, n3, n4)
            VALUES (@ownerGuid, @n0, @n1, @n2, @n3, @n4);
            """, new
        {
            ownerGuid,
            n0 = names.ElementAtOrDefault(0) ?? "", n1 = names.ElementAtOrDefault(1) ?? "",
            n2 = names.ElementAtOrDefault(2) ?? "", n3 = names.ElementAtOrDefault(3) ?? "",
            n4 = names.ElementAtOrDefault(4) ?? "",
        });
    }

    /// <summary>
    /// GUID'ы персонажей (из набора), у кого заданы непустые склонения имени. Для флага
    /// CHARACTER_FLAG_DECLINED в SMSG_CHAR_ENUM — иначе ruRU-клиент спрашивает склонения каждый вход. M7 #16.
    /// </summary>
    public async Task<HashSet<uint>> GetGuidsWithDeclinedNamesAsync(
        IReadOnlyCollection<uint> guids, CancellationToken ct = default)
    {
        var result = new HashSet<uint>();
        if (guids.Count == 0)
            return result;
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition("""
            SELECT owner_guid FROM character_declined_names
            WHERE owner_guid IN @guids
              AND (n0 <> '' OR n1 <> '' OR n2 <> '' OR n3 <> '' OR n4 <> '');
            """, new { guids }, cancellationToken: ct));
        foreach (var g in rows)
            result.Add(g);
        return result;
    }

    /// <summary>5 склонений имени персонажа или null, если не заданы.</summary>
    public async Task<string[]?> GetDeclinedNamesAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT n0,n1,n2,n3,n4 FROM character_declined_names WHERE owner_guid = @ownerGuid;",
            new { ownerGuid }, cancellationToken: ct));
        if (row is null) return null;
        var d = (IDictionary<string, object>)row;
        return new[] { d["n0"], d["n1"], d["n2"], d["n3"], d["n4"] }
            .Select(x => x?.ToString() ?? "").ToArray();
    }

    /// <summary>Деньги персонажа (медь). M6.2.</summary>
    public async Task SetMoneyAsync(uint guid, uint money, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("UPDATE characters SET money = @money WHERE guid = @guid;", new { guid, money });
    }

    /// <summary>Сохраняет уровень и текущий опыт персонажа (M9.1 — прокачка).</summary>
    public async Task SetLevelXpAsync(uint guid, byte level, uint xp, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("UPDATE characters SET level = @level, xp = @xp WHERE guid = @guid;",
            new { guid, level, xp });
    }

    /// <summary>Изученные у тренера спеллы персонажа (сверх стартового набора по классу). M9.3.</summary>
    public async Task<IReadOnlyList<uint>> GetLearnedSpellsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT spell FROM character_spell WHERE owner_guid = @ownerGuid;",
            new { ownerGuid }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Сохраняет изученный спелл (идемпотентно — повторное изучение игнорируется). M9.3.</summary>
    public async Task AddLearnedSpellAsync(uint ownerGuid, uint spell, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync(
            "INSERT IGNORE INTO character_spell (owner_guid, spell) VALUES (@ownerGuid, @spell);",
            new { ownerGuid, spell });
    }

    /// <summary>Ярлыки панелей персонажа: button → packed_data. M7 #17.</summary>
    public async Task<IReadOnlyDictionary<byte, uint>> GetActionButtonsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT button AS Button, packed_data AS Packed FROM character_action WHERE owner_guid = @ownerGuid;",
            new { ownerGuid }, cancellationToken: ct));
        var map = new Dictionary<byte, uint>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            map[Convert.ToByte(d["Button"], System.Globalization.CultureInfo.InvariantCulture)] =
                Convert.ToUInt32(d["Packed"], System.Globalization.CultureInfo.InvariantCulture);
        }
        return map;
    }

    /// <summary>Ставит ярлык на кнопку панели (packed=0 → снять). M7 #17.</summary>
    public async Task SetActionButtonAsync(uint ownerGuid, byte button, uint packed, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        if (packed == 0)
        {
            await db.ExecuteAsync("DELETE FROM character_action WHERE owner_guid=@ownerGuid AND button=@button;",
                new { ownerGuid, button });
            return;
        }
        await db.ExecuteAsync("""
            INSERT INTO character_action (owner_guid, button, packed_data) VALUES (@ownerGuid, @button, @packed)
            ON DUPLICATE KEY UPDATE packed_data=@packed;
            """, new { ownerGuid, button, packed });
    }

    /// <summary>Сохраняет маску видимых доп. панелей (PLAYER_FIELD_BYTES[2]). M7 #17.</summary>
    public async Task SetActionBarsAsync(uint guid, byte actionBars, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("UPDATE characters SET action_bars = @actionBars WHERE guid = @guid;",
            new { guid, actionBars });
    }

    /// <summary>Сохранённый блоб account-data (сжатый, как прислал клиент) + время, или null. M7 #17.</summary>
    public async Task<(uint Time, byte[] Data)?> GetAccountDataAsync(uint ownerId, bool isChar, byte dataType, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT update_time AS Time, data AS Data FROM account_data WHERE owner_id=@ownerId AND is_char=@isChar AND data_type=@dataType;",
            new { ownerId, isChar = isChar ? 1 : 0, dataType }, cancellationToken: ct));
        if (row is null)
            return null;
        var d = (IDictionary<string, object>)row;
        var time = Convert.ToUInt32(d["Time"], System.Globalization.CultureInfo.InvariantCulture);
        var data = d["Data"] as byte[] ?? Array.Empty<byte>();
        return (time, data);
    }

    /// <summary>Времена сохранённых блобов owner'а (для SMSG_ACCOUNT_DATA_TIMES): data_type → time. M7 #17.</summary>
    public async Task<IReadOnlyDictionary<byte, uint>> GetAccountDataTimesAsync(uint ownerId, bool isChar, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT data_type AS Type, update_time AS Time FROM account_data WHERE owner_id=@ownerId AND is_char=@isChar;",
            new { ownerId, isChar = isChar ? 1 : 0 }, cancellationToken: ct));
        var map = new Dictionary<byte, uint>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            map[Convert.ToByte(d["Type"], System.Globalization.CultureInfo.InvariantCulture)] =
                Convert.ToUInt32(d["Time"], System.Globalization.CultureInfo.InvariantCulture);
        }
        return map;
    }

    /// <summary>Сохраняет/обновляет блоб account-data (M7 #17).</summary>
    public async Task UpsertAccountDataAsync(uint ownerId, bool isChar, byte dataType, uint time, byte[] data, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO account_data (owner_id, is_char, data_type, update_time, data)
            VALUES (@ownerId, @isChar, @dataType, @time, @data)
            ON DUPLICATE KEY UPDATE update_time=@time, data=@data;
            """, new { ownerId, isChar = isChar ? 1 : 0, dataType, time, data }, cancellationToken: ct));
    }

    /// <summary>Удаляет предмет персонажа по его low-counter GUID (продажа/перемещение). M6.2.</summary>
    public async Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("DELETE FROM character_items WHERE item_guid = @itemGuid;", new { itemGuid });
    }

    /// <summary>Перемещает предмет в другой контейнер/слот. M6.9.</summary>
    public async Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("UPDATE character_items SET bag = @bag, slot = @slot WHERE item_guid = @itemGuid;",
            new { itemGuid, bag, slot });
    }

    /// <summary>Меняет размер стопки предмета. M6.9.</summary>
    public async Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("UPDATE character_items SET stack_count = @stackCount WHERE item_guid = @itemGuid;",
            new { itemGuid, stackCount });
    }

    /// <summary>Статусы квестов персонажа (активные + сданные). M6.10.</summary>
    public async Task<IReadOnlyList<QuestStatusRow>> GetQuestStatusesAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<QuestStatusRow>(new CommandDefinition("""
            SELECT quest_id AS QuestId, slot AS Slot, status AS Status,
                   counter0 AS Counter0, counter1 AS Counter1, counter2 AS Counter2, counter3 AS Counter3
            FROM character_queststatus WHERE owner_guid = @ownerGuid;
            """, new { ownerGuid }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Создаёт/обновляет статус квеста (accept/прогресс/сдача). M6.10.</summary>
    public async Task UpsertQuestStatusAsync(uint ownerGuid, uint questId, byte slot, byte status,
        ushort c0, ushort c1, ushort c2, ushort c3, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync("""
            INSERT INTO character_queststatus
                (owner_guid, quest_id, slot, status, counter0, counter1, counter2, counter3)
            VALUES (@ownerGuid, @questId, @slot, @status, @c0, @c1, @c2, @c3)
            ON DUPLICATE KEY UPDATE
                slot=@slot, status=@status, counter0=@c0, counter1=@c1, counter2=@c2, counter3=@c3;
            """, new { ownerGuid, questId, slot, status, c0, c1, c2, c3 });
    }

    /// <summary>Удаляет персонажа, принадлежащего аккаунту. Возвращает true, если строка удалена.</summary>
    public async Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var affected = await db.ExecuteAsync(
            "DELETE FROM characters WHERE guid = @guid AND account_id = @accountId;",
            new { guid, accountId });
        if (affected > 0)
        {
            await db.ExecuteAsync("DELETE FROM character_items WHERE owner_guid = @guid;", new { guid });
            await db.ExecuteAsync("DELETE FROM character_declined_names WHERE owner_guid = @guid;", new { guid });
            await db.ExecuteAsync("DELETE FROM character_queststatus WHERE owner_guid = @guid;", new { guid });
            await db.ExecuteAsync("DELETE FROM character_spell WHERE owner_guid = @guid;", new { guid });
            await db.ExecuteAsync("DELETE FROM account_data WHERE owner_id = @guid AND is_char = 1;", new { guid });
        }
        return affected > 0;
    }
}
