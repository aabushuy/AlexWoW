using System.Globalization;
using AlexWoW.Database.Models;
using Dapper;
using MySqlConnector;

namespace AlexWoW.Database;

/// <summary>
/// Доступ к статической БД мира (дамп CMaNGOS-WotLK: creature, creature_template …).
/// Только чтение. Координаты в дампе — decimal(40,20); приводим к DOUBLE на стороне MySQL.
/// </summary>
public sealed class WorldDatabase(string connectionString)
{
    private readonly string _connectionString = connectionString;

    /// <summary>
    /// Кастомные/тестовые NPC CMaNGOS, которых нет в ретейле (арена-организаторы и пр.) —
    /// заспавнены в каждом городе и часто «парят». Не показываем игрокам.
    /// </summary>
    private static readonly int[] ExcludedCreatureEntries =
    {
        26012, // Arena Organizer
        26075, // Paymaster
        26760, // Fight Promoter (Arena Battlemaster's Assistant)
    };

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>Проверка доступности БД мира при старте (есть ли таблица creature).</summary>
    public async Task<long> CountCreaturesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM creature;");
    }

    /// <summary>Спавны существ на карте в квадрате ±range от точки (грубая зона видимости).</summary>
    public async Task<IReadOnlyList<CreatureSpawnData>> GetCreaturesNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<CreatureSpawnData>(new CommandDefinition("""
            SELECT c.guid AS Guid, c.id AS Entry,
                   CAST(c.position_x AS DOUBLE) AS X, CAST(c.position_y AS DOUBLE) AS Y,
                   CAST(c.position_z AS DOUBLE) AS Z, CAST(c.orientation AS DOUBLE) AS O,
                   t.Name, t.SubName,
                   t.DisplayId1, t.DisplayId2, t.DisplayId3, t.DisplayId4,
                   t.Faction, t.MinLevel, t.MaxLevel, t.CreatureType, t.NpcFlags, t.UnitClass, t.Scale
            FROM creature c
            JOIN creature_template t ON t.Entry = c.id
            WHERE c.map = @map
              AND (c.spawnMask & 1) = 1
              AND t.Name NOT LIKE '%[%'      -- дев/плейсхолдеры в имени где угодно: [DND]/[PH]/[UNUSED]
              -- TAR-тестовые тренеры/бистмастер: generic-имя без subname (у настоящих имя — личное)
              AND NOT (t.Name LIKE '% Trainer' AND COALESCE(t.SubName, '') = '')
              AND NOT (t.Name = 'Beastmaster' AND COALESCE(t.SubName, '') = '')
              AND COALESCE(t.SubName, '') NOT LIKE 'Arena %'   -- арена-NPC по subname
              AND NOT (t.Name LIKE '%Vendor%' AND COALESCE(t.SubName, '') = '') -- generic-вендоры без имени
              AND c.id NOT IN @excluded     -- кастомные арена-NPC CMaNGOS (по entry)
              -- Ивентовый контент (Ярмарка Новолуния, Новогодье и пр.): системы game_event у нас нет,
              -- иначе он спавнится ПОСТОЯННО — прячем спавны, активные только во время ивента (event>0).
              AND NOT EXISTS (SELECT 1 FROM game_event_creature gec WHERE gec.guid = c.guid AND gec.event > 0)
              AND c.position_x BETWEEN @minX AND @maxX
              AND c.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit,
                  excluded = ExcludedCreatureEntries },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Шаблон существа по entry (для CMSG_CREATURE_QUERY).</summary>
    public async Task<CreatureTemplateData?> GetCreatureTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CreatureTemplateData>(new CommandDefinition("""
            SELECT Entry, Name, SubName, DisplayId1, Faction, MinLevel, CreatureType, NpcFlags, UnitClass, Scale
            FROM creature_template WHERE Entry = @entry;
            """, new { entry }, cancellationToken: ct));
    }

    /// <summary>
    /// Стартовый набор предметов по расе/классу (playercreateinfo_item ⨝ item_template).
    /// В CMaNGOS-дампе таблица наполнена офлайн из CharStartOutfit.dbc (см. tools/MapExtractor).
    /// </summary>
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

    /// <summary>displayid + InventoryType по набору entry (для paperdoll в SMSG_CHAR_ENUM).</summary>
    public async Task<IReadOnlyDictionary<uint, (uint DisplayId, byte InventoryType)>> GetItemDisplaysAsync(
        IReadOnlyCollection<uint> entries, CancellationToken ct = default)
    {
        var result = new Dictionary<uint, (uint, byte)>();
        if (entries.Count == 0)
            return result;

        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync(new CommandDefinition(
            "SELECT entry AS Entry, displayid AS DisplayId, InventoryType FROM item_template WHERE entry IN @entries;",
            new { entries }, cancellationToken: ct));
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object>)row;
            var entry = Convert.ToUInt32(d["Entry"], CultureInfo.InvariantCulture);
            var displayId = Convert.ToUInt32(d["DisplayId"], CultureInfo.InvariantCulture);
            var invType = Convert.ToByte(d["InventoryType"], CultureInfo.InvariantCulture);
            result[entry] = (displayId, invType);
        }
        return result;
    }

    /// <summary>Ассортимент вендора по entry существа (npc_vendor ⨝ item_template). Только за золото
    /// (ExtendedCost=0), без условий. M6.2.</summary>
    public async Task<IReadOnlyList<VendorItem>> GetVendorItemsAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        // Ассортимент может быть задан напрямую (npc_vendor по entry существа) ИЛИ через шаблон
        // (npc_vendor_template по creature_template.VendorTemplateId) — объединяем оба источника.
        var rows = await db.QueryAsync<VendorItem>(new CommandDefinition("""
            SELECT v.slot AS Slot, v.item AS ItemId, v.maxcount AS MaxCount,
                   t.BuyPrice AS BuyPrice, t.displayid AS DisplayId, t.MaxDurability AS MaxDurability,
                   t.BuyCount AS BuyCount, t.name AS Name, t.stackable AS Stackable
            FROM (
                SELECT slot, item, maxcount, ExtendedCost, condition_id
                FROM npc_vendor WHERE entry = @entry
                UNION ALL
                SELECT nvt.slot, nvt.item, nvt.maxcount, nvt.ExtendedCost, nvt.condition_id
                FROM npc_vendor_template nvt
                JOIN creature_template ct ON ct.VendorTemplateId = nvt.entry
                WHERE ct.entry = @entry AND ct.VendorTemplateId <> 0
            ) v
            JOIN item_template t ON t.entry = v.item
            WHERE v.ExtendedCost = 0 AND COALESCE(v.condition_id, 0) = 0
            ORDER BY v.slot;
            """, new { entry }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Лут-определение существа (M6.6): деньги (creature_template.MinLootGold/MaxLootGold) + кандидаты-
    /// предметы из creature_loot_template по LootId существа ⨝ item_template (для displayid). Только обычные
    /// предметы: ChanceOrQuestChance &gt; 0 (квест-предметы &lt; 0 — позже) и mincountOrRef &gt; 0
    /// (отрицательное — ссылка на reference_loot_template, пропускаем). Возвращает null, если у существа нет лута.
    /// </summary>
    public async Task<CreatureLootData?> GetCreatureLootAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);

        var head = await db.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT LootId, MinLootGold, MaxLootGold FROM creature_template WHERE Entry = @creatureEntry;",
            new { creatureEntry }, cancellationToken: ct));
        if (head is null)
            return null;
        var h = (IDictionary<string, object>)head;
        var lootId = Convert.ToUInt32(h["LootId"], CultureInfo.InvariantCulture);
        var minGold = Convert.ToUInt32(h["MinLootGold"], CultureInfo.InvariantCulture);
        var maxGold = Convert.ToUInt32(h["MaxLootGold"], CultureInfo.InvariantCulture);

        IReadOnlyList<CreatureLootEntry> drops = [];
        if (lootId != 0)
        {
            var rows = await db.QueryAsync<CreatureLootEntry>(new CommandDefinition("""
                SELECT lt.item AS ItemId, lt.ChanceOrQuestChance AS Chance,
                       lt.mincountOrRef AS MinCount, lt.maxcount AS MaxCount, it.displayid AS DisplayId
                FROM creature_loot_template lt
                JOIN item_template it ON it.entry = lt.item
                WHERE lt.entry = @lootId AND lt.ChanceOrQuestChance > 0 AND lt.mincountOrRef > 0;
                """, new { lootId }, cancellationToken: ct));
            drops = rows.AsList();
        }

        if (maxGold == 0 && drops.Count == 0)
            return null; // нечего лутать
        return new CreatureLootData { MinGold = minGold, MaxGold = maxGold, Drops = drops };
    }

    /// <summary>
    /// Все реакции фракций (таблица faction_template, наполнена офлайн из FactionTemplate.dbc —
    /// см. tools/MapExtractor factiontemplate). Для серверного авто-агро (M6.7). Если таблицы нет —
    /// бросает (вызывающий трактует как «авто-агро выключено»).
    /// </summary>
    public async Task<IReadOnlyList<FactionTemplateRow>> GetFactionTemplatesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<FactionTemplateRow>(new CommandDefinition(
            "SELECT id AS Id, faction AS Faction, ourMask AS OurMask, friendMask AS FriendMask, hostileMask AS HostileMask, "
            + "enemy1 AS Enemy1, enemy2 AS Enemy2, enemy3 AS Enemy3, enemy4 AS Enemy4, "
            + "friend1 AS Friend1, friend2 AS Friend2, friend3 AS Friend3, friend4 AS Friend4 FROM faction_template;",
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Entry существ, дающих квесты (distinct creature_questrelation.id) — для иконок «!». M6.5.</summary>
    public async Task<IReadOnlyList<uint>> GetQuestGiverEntriesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT DISTINCT id FROM creature_questrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Entry существ, принимающих квесты (distinct creature_involvedrelation.id) — для иконок «?». M6.5.</summary>
    public async Task<IReadOnlyList<uint>> GetQuestEnderEntriesAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT DISTINCT id FROM creature_involvedrelation;", cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Квесты, которые даёт существо (creature_questrelation ⨝ quest_template). M6.5.</summary>
    public async Task<IReadOnlyList<GiverQuest>> GetGiverQuestsAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<GiverQuest>(new CommandDefinition("""
            SELECT q.entry AS QuestId, q.QuestLevel AS QuestLevel, q.QuestFlags AS QuestFlags, q.Title AS Title,
                   q.MinLevel AS MinLevel, q.RequiredRaces AS RequiredRaces, q.RequiredClasses AS RequiredClasses,
                   q.PrevQuestId AS PrevQuestId
            FROM creature_questrelation r JOIN quest_template q ON q.entry = r.quest
            WHERE r.id = @creatureEntry ORDER BY q.entry;
            """, new { creatureEntry }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Id квестов, которые ПРИНИМАЕТ существо (creature_involvedrelation). Для сдачи. M6.5.</summary>
    public async Task<IReadOnlyList<uint>> GetEnderQuestIdsAsync(uint creatureEntry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<uint>(new CommandDefinition(
            "SELECT quest FROM creature_involvedrelation WHERE id = @creatureEntry;",
            new { creatureEntry }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Полный шаблон квеста (quest_template) — детали/награды/цели. M6.5.</summary>
    public async Task<QuestTemplateData?> GetQuestAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = (IDictionary<string, object>?)await db.QueryFirstOrDefaultAsync(new CommandDefinition(
            "SELECT * FROM quest_template WHERE entry = @entry;", new { entry }, cancellationToken: ct));
        return row is null ? null : MapQuest(row);
    }

    private static QuestTemplateData MapQuest(IDictionary<string, object> r)
    {
        static uint U(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToUInt32(v, CultureInfo.InvariantCulture) : 0u;
        static int I(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        static string S(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

        return new QuestTemplateData
        {
            Entry = U(r, "entry"),
            QuestLevel = I(r, "QuestLevel"),
            MinLevel = I(r, "MinLevel"),
            ZoneOrSort = I(r, "ZoneOrSort"),
            Type = U(r, "Type"),
            Method = U(r, "Method"),
            SrcItemId = U(r, "SrcItemId"),
            NextQuestId = U(r, "NextQuestId"),
            QuestFlags = U(r, "QuestFlags"),
            SuggestedPlayers = U(r, "SuggestedPlayers"),
            Title = S(r, "Title"),
            Details = S(r, "Details"),
            Objectives = S(r, "Objectives"),
            OfferRewardText = S(r, "OfferRewardText"),
            RequestItemsText = S(r, "RequestItemsText"),
            EndText = S(r, "EndText"),
            RewOrReqMoney = I(r, "RewOrReqMoney"),
            RewSpell = U(r, "RewSpell"),
            RewSpellCast = U(r, "RewSpellCast"),
            RewItemId = [U(r, "RewItemId1"), U(r, "RewItemId2"), U(r, "RewItemId3"), U(r, "RewItemId4")],
            RewItemCount = [U(r, "RewItemCount1"), U(r, "RewItemCount2"), U(r, "RewItemCount3"), U(r, "RewItemCount4")],
            RewChoiceItemId = [U(r, "RewChoiceItemId1"), U(r, "RewChoiceItemId2"), U(r, "RewChoiceItemId3"),
                               U(r, "RewChoiceItemId4"), U(r, "RewChoiceItemId5"), U(r, "RewChoiceItemId6")],
            RewChoiceItemCount = [U(r, "RewChoiceItemCount1"), U(r, "RewChoiceItemCount2"), U(r, "RewChoiceItemCount3"),
                                  U(r, "RewChoiceItemCount4"), U(r, "RewChoiceItemCount5"), U(r, "RewChoiceItemCount6")],
            ReqCreatureOrGoId = [I(r, "ReqCreatureOrGOId1"), I(r, "ReqCreatureOrGOId2"), I(r, "ReqCreatureOrGOId3"), I(r, "ReqCreatureOrGOId4")],
            ReqCreatureOrGoCount = [U(r, "ReqCreatureOrGOCount1"), U(r, "ReqCreatureOrGOCount2"), U(r, "ReqCreatureOrGOCount3"), U(r, "ReqCreatureOrGOCount4")],
            ReqItemId = [U(r, "ReqItemId1"), U(r, "ReqItemId2"), U(r, "ReqItemId3"), U(r, "ReqItemId4"), U(r, "ReqItemId5"), U(r, "ReqItemId6")],
            ReqItemCount = [U(r, "ReqItemCount1"), U(r, "ReqItemCount2"), U(r, "ReqItemCount3"), U(r, "ReqItemCount4"), U(r, "ReqItemCount5"), U(r, "ReqItemCount6")],
            ObjectiveText = [S(r, "ObjectiveText1"), S(r, "ObjectiveText2"), S(r, "ObjectiveText3"), S(r, "ObjectiveText4")],
        };
    }

    /// <summary>Полный шаблон предмета (item_template) для SMSG_ITEM_QUERY_SINGLE_RESPONSE.</summary>
    public async Task<ItemTemplateData?> GetItemTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var row = (IDictionary<string, object>?)await db.QueryFirstOrDefaultAsync(new CommandDefinition(
            "SELECT * FROM item_template WHERE entry = @entry;", new { entry }, cancellationToken: ct));
        return row is null ? null : MapItemTemplate(row);
    }

    private static ItemTemplateData MapItemTemplate(IDictionary<string, object> r)
    {
        static uint U(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToUInt32(v, CultureInfo.InvariantCulture) : 0u;
        static int I(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        static float F(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? Convert.ToSingle(v, CultureInfo.InvariantCulture) : 0f;
        static string S(IDictionary<string, object> r, string k)
            => r.TryGetValue(k, out var v) && v is not null ? v.ToString() ?? string.Empty : string.Empty;

        var statCount = U(r, "StatsCount");
        var stats = new ItemStat[Math.Min(statCount, 10u)];
        for (var i = 0; i < stats.Length; i++)
            stats[i] = new ItemStat(U(r, $"stat_type{i + 1}"), I(r, $"stat_value{i + 1}"));

        var damages = new ItemDamage[2];
        for (var i = 0; i < 2; i++)
            damages[i] = new ItemDamage(F(r, $"dmg_min{i + 1}"), F(r, $"dmg_max{i + 1}"), U(r, $"dmg_type{i + 1}"));

        var spells = new ItemSpell[5];
        for (var i = 0; i < 5; i++)
            spells[i] = new ItemSpell(U(r, $"spellid_{i + 1}"), U(r, $"spelltrigger_{i + 1}"),
                I(r, $"spellcharges_{i + 1}"), I(r, $"spellcooldown_{i + 1}"),
                U(r, $"spellcategory_{i + 1}"), I(r, $"spellcategorycooldown_{i + 1}"));

        var sockets = new ItemSocket[3];
        for (var i = 0; i < 3; i++)
            sockets[i] = new ItemSocket(U(r, $"socketColor_{i + 1}"), U(r, $"socketContent_{i + 1}"));

        return new ItemTemplateData
        {
            Entry = U(r, "entry"),
            Class = U(r, "class"),
            SubClass = U(r, "subclass"),
            SoundOverrideSubclass = I(r, "unk0"),
            Name = S(r, "name"),
            DisplayId = U(r, "displayid"),
            Quality = U(r, "Quality"),
            Flags = U(r, "Flags"),
            Flags2 = U(r, "Flags2"),
            BuyPrice = U(r, "BuyPrice"),
            SellPrice = U(r, "SellPrice"),
            InventoryType = U(r, "InventoryType"),
            AllowableClass = I(r, "AllowableClass"),
            AllowableRace = I(r, "AllowableRace"),
            ItemLevel = U(r, "ItemLevel"),
            RequiredLevel = U(r, "RequiredLevel"),
            RequiredSkill = U(r, "RequiredSkill"),
            RequiredSkillRank = U(r, "RequiredSkillRank"),
            RequiredSpell = U(r, "requiredspell"),
            RequiredHonorRank = U(r, "requiredhonorrank"),
            RequiredCityRank = U(r, "RequiredCityRank"),
            RequiredReputationFaction = U(r, "RequiredReputationFaction"),
            RequiredReputationRank = U(r, "RequiredReputationRank"),
            MaxCount = I(r, "maxcount"),
            Stackable = I(r, "stackable"),
            ContainerSlots = U(r, "ContainerSlots"),
            Stats = stats,
            ScalingStatDistribution = I(r, "ScalingStatDistribution"),
            ScalingStatValue = U(r, "ScalingStatValue"),
            Damages = damages,
            Armor = I(r, "armor"),
            HolyRes = I(r, "holy_res"),
            FireRes = I(r, "fire_res"),
            NatureRes = I(r, "nature_res"),
            FrostRes = I(r, "frost_res"),
            ShadowRes = I(r, "shadow_res"),
            ArcaneRes = I(r, "arcane_res"),
            Delay = U(r, "delay"),
            AmmoType = U(r, "ammo_type"),
            RangedModRange = F(r, "RangedModRange"),
            Spells = spells,
            Bonding = U(r, "bonding"),
            Description = S(r, "description"),
            PageText = U(r, "PageText"),
            LanguageId = U(r, "LanguageID"),
            PageMaterial = U(r, "PageMaterial"),
            StartQuest = U(r, "startquest"),
            LockId = U(r, "lockid"),
            Material = I(r, "Material"),
            Sheath = U(r, "sheath"),
            RandomProperty = U(r, "RandomProperty"),
            RandomSuffix = U(r, "RandomSuffix"),
            Block = U(r, "block"),
            ItemSet = U(r, "itemset"),
            MaxDurability = U(r, "MaxDurability"),
            Area = U(r, "area"),
            Map = I(r, "Map"),
            BagFamily = I(r, "BagFamily"),
            TotemCategory = I(r, "TotemCategory"),
            Sockets = sockets,
            SocketBonus = U(r, "socketBonus"),
            GemProperties = U(r, "GemProperties"),
            RequiredDisenchantSkill = I(r, "RequiredDisenchantSkill"),
            ArmorDamageModifier = F(r, "ArmorDamageModifier"),
            Duration = U(r, "Duration"),
            ItemLimitCategory = I(r, "ItemLimitCategory"),
            HolidayId = U(r, "HolidayId"),
        };
    }

    /// <summary>Видимые гейм-объекты на карте в квадрате ±range (только с моделью: displayId &lt;&gt; 0).</summary>
    public async Task<IReadOnlyList<GameObjectSpawnData>> GetGameObjectsNearAsync(
        uint map, float x, float y, float range, int limit, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        var rows = await db.QueryAsync<GameObjectSpawnData>(new CommandDefinition("""
            SELECT g.guid AS Guid, g.id AS Entry,
                   CAST(g.position_x AS DOUBLE) AS X, CAST(g.position_y AS DOUBLE) AS Y,
                   CAST(g.position_z AS DOUBLE) AS Z, CAST(g.orientation AS DOUBLE) AS O,
                   CAST(g.rotation0 AS DOUBLE) AS Rot0, CAST(g.rotation1 AS DOUBLE) AS Rot1,
                   CAST(g.rotation2 AS DOUBLE) AS Rot2, CAST(g.rotation3 AS DOUBLE) AS Rot3,
                   t.name AS Name, t.type AS Type, t.displayId AS DisplayId,
                   t.faction AS Faction, t.flags AS Flags, t.size AS Size
            FROM gameobject g
            JOIN gameobject_template t ON t.entry = g.id
            WHERE g.map = @map
              AND (g.spawnMask & 1) = 1
              AND t.displayId <> 0
              AND t.name NOT LIKE '%[%'      -- дев/плейсхолдеры в имени где угодно
              -- Ивентовый контент (венки Новогодья, тенты/декор Ярмарки Новолуния и пр.) — прячем event>0.
              AND NOT EXISTS (SELECT 1 FROM game_event_gameobject geg WHERE geg.guid = g.guid AND geg.event > 0)
              AND g.position_x BETWEEN @minX AND @maxX
              AND g.position_y BETWEEN @minY AND @maxY
            LIMIT @limit;
            """,
            new { map, minX = x - range, maxX = x + range, minY = y - range, maxY = y + range, limit },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Шаблон гейм-объекта по entry (для CMSG_GAMEOBJECT_QUERY).</summary>
    public async Task<GameObjectTemplateData?> GetGameObjectTemplateAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<GameObjectTemplateData>(new CommandDefinition("""
            SELECT entry AS Entry, type AS Type, displayId AS DisplayId, name AS Name,
                   IconName, castBarCaption AS CastBarCaption, unk1 AS Unk1, size AS Size
            FROM gameobject_template WHERE entry = @entry;
            """, new { entry }, cancellationToken: ct));
    }
}
