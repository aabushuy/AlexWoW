using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Навыки персонажа (M11.1, DI-сервис M7 S6 — бывший статик Skills): загрузка при входе, выдача (изучение
/// профессии), прокачка значения и потолка. Пишет в поля <c>PLAYER_SKILL_INFO</c> (через
/// <see cref="WorldSession.SkillBook"/>) и персист в <c>character_skill</c>. Языковые навыки сюда
/// не входят — они выдаются по расе в спавне.
/// </summary>
internal sealed class SkillsService
{
    /// <summary>Загружает персист-навыки персонажа в книгу сессии (число языковых слотов — по расе).</summary>
    internal async Task LoadAsync(WorldSession session, uint guid, byte race, CancellationToken ct)
    {
        var languageSlots = LanguageSkills.ForRace(race).Count;
        var persisted = await session.CharState.GetSkillsAsync(guid, ct);
        session.SkillBook.Init(languageSlots, persisted.Select(s => (s.SkillId, s.Value, s.Max)));
    }

    /// <summary>Текущее значение/потолок навыка или null, если не изучен.</summary>
    internal (ushort Value, ushort Max)? Get(WorldSession session, ushort skillId)
    {
        var s = session.SkillBook.Get(skillId);
        return s is null ? null : (s.Value, s.Max);
    }

    /// <summary>Выдаёт/обновляет навык (изучение профессии или смена потолка): персист + апдейт клиенту.</summary>
    internal async Task GrantAsync(WorldSession session, ushort skillId, ushort value, ushort max,
        CancellationToken ct)
    {
        session.SkillBook.AddOrSet(skillId, value, max);
        await session.CharState.UpsertSkillAsync(session.InWorldGuid, skillId, value, max, ct);
        await SendSkillUpdateAsync(session, skillId, value, max, ct);
    }

    /// <summary>Поднимает значение навыка на <paramref name="delta"/> (но не выше потолка). Возвращает
    /// новое значение, либо null, если навыка нет или он уже в потолке.</summary>
    internal async Task<ushort?> AddValueAsync(WorldSession session, ushort skillId, ushort delta,
        CancellationToken ct)
    {
        var sk = session.SkillBook.Get(skillId);
        if (sk is null)
            return null;
        var newValue = (ushort)Math.Min(sk.Max, sk.Value + delta);
        if (newValue == sk.Value)
            return null;
        sk.Value = newValue;
        await session.CharState.UpsertSkillAsync(session.InWorldGuid, skillId, sk.Value, sk.Max, ct);
        await SendSkillUpdateAsync(session, skillId, sk.Value, sk.Max, ct);
        return newValue;
    }

    /// <summary>Поднимает потолок навыка (изучение следующего тира у тренера). M11.5.</summary>
    internal async Task SetMaxAsync(WorldSession session, ushort skillId, ushort max, CancellationToken ct)
    {
        var sk = session.SkillBook.Get(skillId);
        if (sk is null || max <= sk.Max)
            return;
        sk.Max = max;
        await session.CharState.UpsertSkillAsync(session.InWorldGuid, skillId, sk.Value, sk.Max, ct);
        await SendSkillUpdateAsync(session, skillId, sk.Value, sk.Max, ct);
    }

    /// <summary>VALUES-апдейт одного слота PLAYER_SKILL_INFO (skillId | value | max), только себе.</summary>
    private static async Task SendSkillUpdateAsync(WorldSession session, ushort skillId, ushort value, ushort max,
        CancellationToken ct)
    {
        var slot = session.SkillBook.SlotOf(skillId);
        if (slot < 0)
            return;
        var baseIdx = UpdateField.PlayerSkillInfo11 + slot * 3;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(baseIdx, skillId);                          // skillId | step(0)
                m.SetUInt32(baseIdx + 1, (uint)(value | (max << 16)));  // value | max
                m.SetUInt32(baseIdx + 2, 0);                            // бонусы
            }), ct);
    }
}
