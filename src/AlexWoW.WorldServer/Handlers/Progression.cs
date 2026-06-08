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

        var oldMaxHp = session.MaxHealth;
        c.Level = level;
        session.MaxHealth = DisplayData.MaxHealthForLevel(level);
        session.Health = session.MaxHealth;
        session.Xp = 0;
        var hpDiff = session.MaxHealth > oldMaxHp ? session.MaxHealth - oldMaxHp : 0;

        var w = new ByteWriter(56).UInt32(level).UInt32(hpDiff);
        for (var i = 0; i < 12; i++) w.UInt32(0); // 7 powers + 5 статов
        await session.SendAsync(WorldOpcode.SmsgLevelupInfo, w.ToArray(), ct);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitLevel, level);
                m.SetUInt32(UpdateField.UnitMaxHealth, session.MaxHealth);
                m.SetUInt32(UpdateField.UnitHealth, session.Health);
                m.SetUInt32(UpdateField.PlayerXp, 0);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, session.World.Levels.XpToNext(level));
            }), ct);
        await session.Characters.SetLevelXpAsync(session.InWorldGuid, level, 0, ct);
        session.Logger.LogInformation("DEV SETLEVEL '{User}' → {Level}", session.Account, level);
    }

    /// <summary>Повышение на один уровень: пересчёт HP (флэт по уровню), фулл-хил, SMSG_LEVELUP_INFO + поля.</summary>
    private static async Task ApplyLevelUpAsync(WorldSession session, CancellationToken ct)
    {
        var c = session.Character!;
        var oldMaxHp = session.MaxHealth;
        c.Level = (byte)(c.Level + 1);
        session.MaxHealth = DisplayData.MaxHealthForLevel(c.Level);
        session.Health = session.MaxHealth;                 // фулл-хил на новом уровне
        var hpDiff = session.MaxHealth - oldMaxHp;

        // SMSG_LEVELUP_INFO (wrath): new_level + health + 7 powers + 5 статов (диффы). Мана/статы — M9.2 (0).
        var w = new ByteWriter(56).UInt32(c.Level).UInt32(hpDiff);
        for (var i = 0; i < 7; i++) w.UInt32(0); // mana,rage,focus,energy,happiness,rune,runic_power
        for (var i = 0; i < 5; i++) w.UInt32(0); // strength..spirit
        await session.SendAsync(WorldOpcode.SmsgLevelupInfo, w.ToArray(), ct);

        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.UnitLevel, c.Level);
                m.SetUInt32(UpdateField.UnitMaxHealth, session.MaxHealth);
                m.SetUInt32(UpdateField.UnitHealth, session.Health);
            }), ct);

        session.Logger.LogInformation("LEVELUP '{User}' → уровень {Level} (HP {Hp})",
            session.Account, c.Level, session.MaxHealth);
    }
}
