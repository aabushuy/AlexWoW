using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Система аур (M6.11, DI-сервис M7 S3 — бывший статик Auras): применение/снятие/истечение аур игрока
/// (баффы, дебаффы, формы/стойки) и их показ клиенту (SMSG_AURA_UPDATE) + себе и наблюдателям. Аура-форма
/// (ShapeshiftForm != 0) выставляет форму в UNIT_FIELD_BYTES_2 → клиент меняет набор кнопок (стойки воина,
/// M6.12; формы друида — позже). Фундамент под расход/эффекты абилок и баффы спеллов. Точка применения —
/// каст спелла с аура-эффектом (SpellHandlers). Персист через релог — <see cref="AuraPersistenceService"/>.
/// </summary>
internal sealed class AuraService(ICharacterStateRepository charState, CombatResourcesService combatResources)
{
    private const int MaxAuraSlots = 56; // визуальные слоты аур игрока (3.3.5)

    /// <summary>
    /// Накладывает ауру (M6.11/M7 #21). <paramref name="form"/> != 0 — аура-форма (стойка/форма друида):
    /// выставляет UNIT_FIELD_BYTES_2. <paramref name="group"/> != 0 — эксклюзивная группа-переключатель
    /// (стойки/формы, ауры паладина, аспекты охотника): новый снимает прочие той же группы.
    /// <paramref name="durationMs"/> = 0 — перманентная (переключатель) → персистится через релог
    /// (если <paramref name="persist"/>). Повтор того же спелла — рефреш.
    /// </summary>
    internal async Task ApplyAsync(WorldSession session, uint spellId, int durationMs,
        bool positive, byte form, CancellationToken ct, byte group = 0, bool persist = false,
        int damageDonePct = 0, byte damageDoneSchool = 0, int damageTakenPct = 0)
    {
        if (session.InWorldGuid == 0)
            return;

        // Эксклюзивность переключателя: снять прочие ауры той же группы. Форму НЕ сбрасываем в 0, если новая
        // тоже задаёт форму (resetForm:false) — иначе подсветка стойки мигает/запаздывает (M7 #21).
        if (group != 0)
        {
            foreach (var g in session.Progression.Auras.Where(a => a.Group == group).ToList())
                await RemoveInternalAsync(session, g, resetForm: form == 0, ct);
        }

        // Рефреш: повтор того же спелла — снять старый экземпляр.
        var dup = session.Progression.Auras.FirstOrDefault(a => a.SpellId == spellId);
        if (dup is not null)
            await RemoveInternalAsync(session, dup, resetForm: true, ct);

        // Само-каст (SELF_CAST) обязателен — иначе клиент ждёт PackedGuid кастера и десинхронит поток.
        byte flags = (byte)(AuraFlags.Effect1 | AuraFlags.SelfCast);
        flags |= positive ? AuraFlags.Positive : AuraFlags.Negative;
        if (durationMs > 0)
            flags |= AuraFlags.Duration;

        var doPersist = persist && durationMs == 0; // персистим только перманентные переключатели
        var aura = new ActiveAura
        {
            SpellId = spellId,
            Slot = FirstFreeSlot(session),
            Flags = flags,
            ShapeshiftForm = form,
            Group = group,
            Persist = doPersist,
            DurationMs = durationMs,
            ExpiresAtMs = durationMs > 0 ? Environment.TickCount64 + durationMs : 0,
            DamageDonePct = damageDonePct,
            DamageDoneSchool = damageDoneSchool,
            DamageTakenPct = damageTakenPct,
        };
        session.Progression.Auras.Add(aura);

        // Сначала аура (SMSG_AURA_UPDATE), затем форма (UNIT_FIELD_BYTES_2) — порядок как в CMaNGOS,
        // чтобы клиент сразу подсветил активную стойку (M7 #21).
        var level = (byte)(session.Character?.Level ?? 1);
        await session.World.BroadcastToPlayerObserversAsync(session.Player!, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApply((ulong)session.InWorldGuid, aura.Slot, spellId, flags, level, 1, durationMs), ct);

        if (form != 0)
        {
            session.Progression.ShapeshiftForm = form;
            await BroadcastFormAsync(session, ct);
            // §1 Формы друида: сменить тип ресурса под форму (медведь→ярость, кошка→энергия). Централизованно
            // здесь — покрывает вход и смену формы любым путём (toggle/персист). Не-друид/не-фераль — no-op.
            await combatResources.ApplyFormPowerAsync(session, form, ct);
        }

        if (doPersist)
        {
            try { await charState.AddAuraAsync(session.InWorldGuid, spellId, form, ct); }
            catch { /* персист не критичен для текущей сессии */ }
        }
    }

    /// <summary>Снимает ауру по spellId, если есть (M6.11).</summary>
    internal async Task RemoveAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        var aura = session.Progression.Auras.FirstOrDefault(a => a.SpellId == spellId);
        if (aura is not null)
            await RemoveInternalAsync(session, aura, resetForm: true, ct);
    }

    /// <summary>Тик истечения аур (M6.11): снимает ауры с вышедшим таймером. Из WorldTick.UpdateAsync.</summary>
    internal async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.Progression.Auras.Count == 0)
            return;
        foreach (var aura in session.Progression.Auras.Where(a => a.ExpiresAtMs != 0 && now >= a.ExpiresAtMs).ToList())
            await RemoveInternalAsync(session, aura, resetForm: true, ct);
    }

    /// <summary>
    /// Снимает ауру (M6.11). <paramref name="resetForm"/>=false — НЕ сбрасывать форму в 0 (при смене стойки
    /// новая форма выставится сразу следом, без промежуточного «нет стойки» → без мигания подсветки, M7 #21).
    /// </summary>
    private async Task RemoveInternalAsync(WorldSession session, ActiveAura aura, bool resetForm, CancellationToken ct)
    {
        session.Progression.Auras.Remove(aura);
        if (resetForm && aura.ShapeshiftForm != 0 && session.Progression.ShapeshiftForm == aura.ShapeshiftForm)
        {
            session.Progression.ShapeshiftForm = 0;
            await BroadcastFormAsync(session, ct);
            // §1 Формы друида: ЛЮБОЙ полный выход из формы (повторный каст / отмена через не-форменную абилку
            // CMSG_CANCEL_AURA / истечение) → вернуть тип ресурса в ману. Централизация чинит «частичный выход».
            await combatResources.ApplyFormPowerAsync(session, 0, ct);
        }
        if (aura.Persist)
        {
            try { await charState.RemoveAuraAsync(session.InWorldGuid, aura.SpellId, ct); }
            catch { /* персист не критичен */ }
        }

        if (session.Player is { } player)
        {
            await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAuraUpdate,
                AuraPackets.BuildRemove((ulong)session.InWorldGuid, aura.Slot), ct);
        }

        // Кнопка формы/стойки залипает «дожатой» после снятия ауры, пока клиент не получит SMSG_COOLDOWN_EVENT
        // (переводит спелл active→ready). Нужно ВСЕМ шейпшифт-кнопкам, а не только COOLDOWN_ON_EVENT
        // (Shadowform/Stealth) — формы друида кнопку тоже не отжимали. Клиент берёт длительность КД из DBC:
        // у форм без кулдауна (медведь/кошка) событие просто отжимает кнопку (КД 0). Только полный выход (resetForm).
        if (resetForm && aura.ShapeshiftForm != 0)
            await session.SendAsync(WorldOpcode.SmsgCooldownEvent,
                SpellPackets.BuildCooldownEvent((ulong)session.InWorldGuid, aura.SpellId), ct);
    }

    /// <summary>VALUES-апдейт UNIT_FIELD_BYTES_2 (байт 3 = форма) себе и наблюдателям. M6.11.
    /// Internal: восстановление персиста (<see cref="AuraPersistenceService"/>) выставляет форму напрямую.</summary>
    internal Task BroadcastFormAsync(WorldSession session, CancellationToken ct)
    {
        // sheath/pvp/pet байты пока не используем (0); форма — старший байт.
        var bytes2 = (uint)session.Progression.ShapeshiftForm << 24;
        return session.World.BroadcastToPlayerObserversAsync(session.Player!, WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.UnitBytes2, bytes2)), ct);
    }

    /// <summary>Первый свободный визуальный слот ауры (чистый хелпер; нужен и персисту аур).</summary>
    internal static byte FirstFreeSlot(WorldSession session)
    {
        for (var slot = 0; slot < MaxAuraSlots; slot++)
        {
            if (session.Progression.Auras.All(a => a.Slot != slot))
                return (byte)slot;
        }

        return (byte)(MaxAuraSlots - 1); // переполнение — переиспользуем последний (крайний случай)
    }
}
