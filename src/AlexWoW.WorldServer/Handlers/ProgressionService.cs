using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Прогрессия (M9.1, DI-сервис M7 S6 — бывший статик Progression): начисление опыта и повышение уровня.
/// XP за килл/квест → <see cref="GiveXpAsync"/> → накопление, level-up при достижении порога
/// (player_xp_for_level), <c>SMSG_LEVELUP_INFO</c> (сплеш «ding») + апдейт полей (уровень/HP) + персист.
/// Статы пока флэт по уровню (точные по классу — M9.2).
/// </summary>
internal sealed class ProgressionService(
    TalentHandlers talents,
    ICharacterRepository characters,
    IWorldRepository worldDb,
    SkillsService skills)
{
    /// <summary>Навык «Защита» (SKILL_DEFENSE) — для базового уклонения/парирования/блока в чарпейне.</summary>
    private const ushort DefenseSkillId = 95;

    /// <summary>
    /// Начисляет <paramref name="amount"/> опыта игроку: копит, повышает уровень (с переносом остатка),
    /// шлёт level-up визуал/поля, обновляет PLAYER_XP, персистит уровень+опыт. На капе опыт не копится.
    /// </summary>
    internal async Task GiveXpAsync(WorldSession session, uint amount, CancellationToken ct)
    {
        var c = session.Character;
        if (c is null || session.InWorldGuid == 0 || amount == 0 || c.Level >= LevelStore.MaxLevel)
            return;
        await session.World.Levels.EnsureLoadedAsync(ct);
        if (!session.World.Levels.Available)
            return;

        session.Progression.Xp += amount;

        uint next;
        while (c.Level < LevelStore.MaxLevel
            && (next = session.World.Levels.XpToNext(c.Level)) > 0
            && session.Progression.Xp >= next)
        {
            session.Progression.Xp -= next;
            await ApplyLevelUpAsync(session, ct);
        }
        if (c.Level >= LevelStore.MaxLevel)
            session.Progression.Xp = 0; // кап — опыт не копим

        var nextXp = session.World.Levels.XpToNext(c.Level);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, session.Progression.Xp);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, nextXp);
            }), ct);

        await characters.SetLevelXpAsync(session.InWorldGuid, c.Level, session.Progression.Xp, ct);
    }

    /// <summary>
    /// Выставляет уровень напрямую (дев-команда M9.4): пересчёт HP, фулл-хил, сброс опыта, апдейт полей +
    /// SMSG_LEVELUP_INFO. Клампится 1..80.
    /// </summary>
    internal async Task SetLevelAsync(WorldSession session, byte level, CancellationToken ct)
    {
        var c = session.Character;
        if (c is null || session.InWorldGuid == 0)
            return;
        level = Math.Clamp(level, (byte)1, LevelStore.MaxLevel);
        await session.World.Levels.EnsureLoadedAsync(ct);
        c.Level = level;
        session.Progression.Xp = 0;
        await RecalcStatsAsync(session, ding: true, ct); // уровень/HP/мана/статы + ding

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, 0);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, session.World.Levels.XpToNext(level));
            }), ct);
        await characters.SetLevelXpAsync(session.InWorldGuid, level, 0, ct);
        session.Logger.LogInformation("DEV SETLEVEL '{User}' → {Level}", session.Account, level);
    }

    /// <summary>
    /// Боевые поля игрока (урон/скорость атаки) из экипированного оружия главной руки (M9.2): без них
    /// слот-тултип оружия показывает NaN/INF. Урон = урон оружия (attack power пока 0 → percent клиента
    /// = 1.0), скорость = задержка оружия; без оружия — 1-2 / 2.0с. Зовётся после спавна и при смене
    /// экипировки (M6.9 — позже подключить). VALUES-апдейт себе.
    /// </summary>
    internal async Task RefreshMeleeAsync(WorldSession session, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Character is not { } c)
            return;
        float min = 1f, max = 2f;
        uint attackTime = 2000;
        var mh = session.Inv.Inventory.FirstOrDefault(i =>
            i.Bag == InventorySlots.MainBag && i.Slot == InventorySlots.MainHandSlot);
        if (mh is not null)
        {
            try
            {
                var t = await worldDb.GetItemTemplateAsync(mh.ItemEntry, ct);
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
        session.Combat.MainHandSpeedMs = attackTime; // M6.12: для формулы ярости
        session.Combat.WeaponMinDamage = min;        // M10.4a: для мили-абилок (WEAPON_DAMAGE)
        session.Combat.WeaponMaxDamage = max;

        // M7 #16: attack power (по статам класса). Без этих полей клиентский UnitDamage даёт percent=0 →
        // слот-тултип оружия показывает 1.#INF. Формула — CMaNGOS Player::UpdateAttackPowerAndDamage.
        await session.World.Stats.EnsureLoadedAsync(ct);
        var stats = session.World.Stats.Compute(c.Race, c.Class, c.Level);
        uint str = stats?.Str ?? 0, agi = stats?.Agi ?? 0;
        var baseMeleeAp = MeleeAttackPower(c.Class, c.Level, str, agi);
        var baseRangedAp = RangedAttackPower(c.Class, c.Level, agi);
        // Кэшируем базу — её использует PeriodicsService.SendAttackPowerAsync при apply/remove аур-баффов AP,
        // и PlayerMeleeService — в формуле AP-вклада в автоатаку (база + Combat.AttackPowerBonus от аур).
        session.Combat.BaseMeleeAttackPower = baseMeleeAp;
        session.Combat.BaseRangedAttackPower = baseRangedAp;
        // Итоговое поле = база + аур-бонусы (Боевой клич и т.п.).
        var apBonus = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.AttackPowerBonus);
        var rapBonus = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.RangedAttackPowerBonus);
        session.Combat.AttackPowerBonus = apBonus;
        session.Combat.RangedAttackPowerBonus = rapBonus;
        var meleeAp = (uint)Math.Max(0, (int)baseMeleeAp + apBonus);
        var rangedAp = (uint)Math.Max(0, (int)baseRangedAp + rapBonus);

        // Защитные статы (срез M-защита): броня предметов + флаги оружия/щита.
        uint itemArmor = 0;
        var hasShield = false;
        foreach (var it in session.Inv.Inventory
            .Where(i => i.Bag == InventorySlots.MainBag && InventorySlots.IsEquipmentSlot(i.Slot)))
        {
            try
            {
                var tpl = await worldDb.GetItemTemplateAsync(it.ItemEntry, ct);
                if (tpl is null)
                    continue;
                if (tpl.Armor > 0)
                    itemArmor += (uint)tpl.Armor;
                if (it.Slot == InventorySlots.OffHandSlot && tpl.InventoryType == 14) // INVTYPE_SHIELD
                    hasShield = true;
            }
            catch { /* БД мира недоступна — без брони предметов */ }
        }
        var hasMeleeWeapon = mh is not null;

        session.Combat.HasShield = hasShield; // кэш для пересчёта блока при аурах («Блок щитом»)
        var blockAura = session.Progression.Periodics.Where(p => p.TargetGuid == 0).Sum(p => p.BlockBonus);
        var armor = CombatStats.Armor(agi, itemArmor);
        var dodge = session.World.Ratings.DodgePercent(c.Class, c.Level, agi);
        var crit = session.World.Ratings.MeleeCritPercent(c.Class, c.Level, agi);
        var parry = CombatStats.ParryPercent(c.Class, hasMeleeWeapon);
        var block = CombatStats.BlockPercent(c.Class, hasShield, blockAura);

        // Кэш для серверной обработки входящего удара (уклон/парри/блок/броня) + исходящего крита (CRIT.2).
        session.Combat.DodgePct = dodge;
        session.Combat.ParryPct = parry;
        session.Combat.BlockPct = block;
        session.Combat.ArmorValue = armor;
        session.Combat.MeleeCritPct = crit;

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetFloat(UpdateField.UnitMinDamage, min);
                m.SetFloat(UpdateField.UnitMaxDamage, max);
                m.SetUInt32(UpdateField.UnitBaseAttackTime, attackTime);
                m.SetUInt32(UpdateField.UnitBaseAttackTime + 1, attackTime);
                m.SetUInt32(UpdateField.UnitAttackPower, meleeAp);
                m.SetUInt32(UpdateField.UnitAttackPowerMods, 0);              // pos|neg = 0
                m.SetFloat(UpdateField.UnitAttackPowerMultiplier, 0f);        // TOTAL_PCT-1 = 0 → percent 1.0
                m.SetUInt32(UpdateField.UnitRangedAttackPower, rangedAp);
                m.SetUInt32(UpdateField.UnitRangedAttackPowerMods, 0);
                m.SetFloat(UpdateField.UnitRangedAttackPowerMultiplier, 0f);
                // Защитные: броня + проценты уклонения/парирования/блока/крита.
                m.SetUInt32(UpdateField.UnitResistances, armor);
                m.SetFloat(UpdateField.PlayerDodgePercentage, dodge);
                m.SetFloat(UpdateField.PlayerParryPercentage, parry);
                m.SetFloat(UpdateField.PlayerBlockPercentage, block);
                m.SetFloat(UpdateField.PlayerCritPercentage, crit);
                m.SetFloat(UpdateField.PlayerOffhandCritPercentage, crit);
                m.SetFloat(UpdateField.PlayerRangedCritPercentage, crit);
            }), ct);

        // Навык «Защита» (5×level) — для чарпейна и UI-корректировок; обновляем только при изменении.
        var def = CombatStats.DefenseSkill(c.Level);
        if (skills.Get(session, DefenseSkillId) is not { } cur || cur.Value != def)
            await skills.GrantAsync(session, DefenseSkillId, def, def, ct);
    }

    /// <summary>Сила атаки (мили) по классу/уровню/статам — формула CMaNGOS. M7 #16. (Чистая математика — static.)</summary>
    private static uint MeleeAttackPower(byte cls, byte level, uint str, uint agi)
    {
        float ap = cls switch
        {
            1 or 2 or 6 => level * 3f + str * 2f - 20f,                 // воин/паладин/DK
            4 or 3 or 7 => level * 2f + str + agi - 20f,                // разбойник/охотник/шаман
            11 => level * 3f + str * 2f - 20f,                          // друид (форма-бонусы — позже)
            _ => str - 10f,                                            // маг/жрец/чернокнижник
        };
        return (uint)Math.Max(0f, ap);
    }

    /// <summary>Сила атаки (дальний бой): значима для охотника; прочим — 0. M7 #16.</summary>
    private static uint RangedAttackPower(byte cls, byte level, uint agi)
        => cls == 3 ? (uint)Math.Max(0f, level * 2f + agi - 10f) : 0u;

    /// <summary>Повышение на один уровень: +1 к уровню, пересчёт статов/HP/маны, ding.</summary>
    private async Task ApplyLevelUpAsync(WorldSession session, CancellationToken ct)
    {
        var c = session.Character!;
        c.Level = (byte)(c.Level + 1);
        await RecalcStatsAsync(session, ding: true, ct);
        session.Logger.LogInformation("LEVELUP '{User}' → уровень {Level} (HP {Hp})",
            session.Account, c.Level, session.Combat.MaxHealth);
    }

    /// <summary>
    /// Пересчёт статов/HP/маны по текущему уровню (M9.2: player_levelstats; фолбэк — флэт), фулл-хил,
    /// апдейт полей (уровень/HP/мана/статы). <paramref name="ding"/> — ещё и SMSG_LEVELUP_INFO («ding»).
    /// </summary>
    private async Task RecalcStatsAsync(WorldSession session, bool ding, CancellationToken ct)
    {
        var c = session.Character!;
        await session.World.Stats.EnsureLoadedAsync(ct);
        var stats = session.World.Stats.Compute(c.Race, c.Class, c.Level);
        var oldMaxHp = session.Combat.MaxHealth;
        session.Combat.MaxHealth = stats?.MaxHealth ?? DisplayData.MaxHealthForLevel(c.Level);
        session.Combat.Health = session.Combat.MaxHealth;                 // фулл-хил
        session.Cast.MaxMana = stats?.MaxMana ?? DisplayData.MaxManaForClass(c.Class, c.Level);
        session.Cast.Mana = session.Cast.MaxMana;
        var hpDiff = session.Combat.MaxHealth > oldMaxHp ? session.Combat.MaxHealth - oldMaxHp : 0;
        var powerType = DisplayData.PowerTypeForClass(c.Class);

        // M9.6: очки талантов растут с уровнем (MaxPoints − потрачено).
        talents.RecomputePoints(session, c.Class, c.Level);

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
                m.SetUInt32(UpdateField.UnitMaxHealth, session.Combat.MaxHealth);
                m.SetUInt32(UpdateField.UnitHealth, session.Combat.Health);
                if (powerType == 0) // мана-классам обновляем пул
                {
                    m.SetUInt32(UpdateField.UnitMaxPower1, session.Cast.MaxMana);
                    m.SetUInt32(UpdateField.UnitPower1, session.Cast.Mana);
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
                m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.Progression.TalentPoints); // M9.6
            }), ct);

        if (ding) // M9.6: обновить панель талантов (новые свободные очки)
            await talents.SendTalentsInfoAsync(session, ct);

        // Защитные статы зависят от уровня/ловкости — пересчитать и переслать (броня/уклон/крит + навык защиты).
        await RefreshMeleeAsync(session, ct);
    }
}
