using System.Globalization;
using AlexWoW.Database.Models;

namespace AlexWoW.Database.Repositories.World;

/// <summary>
/// Маппинг строки <c>item_template</c> (Dapper dynamic row) в <see cref="ItemTemplateData"/>.
/// Общий для репозиториев, читающих полный шаблон (<see cref="ItemTemplateRepository"/> для
/// item-query и <see cref="ItemSearchRepository"/> для поиска в админке) — чтобы не дублировать.
/// </summary>
internal static class ItemTemplateMapper
{
    public static ItemTemplateData Map(IDictionary<string, object> r)
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
        {
            spells[i] = new ItemSpell(U(r, $"spellid_{i + 1}"), U(r, $"spelltrigger_{i + 1}"),
                I(r, $"spellcharges_{i + 1}"), I(r, $"spellcooldown_{i + 1}"),
                U(r, $"spellcategory_{i + 1}"), I(r, $"spellcategorycooldown_{i + 1}"));
        }

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
}
