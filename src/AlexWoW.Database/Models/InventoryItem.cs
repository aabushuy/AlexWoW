namespace AlexWoW.Database.Models;

/// <summary>
/// Экземпляр предмета во владении персонажа (строка таблицы character_items).
/// Слоты: экипировка 0..18, сумки 19..22, рюкзак 23..38 (INVENTORY_SLOT_* 3.3.5a).
/// bag = 255 (INVENTORY_SLOT_BAG_0) — основной контейнер (экипировка + рюкзак).
/// </summary>
public sealed class InventoryItem
{
    public uint ItemGuid { get; init; }   // low-counter; полный GUID = HIGHGUID_ITEM | ItemGuid
    public uint OwnerGuid { get; init; }
    public uint ItemEntry { get; init; }
    public byte Bag { get; set; } = 255;            // меняется при перемещении (M6.9)
    public byte Slot { get; set; }
    public uint StackCount { get; set; } = 1;
}

/// <summary>Товар вендора (npc_vendor ⨝ item_template) — для SMSG_LIST_INVENTORY и покупки. M6.2.</summary>
public sealed class VendorItem
{
    public uint Slot { get; init; }        // npc_vendor.slot (порядок в окне)
    public uint ItemId { get; init; }
    public uint MaxCount { get; init; }    // 0 = бесконечный запас
    public uint BuyPrice { get; init; }    // цена покупки (медь) из item_template
    public uint DisplayId { get; init; }
    public uint MaxDurability { get; init; }
    public uint BuyCount { get; init; }     // размер пачки за одну покупку
    public string Name { get; init; } = string.Empty;
    public int Stackable { get; init; } = 1;
}

/// <summary>Строка лут-таблицы существа (creature_loot_template ⨝ item_template) — кандидат на дроп. M6.6.</summary>
public sealed class CreatureLootEntry
{
    public uint ItemId { get; init; }
    public float Chance { get; init; }     // ChanceOrQuestChance: шанс дропа (%), >0 — обычный предмет
    public int MinCount { get; init; }     // mincountOrRef: >0 — мин. количество (отрицательное — ссылка, пропускаем)
    public uint MaxCount { get; init; }
    public uint DisplayId { get; init; }
}

/// <summary>Лут-определение существа: диапазон денег + кандидаты-предметы (до ролла). M6.6.</summary>
public sealed class CreatureLootData
{
    public uint MinGold { get; init; }
    public uint MaxGold { get; init; }
    public IReadOnlyList<CreatureLootEntry> Drops { get; init; } = [];
}

/// <summary>Квест в списке квестгивера (creature_questrelation ⨝ quest_template) — для SMSG_QUESTGIVER_QUEST_LIST. M6.5.</summary>
public sealed class GiverQuest
{
    public uint QuestId { get; init; }
    public int QuestLevel { get; init; }
    public uint QuestFlags { get; init; }
    public string Title { get; init; } = string.Empty;
}

/// <summary>Шаблон квеста (quest_template) — нужные поля для деталей/награды/целей. M6.5.</summary>
public sealed class QuestTemplateData
{
    public uint Entry { get; init; }
    public int QuestLevel { get; init; }
    public int MinLevel { get; init; }
    public int ZoneOrSort { get; init; }
    public uint Type { get; init; }
    public uint Method { get; init; }
    public uint SrcItemId { get; init; }
    public uint NextQuestId { get; init; }
    public uint QuestFlags { get; init; }
    public uint SuggestedPlayers { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Objectives { get; init; } = string.Empty;
    public string OfferRewardText { get; init; } = string.Empty;
    public string RequestItemsText { get; init; } = string.Empty;
    public string EndText { get; init; } = string.Empty;
    /// <summary>Деньги: &gt;0 — награда, &lt;0 — требуется (для нашего инкремента берём как награду, если &gt;0).</summary>
    public int RewOrReqMoney { get; init; }
    public uint RewSpell { get; init; }
    public uint RewSpellCast { get; init; }
    public uint[] RewItemId { get; init; } = new uint[4];
    public uint[] RewItemCount { get; init; } = new uint[4];
    public uint[] RewChoiceItemId { get; init; } = new uint[6];
    public uint[] RewChoiceItemCount { get; init; } = new uint[6];
    /// <summary>Цели-убийства/гео (отрицательное = GO): entry + count, до 4. M6.5 инкр.3.</summary>
    public int[] ReqCreatureOrGoId { get; init; } = new int[4];
    public uint[] ReqCreatureOrGoCount { get; init; } = new uint[4];
    public uint[] ReqItemId { get; init; } = new uint[6];
    public uint[] ReqItemCount { get; init; } = new uint[6];
    public string[] ObjectiveText { get; init; } = new string[4];
}

/// <summary>Строка faction_template (из FactionTemplate.dbc) — реакции фракций для авто-агро. M6.7.</summary>
public sealed class FactionTemplateRow
{
    public uint Id { get; init; }
    public uint Faction { get; init; }
    public uint OurMask { get; init; }
    public uint FriendMask { get; init; }
    public uint HostileMask { get; init; }
    public uint Enemy1 { get; init; }
    public uint Enemy2 { get; init; }
    public uint Enemy3 { get; init; }
    public uint Enemy4 { get; init; }
    public uint Friend1 { get; init; }
    public uint Friend2 { get; init; }
    public uint Friend3 { get; init; }
    public uint Friend4 { get; init; }
}

/// <summary>Стартовый предмет (playercreateinfo_item ⨝ item_template) — для раскладки набора.</summary>
public sealed class StartingItem
{
    public uint ItemId { get; init; }
    public byte Amount { get; init; } = 1;
    public byte InventoryType { get; init; }  // item_template.InventoryType → слот экипировки
    public int Stackable { get; init; } = 1;
}
