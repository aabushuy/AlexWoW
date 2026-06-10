using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Отправка SMSG_SPELL_GO (M6.4, выделено из SpellCaster в M7 S3): кастеру (снаряд) и наблюдателям
/// (broadcast). Общая точка для обычного каста (<see cref="SpellCastService"/>/<see cref="SpellCastCompletion"/>)
/// и переключателей (<see cref="SpellTogglesService"/>) — разрывает цикл «каст ↔ переключатели».
/// </summary>
internal sealed class SpellGoSender
{
    /// <summary>SMSG_SPELL_GO кастеру (снаряд) и наблюдателям (broadcast).</summary>
    public async Task SendSpellGoAsync(WorldSession session, uint spellId, ulong targetGuid, byte castCount, CancellationToken ct)
    {
        // cast_count берём ПАРАМЕТРОМ, а не из session.CastCount: повторное нажатие во время каста
        // перезатирает session.CastCount, и GO завершения исходного каста ушёл бы с чужим счётчиком →
        // клиент не сопоставит GO со своим pending-кастом → залипание завершения каста (M10.4a фикс #26).
        var body = SpellPackets.BuildSpellGo((ulong)session.InWorldGuid, spellId, targetGuid, castCount);
        await session.SendAsync(WorldOpcode.SmsgSpellGo, body, ct);   // кастеру (снаряд)
        if (session.Player is { } player)                            // и наблюдателям
            await session.World.BroadcastToNeighborsAsync(player, WorldOpcode.SmsgSpellGo, body, ct);
    }
}
