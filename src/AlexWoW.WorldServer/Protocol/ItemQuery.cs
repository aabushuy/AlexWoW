using AlexWoW.Common.Network;
using AlexWoW.Database.Models;

namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Сборка SMSG_ITEM_QUERY_SINGLE_RESPONSE (3.3.5a). Полный layout сверен с gtker.com
/// (stats[N], damages[2], spells[5], sockets[3]) — для корректных тултипов предметов.
/// </summary>
public static class ItemQuery
{
    public static byte[] BuildResponse(ItemTemplateData t)
    {
        var w = new ByteWriter(256);
        w.UInt32(t.Entry);
        w.UInt32(t.Class).UInt32(t.SubClass);
        w.Int32(t.SoundOverrideSubclass);
        w.CString(t.Name).CString(string.Empty).CString(string.Empty).CString(string.Empty);
        w.UInt32(t.DisplayId);
        w.UInt32(t.Quality);
        w.UInt32(t.Flags);
        w.UInt32(t.Flags2);
        w.UInt32(t.BuyPrice);
        w.UInt32(t.SellPrice);
        w.UInt32(t.InventoryType);
        w.Int32(t.AllowableClass);
        w.Int32(t.AllowableRace);
        w.UInt32(t.ItemLevel);
        w.UInt32(t.RequiredLevel);
        w.UInt32(t.RequiredSkill);
        w.UInt32(t.RequiredSkillRank);
        w.UInt32(t.RequiredSpell);
        w.UInt32(t.RequiredHonorRank);
        w.UInt32(t.RequiredCityRank);
        w.UInt32(t.RequiredReputationFaction);
        w.UInt32(t.RequiredReputationRank);
        w.Int32(t.MaxCount);
        w.Int32(t.Stackable);
        w.UInt32(t.ContainerSlots);

        w.UInt32((uint)t.Stats.Length);
        foreach (var s in t.Stats)
            w.UInt32(s.Type).Int32(s.Value);

        w.Int32(t.ScalingStatDistribution);
        w.UInt32(t.ScalingStatValue);

        foreach (var d in t.Damages)
            w.Single(d.Min).Single(d.Max).UInt32(d.School);

        w.Int32(t.Armor)
         .Int32(t.HolyRes).Int32(t.FireRes).Int32(t.NatureRes)
         .Int32(t.FrostRes).Int32(t.ShadowRes).Int32(t.ArcaneRes);
        w.UInt32(t.Delay);
        w.UInt32(t.AmmoType);
        w.Single(t.RangedModRange);

        foreach (var sp in t.Spells)
            w.UInt32(sp.Id).UInt32(sp.Trigger).Int32(sp.Charges)
             .Int32(sp.Cooldown).UInt32(sp.Category).Int32(sp.CategoryCooldown);

        w.UInt32(t.Bonding);
        w.CString(t.Description);
        w.UInt32(t.PageText);
        w.UInt32(t.LanguageId);
        w.UInt32(t.PageMaterial);
        w.UInt32(t.StartQuest);
        w.UInt32(t.LockId);
        w.Int32(t.Material);
        w.UInt32(t.Sheath);
        w.UInt32(t.RandomProperty);
        w.UInt32(t.RandomSuffix);
        w.UInt32(t.Block);
        w.UInt32(t.ItemSet);
        w.UInt32(t.MaxDurability);
        w.UInt32(t.Area);
        w.Int32(t.Map);
        w.Int32(t.BagFamily);
        w.Int32(t.TotemCategory);

        foreach (var sock in t.Sockets)
            w.UInt32(sock.Color).UInt32(sock.Content);

        w.UInt32(t.SocketBonus);
        w.UInt32(t.GemProperties);
        w.Int32(t.RequiredDisenchantSkill);
        w.Single(t.ArmorDamageModifier);
        w.UInt32(t.Duration);
        w.Int32(t.ItemLimitCategory);
        w.UInt32(t.HolidayId);

        return w.ToArray();
    }
}
