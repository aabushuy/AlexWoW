using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Навыки персонажа (M11.1, DI-сервис M7 S6 — бывший статик Skills): загрузка при входе, выдача (изучение
/// профессии), прокачка значения и потолка. Пишет в поля <c>PLAYER_SKILL_INFO</c> (через
/// <see cref="Net.SessionState.SessionProgressionState.SkillBook"/>) и персист в <c>character_skill</c>. Языковые навыки сюда
/// не входят — они выдаются по расе в спавне.
/// </summary>
internal sealed class SkillsService(ICharacterStateRepository charState)
{
    /// <summary>Загружает персист-навыки персонажа в книгу сессии (число языковых слотов — по расе).</summary>
    internal async Task LoadAsync(WorldSession session, uint guid, byte race, CancellationToken ct)
    {
        var languageSlots = LanguageSkills.ForRace(race).Count;
        var persisted = await charState.GetSkillsAsync(guid, ct);
        session.Progression.SkillBook.Init(languageSlots, persisted.Select(s => (s.SkillId, s.Value, s.Max)));
    }

    /// <summary>Текущее значение/потолок навыка или null, если не изучен.</summary>
    internal (ushort Value, ushort Max)? Get(WorldSession session, ushort skillId)
    {
        var s = session.Progression.SkillBook.Get(skillId);
        return s is null ? null : (s.Value, s.Max);
    }

    /// <summary>Выдаёт/обновляет навык (изучение профессии или смена потолка): персист + апдейт клиенту.</summary>
    internal async Task GrantAsync(WorldSession session, ushort skillId, ushort value, ushort max,
        CancellationToken ct)
    {
        session.Progression.SkillBook.AddOrSet(skillId, value, max);
        await charState.UpsertSkillAsync(session.InWorldGuid, skillId, value, max, ct);
        await SendSkillUpdateAsync(session, skillId, value, max, ct);
    }

    /// <summary>Поднимает значение навыка на <paramref name="delta"/> (но не выше потолка). Возвращает
    /// новое значение, либо null, если навыка нет или он уже в потолке.</summary>
    internal async Task<ushort?> AddValueAsync(WorldSession session, ushort skillId, ushort delta,
        CancellationToken ct)
    {
        var sk = session.Progression.SkillBook.Get(skillId);
        if (sk is null)
            return null;
        var newValue = (ushort)Math.Min(sk.Max, sk.Value + delta);
        if (newValue == sk.Value)
            return null;
        sk.Value = newValue;
        await charState.UpsertSkillAsync(session.InWorldGuid, skillId, sk.Value, sk.Max, ct);
        await SendSkillUpdateAsync(session, skillId, sk.Value, sk.Max, ct);
        return newValue;
    }

    /// <summary>Поднимает потолок навыка (изучение следующего тира у тренера). M11.5.</summary>
    internal async Task SetMaxAsync(WorldSession session, ushort skillId, ushort max, CancellationToken ct)
    {
        var sk = session.Progression.SkillBook.Get(skillId);
        if (sk is null || max <= sk.Max)
            return;
        sk.Max = max;
        await charState.UpsertSkillAsync(session.InWorldGuid, skillId, sk.Value, sk.Max, ct);
        await SendSkillUpdateAsync(session, skillId, sk.Value, sk.Max, ct);
    }

    /// <summary>Забывает навык (§177): убирает из книги и <c>character_skill</c>, затем пересылает блок
    /// PLAYER_SKILL_INFO (слоты после удалённого сдвигаются). false — навык не был изучен.</summary>
    internal async Task<bool> ForgetAsync(WorldSession session, ushort skillId, CancellationToken ct)
    {
        var book = session.Progression.SkillBook;
        if (!book.Has(skillId))
            return false;
        var oldCount = book.Skills.Count; // число слотов профессий ДО удаления (хвост обнулим)
        book.Remove(skillId);
        await charState.DeleteSkillAsync(session.InWorldGuid, skillId, ct);
        await ResendSkillBlockAsync(session, oldCount, ct);
        return true;
    }

    /// <summary>Перезаписывает слоты PLAYER_SKILL_INFO профессий [LanguageSlots .. +oldCount): текущие
    /// навыки на новых позициях, освободившийся хвост — нулями. Один VALUES-апдейт себе (§177).</summary>
    private static async Task ResendSkillBlockAsync(WorldSession session, int oldCount, CancellationToken ct)
    {
        var book = session.Progression.SkillBook;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                for (var i = 0; i < oldCount; i++)
                {
                    var baseIdx = UpdateField.PlayerSkillInfo11 + (book.LanguageSlots + i) * 3;
                    if (i < book.Skills.Count)
                    {
                        var sk = book.Skills[i];
                        m.SetUInt32(baseIdx, sk.SkillId);
                        m.SetUInt32(baseIdx + 1, (uint)(sk.Value | (sk.Max << 16)));
                        m.SetUInt32(baseIdx + 2, 0);
                    }
                    else
                    {
                        m.SetUInt32(baseIdx, 0);
                        m.SetUInt32(baseIdx + 1, 0);
                        m.SetUInt32(baseIdx + 2, 0);
                    }
                }
            }), ct);
    }

    /// <summary>VALUES-апдейт одного слота PLAYER_SKILL_INFO (skillId | value | max), только себе.</summary>
    private static async Task SendSkillUpdateAsync(WorldSession session, ushort skillId, ushort value, ushort max,
        CancellationToken ct)
    {
        var slot = session.Progression.SkillBook.SlotOf(skillId);
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
