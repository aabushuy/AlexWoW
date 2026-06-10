using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы боя (M6.3/M6.7, DI-модуль M7 S4 — бывший god-класс CombatHandlers): выбор цели,
/// старт/стоп авто-атаки, возрождение. Логика разнесена по SRP: мили игрока —
/// <see cref="PlayerMeleeService"/>, ИИ существ — <see cref="CreatureCombatAI"/>, реген HP —
/// <see cref="RegenService"/>, байты пакетов — <see cref="Protocol.CombatPackets"/>.
/// </summary>
internal sealed class CombatOpcodeHandlers(PlayerMeleeService playerMelee) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgSetSelection)]
    public Task OnSetSelection(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        session.SelectionGuid = reader.UInt64(); // plain Guid (не packed)
        return Task.CompletedTask;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAttackSwing)]
    public Task OnAttackSwing(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var victimGuid = reader.UInt64(); // plain Guid
        return playerMelee.StartAttackAsync(session, victimGuid, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAttackStop)]
    public async Task OnAttackStop(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var enemy = session.CombatTargetGuid;
        if (enemy != 0)
            await playerMelee.StopAttackAsync(session, enemy, ct);
    }

    /// <summary>
    /// CMSG_REPOP_REQUEST («отпустить дух»): возрождаем на месте с полным HP (упрощённо — без
    /// кладбища/бега к трупу; corpse-run добавим позже). M6.7 инкр.1.
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgRepopRequest)]
    public async Task OnRepopRequest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (!session.IsDead || session.Player is not { } player)
            return;
        session.IsDead = false;
        session.Health = session.MaxHealth;
        await session.World.BroadcastPlayerHealthAsync(player, ct);
        session.Logger.LogInformation("RESPAWN '{User}' возродился на месте (полное HP)", session.Account);
    }
}
