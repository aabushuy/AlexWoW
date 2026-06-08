using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Система аур (M6.11): применение/снятие/истечение аур игрока (баффы, дебаффы, формы/стойки) и их показ
/// клиенту (SMSG_AURA_UPDATE) + себе и наблюдателям. Аура-форма (ShapeshiftForm != 0) выставляет форму в
/// UNIT_FIELD_BYTES_2 → клиент меняет набор кнопок (стойки воина, M6.12; формы друида — позже). Фундамент
/// под расход/эффекты абилок и баффы спеллов. Точка применения — каст спелла с аура-эффектом (SpellHandlers).
/// </summary>
public static class Auras
{
    private const int MaxAuraSlots = 56; // визуальные слоты аур игрока (3.3.5)

    /// <summary>
    /// Накладывает ауру (M6.11). <paramref name="form"/> != 0 — аура-форма (стойка): снимает предыдущую
    /// форму и выставляет новую в UNIT_FIELD_BYTES_2. <paramref name="positive"/> — бафф (cancellable) vs
    /// дебафф (negative). <paramref name="durationMs"/> = 0 — перманентная. Повторное наложение того же
    /// спелла обновляет ауру (рефреш).
    /// </summary>
    internal static async Task ApplyAsync(WorldSession session, uint spellId, int durationMs,
        bool positive, byte form, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;

        // Форма эксклюзивна: снять прочие аура-формы (другую стойку) перед наложением новой.
        if (form != 0)
            foreach (var f in session.Auras.Where(a => a.ShapeshiftForm != 0).ToList())
                await RemoveInternalAsync(session, f, ct);

        // Рефреш: повтор того же спелла — снять старый экземпляр.
        var dup = session.Auras.FirstOrDefault(a => a.SpellId == spellId);
        if (dup is not null)
            await RemoveInternalAsync(session, dup, ct);

        // Само-каст (SELF_CAST) обязателен — иначе клиент ждёт PackedGuid кастера и десинхронит поток.
        byte flags = (byte)(AuraFlags.Effect1 | AuraFlags.SelfCast);
        flags |= positive ? AuraFlags.Positive : AuraFlags.Negative;
        if (durationMs > 0)
            flags |= AuraFlags.Duration;

        var aura = new ActiveAura
        {
            SpellId = spellId,
            Slot = FirstFreeSlot(session),
            Flags = flags,
            ShapeshiftForm = form,
            DurationMs = durationMs,
            ExpiresAtMs = durationMs > 0 ? Environment.TickCount64 + durationMs : 0,
        };
        session.Auras.Add(aura);

        if (form != 0)
        {
            session.ShapeshiftForm = form;
            await BroadcastFormAsync(session, ct);
        }

        var level = (byte)(session.Character?.Level ?? 1);
        await session.World.BroadcastToPlayerObserversAsync(session.Player!, WorldOpcode.SmsgAuraUpdate,
            AuraPackets.BuildApply((ulong)session.InWorldGuid, aura.Slot, spellId, flags, level, 1, durationMs), ct);
    }

    /// <summary>Снимает ауру по spellId, если есть (M6.11).</summary>
    internal static async Task RemoveAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        var aura = session.Auras.FirstOrDefault(a => a.SpellId == spellId);
        if (aura is not null)
            await RemoveInternalAsync(session, aura, ct);
    }

    /// <summary>Тик истечения аур (M6.11): снимает ауры с вышедшим таймером. Из WorldState.UpdateAsync.</summary>
    internal static async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        if (session.Auras.Count == 0)
            return;
        foreach (var aura in session.Auras.Where(a => a.ExpiresAtMs != 0 && now >= a.ExpiresAtMs).ToList())
            await RemoveInternalAsync(session, aura, ct);
    }

    private static async Task RemoveInternalAsync(WorldSession session, ActiveAura aura, CancellationToken ct)
    {
        session.Auras.Remove(aura);
        if (aura.ShapeshiftForm != 0 && session.ShapeshiftForm == aura.ShapeshiftForm)
        {
            session.ShapeshiftForm = 0;
            await BroadcastFormAsync(session, ct);
        }
        if (session.Player is { } player)
            await session.World.BroadcastToPlayerObserversAsync(player, WorldOpcode.SmsgAuraUpdate,
                AuraPackets.BuildRemove((ulong)session.InWorldGuid, aura.Slot), ct);
    }

    /// <summary>VALUES-апдейт UNIT_FIELD_BYTES_2 (байт 3 = форма) себе и наблюдателям. M6.11.</summary>
    private static Task BroadcastFormAsync(WorldSession session, CancellationToken ct)
    {
        // sheath/pvp/pet байты пока не используем (0); форма — старший байт.
        var bytes2 = (uint)session.ShapeshiftForm << 24;
        return session.World.BroadcastToPlayerObserversAsync(session.Player!, WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.UnitBytes2, bytes2)), ct);
    }

    private static byte FirstFreeSlot(WorldSession session)
    {
        for (var slot = 0; slot < MaxAuraSlots; slot++)
            if (session.Auras.All(a => a.Slot != slot))
                return (byte)slot;
        return (byte)(MaxAuraSlots - 1); // переполнение — переиспользуем последний (крайний случай)
    }
}
