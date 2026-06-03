namespace AlexWoW.Database.Models;

/// <summary>Характеристика предмета (stat_type/value) — пара для ItemStat в item-query.</summary>
public readonly record struct ItemStat(uint Type, int Value);

/// <summary>Урон оружия (ItemDamageType): min/max + школа.</summary>
public readonly record struct ItemDamage(float Min, float Max, uint School);

/// <summary>Спелл на предмете (ItemSpells): id + триггер + заряды + кулдаун + категория.</summary>
public readonly record struct ItemSpell(uint Id, uint Trigger, int Charges, int Cooldown, uint Category, int CategoryCooldown);

/// <summary>Сокет (ItemSocket): цвет + контент.</summary>
public readonly record struct ItemSocket(uint Color, uint Content);

/// <summary>
/// Шаблон предмета из БД мира (item_template, 3.3.5a) — полный набор для
/// SMSG_ITEM_QUERY_SINGLE_RESPONSE и для раскладки/экипировки. Заполняется в WorldDatabase.
/// </summary>
public sealed class ItemTemplateData
{
    public uint Entry { get; init; }
    public uint Class { get; init; }
    public uint SubClass { get; init; }
    public int SoundOverrideSubclass { get; init; } = -1;
    public string Name { get; init; } = string.Empty;
    public uint DisplayId { get; init; }
    public uint Quality { get; init; }
    public uint Flags { get; init; }
    public uint Flags2 { get; init; }
    public uint BuyPrice { get; init; }
    public uint SellPrice { get; init; }
    public uint InventoryType { get; init; }
    public int AllowableClass { get; init; } = -1;
    public int AllowableRace { get; init; } = -1;
    public uint ItemLevel { get; init; }
    public uint RequiredLevel { get; init; }
    public uint RequiredSkill { get; init; }
    public uint RequiredSkillRank { get; init; }
    public uint RequiredSpell { get; init; }
    public uint RequiredHonorRank { get; init; }
    public uint RequiredCityRank { get; init; }
    public uint RequiredReputationFaction { get; init; }
    public uint RequiredReputationRank { get; init; }
    public int MaxCount { get; init; }
    public int Stackable { get; init; } = 1;
    public uint ContainerSlots { get; init; }
    public ItemStat[] Stats { get; init; } = [];
    public int ScalingStatDistribution { get; init; }
    public uint ScalingStatValue { get; init; }
    public ItemDamage[] Damages { get; init; } = new ItemDamage[2];
    public int Armor { get; init; }
    public int HolyRes { get; init; }
    public int FireRes { get; init; }
    public int NatureRes { get; init; }
    public int FrostRes { get; init; }
    public int ShadowRes { get; init; }
    public int ArcaneRes { get; init; }
    public uint Delay { get; init; }
    public uint AmmoType { get; init; }
    public float RangedModRange { get; init; }
    public ItemSpell[] Spells { get; init; } = new ItemSpell[5];
    public uint Bonding { get; init; }
    public string Description { get; init; } = string.Empty;
    public uint PageText { get; init; }
    public uint LanguageId { get; init; }
    public uint PageMaterial { get; init; }
    public uint StartQuest { get; init; }
    public uint LockId { get; init; }
    public int Material { get; init; }
    public uint Sheath { get; init; }
    public uint RandomProperty { get; init; }
    public uint RandomSuffix { get; init; }
    public uint Block { get; init; }
    public uint ItemSet { get; init; }
    public uint MaxDurability { get; init; }
    public uint Area { get; init; }
    public int Map { get; init; }
    public int BagFamily { get; init; }
    public int TotemCategory { get; init; }
    public ItemSocket[] Sockets { get; init; } = new ItemSocket[3];
    public uint SocketBonus { get; init; }
    public uint GemProperties { get; init; }
    public int RequiredDisenchantSkill { get; init; }
    public float ArmorDamageModifier { get; init; }
    public uint Duration { get; init; }
    public int ItemLimitCategory { get; init; }
    public uint HolidayId { get; init; }
}
