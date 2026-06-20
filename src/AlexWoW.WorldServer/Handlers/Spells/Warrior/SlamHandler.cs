using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers.Spells.Warrior;

/// <summary>
/// Воин — Раскол (Slam): мили-абилка с кастом ~1.5с, урон оружия + бонус из BasePoints+1 эффекта DUMMY.
/// Эталон CMaNGOS <c>Spell::EffectDummy</c> case-ветка для Slam (1464/8820/11604/11605/25241/25242/47474/47475).
/// Подменяем SpellInfo «на лету» (WeaponDamage=true, MinAmount/MaxAmount = bonus) и отдаём в стандартный
/// <see cref="SpellEffectsService.ApplyDamageAsync"/> — он сам сделает крит, школу, лог, kill-чек, прок.
/// </summary>
[EffectDummyHandler(1464, 8820, 11604, 11605, 25241, 25242, 47474, 47475)]
internal sealed class SlamHandler(SpellEffectsService effects) : IEffectDummyHandler
{
    public Task<bool> ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct)
    {
        var bonus = Math.Max(0, info.DummyBasePoints);
        var modified = info with
        {
            WeaponDamage = true,
            MinAmount = bonus,
            MaxAmount = bonus,
        };
        return ApplyAndReportAsync(session, spellId, modified, targetGuid, now, ct);
    }

    private async Task<bool> ApplyAndReportAsync(WorldSession session, uint spellId,
        SpellCatalog.SpellInfo modified, ulong targetGuid, long now, CancellationToken ct)
    {
        await effects.ApplyDamageAsync(session, spellId, modified, targetGuid, now, ct);
        return true;
    }
}
