using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы спеллов (M6.4): тонкие обработчики CMSG_CAST_SPELL/CMSG_CANCEL_CAST, делегирующие в
/// <see cref="SpellCaster"/>. Логика разнесена по SRP: данные — <see cref="World.SpellCatalog"/>,
/// оркестрация каста — <see cref="SpellCaster"/>, эффекты — <see cref="SpellEffects"/>, переключатели —
/// <see cref="SpellToggles"/>, реген маны — <see cref="ManaRegen"/>, пакеты — <see cref="Protocol.SpellPackets"/>.
/// </summary>
public static class SpellHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgCastSpell)]
    public static Task OnCastSpell(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => SpellCaster.HandleCastAsync(session, packet, ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgCancelCast)]
    public static Task OnCancelCast(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        SpellCaster.CancelCast(session);
        return Task.CompletedTask;
    }
}
