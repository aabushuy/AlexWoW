using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Прогрессия (M9.1): начисление опыта и повышение уровня. XP за килл/квест → <see cref="GiveXpAsync"/> →
/// накопление, level-up при достижении порога (player_xp_for_level), <c>SMSG_LEVELUP_INFO</c> (сплеш «ding»)
/// + апдейт полей (уровень/HP) + персист. Статы пока флэт по уровню (точные по классу — M9.2).
/// </summary>
public static class Progression
{
    /// <summary>
    /// Начисляет <paramref name="amount"/> опыта игроку: копит, повышает уровень (с переносом остатка),
    /// шлёт level-up визуал/поля, обновляет PLAYER_XP, персистит уровень+опыт. На капе опыт не копится.
    /// </summary>
    internal static async Task GiveXpAsync(WorldSession session, uint amount, CancellationToken ct)
    {
        var c = session.Character;
        if (c is null || session.InWorldGuid == 0 || amount == 0 || c.Level >= LevelStore.MaxLevel)
            return;
        await session.World.Levels.EnsureLoadedAsync(ct);
        if (!session.World.Levels.Available)
            return;

        session.Xp += amount;

        uint next;
        while (c.Level < LevelStore.MaxLevel
            && (next = session.World.Levels.XpToNext(c.Level)) > 0
            && session.Xp >= next)
        {
            session.Xp -= next;
            await ApplyLevelUpAsync(session, ct);
        }
        if (c.Level >= LevelStore.MaxLevel)
            session.Xp = 0; // кап — опыт не копим

        var nextXp = session.World.Levels.XpToNext(c.Level);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, session.Xp);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, nextXp);
            }), ct);

        await session.Characters.SetLevelXpAsync(session.InWorldGuid, c.Level, session.Xp, ct);
    }

    /// <summary>
    /// Выставляет уровень напрямую (дев-команда M9.4): пересчёт HP, фулл-хил, сброс опыта, апдейт полей +
    /// SMSG_LEVELUP_INFO. Клампится 1..80.
    /// </summary>
    internal static async Task SetLevelAsync(WorldSession session, byte level, CancellationToken ct)
    {
        var c = session.Character;
        if (c is null || session.InWorldGuid == 0)
            return;
        level = Math.Clamp(level, (byte)1, LevelStore.MaxLevel);
        await session.World.Levels.EnsureLoadedAsync(ct);
        c.Level = level;
        session.Xp = 0;
        await RecalcStatsAsync(session, ding: true, ct); // уровень/HP/мана/статы + ding

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, 0);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, session.World.Levels.XpToNext(level));
            }), ct);
        await session.Characters.SetLevelXpAsync(session.InWorldGuid, level, 0, ct);
        session.Logger.LogInformation("DEV SETLEVEL '{User}' → {Level}", session.Account, level);
    }

    /// <summary>
    /// Боевые поля игрока (урон/скорость атаки) из экипированного оружия главной руки (M9.2): без них
    /// слот-тултип оружия показывает NaN/INF. Урон = урон оружия (attack power пока 0 → percent клиента
    /// = 1.0), скорость = задержка оружия; без оружия — 1-2 / 2.0с. Зовётся после спавна и при смене
    /// экипировки (M6.9 — позже подключить). VALUES-апдейт себе.
    /// </summary>
    internal static async Task RefreshMeleeAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        float min = 1f, max = 2f;
        uint attackTime = 2000;
        var mh = session.Inventory.FirstOrDefault(i =>
            i.Bag == InventorySlots.MainBag && i.Slot == InventorySlots.MainHandSlot);
        if (mh is not null)
        {
            try
            {
                var t = await session.WorldDb.GetItemTemplateAsync(mh.ItemEntry, ct);
                if (t is not null && (t.Damages[0].Min > 0 || t.Damages[0].Max > 0))
                {
                    min = t.Damages[0].Min;
                    max = t.Damages[0].Max;
                    if (t.Delay > 0)
                        attackTime = t.Delay;
                }
            }
            catch { /* БД мира недоступна — безоружный фолбэк */ }
        }
        session.MainHandSpeedMs = attackTime; // M6.12: для формулы ярости
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetFloat(UpdateField.UnitMinDamage, min);
                m.SetFloat(UpdateField.UnitMaxDamage, max);
                m.SetUInt32(UpdateField.UnitBaseAttackTime, attackTime);
                m.SetUInt32(UpdateField.UnitBaseAttackTime + 1, attackTime);
            }), ct);
    }

    /// <summary>Повышение на один уровень: +1 к уровню, пересчёт статов/HP/маны, ding.</summary>
    private static async Task ApplyLevelUpAsync(WorldSession session, CancellationToken ct)
    {
        var c = session.Character!;
        c.Level = (byte)(c.Level + 1);
        await RecalcStatsAsync(session, ding: true, ct);
        session.Logger.LogInformation("LEVELUP '{User}' → уровень {Level} (HP {Hp})",
            session.Account, c.Level, session.MaxHealth);
    }

    /// <summary>
    /// Пересчёт статов/HP/маны по текущему уровню (M9.2: player_levelstats; фолбэк — флэт), фулл-хил,
    /// апдейт полей (уровень/HP/мана/статы). <paramref name="ding"/> — ещё и SMSG_LEVELUP_INFO («ding»).
    /// </summary>
    private static async Task RecalcStatsAsync(WorldSession session, bool ding, CancellationToken ct)
    {
        var c = session.Character!;
        await session.World.Stats.EnsureLoadedAsync(ct);
        var stats = session.World.Stats.Compute(c.Race, c.Class, c.Level);
        var oldMaxHp = session.MaxHealth;
        session.MaxHealth = stats?.MaxHealth ?? DisplayData.MaxHealthForLevel(c.Level);
        session.Health = session.MaxHealth;                 // фулл-хил
        session.MaxMana = stats?.MaxMana ?? DisplayData.MaxManaForClass(c.Class, c.Level);
        session.Mana = session.MaxMana;
        var hpDiff = session.MaxHealth > oldMaxHp ? session.MaxHealth - oldMaxHp : 0;
        var powerType = DisplayData.PowerTypeForClass(c.Class);

        if (ding)
        {
            // SMSG_LEVELUP_INFO (wrath): new_level + health + 7 powers + 5 статов (диффы). Мана/статы — 0 (упрощённо).
            var w = new ByteWriter(56).UInt32(c.Level).UInt32(hpDiff);
            for (var i = 0; i < 12; i++) w.UInt32(0);
            await session.SendAsync(WorldOpcode.SmsgLevelupInfo, w.ToArray(), ct);
        }

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitLevel, c.Level);
                m.SetUInt32(UpdateField.UnitMaxHealth, session.MaxHealth);
                m.SetUInt32(UpdateField.UnitHealth, session.Health);
                if (powerType == 0) // мана-классам обновляем пул
                {
                    m.SetUInt32(UpdateField.UnitMaxPower1, session.MaxMana);
                    m.SetUInt32(UpdateField.UnitPower1, session.Mana);
                }
                if (stats is { } s)
                {
                    m.SetUInt32(UpdateField.UnitStat0, s.Str);
                    m.SetUInt32(UpdateField.UnitStat1, s.Agi);
                    m.SetUInt32(UpdateField.UnitStat2, s.Sta);
                    m.SetUInt32(UpdateField.UnitStat3, s.Int);
                    m.SetUInt32(UpdateField.UnitStat4, s.Spi);
                    m.SetUInt32(UpdateField.UnitBaseHealth, s.MaxHealth);
                    m.SetUInt32(UpdateField.UnitBaseMana, s.MaxMana);
                }
            }), ct);
    }
}
