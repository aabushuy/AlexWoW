using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы спеллов (M6.4, DI-модуль M7 S3): тонкие обработчики CMSG_CAST_SPELL/CMSG_CANCEL_CAST,
/// делегирующие в <see cref="SpellCastService"/>. Логика разнесена по SRP: данные —
/// <see cref="World.SpellCatalog"/>, оркестрация каста — <see cref="SpellCastService"/>, завершение —
/// <see cref="SpellCastCompletion"/>, эффекты — <see cref="SpellEffectsService"/>, переключатели —
/// <see cref="SpellTogglesService"/>, реген маны — <see cref="ManaRegenService"/>, пакеты —
/// <see cref="Protocol.SpellPackets"/>.
/// </summary>
internal sealed class SpellHandlers(SpellCastService spellCast, AuraService auras, PeriodicsService periodics)
    : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgCastSpell)]
    public Task OnCastSpell(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => spellCast.HandleCastAsync(session, packet, ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgCancelCast)]
    public Task OnCancelCast(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        spellCast.CancelCast(session);
        return Task.CompletedTask;
    }

    /// <summary>
    /// CMSG_CANCEL_AURA (M10.4c): игрок снял свой бафф правым кликом по иконке (u32 spell). Снимаем ауру-
    /// иконку (<see cref="AuraService"/>) и связанный простой эффект/HoT (<see cref="PeriodicsService"/>,
    /// +макс.HP откатим).
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgCancelAura)]
    public async Task OnCancelAura(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var spellId = packet.Reader().UInt32();
        // Toggle-формы/стойки/ауры/аспекты/присутствия правым кликом по иконке НЕ снимаются (как на оффе) —
        // иначе кнопка формы рассинхронится (выход «не по протоколу»). Из формы выходят повторным кастом кнопки.
        if (SpellCatalog.TryGetToggle(spellId, out _))
            return;
        await auras.RemoveAsync(session, spellId, ct);
        await periodics.CancelSelfAsync(session, spellId, ct);
    }
}
