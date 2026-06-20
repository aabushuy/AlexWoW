using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Spells.Warrior;

/// <summary>
/// Воин — Меч и щит (Sword and Board, баф 50227): следующий Shield Slam бесплатен (через
/// ADD_PCT_MODIFIER −101% от стоимости — наш SpellModifierService уже снимает кост на касте). Дополнительно
/// БУФ должен сбрасывать кулдаун Shield Slam (CMaNGOS Spell::EffectScriptEffect / dummy handler).
/// Бафф вешает talent S&amp;B (46951/46952) через generic ProcService — здесь только пост-обработка наложения.
/// </summary>
[DummyAuraHandler(50227)]
internal sealed class SwordAndBoardHandler : DummyAuraHandlerBase
{
    /// <summary>Все ранги Сокрушения щитом (Shield Slam) — CMaNGOS spell_chain: 23922, 29404, 29405, 30356,
    /// 47487, 47488. CD очищается при наложении ауры 50227 (S&amp;B-эффект).</summary>
    internal static readonly uint[] ShieldSlamRanks = [23922, 29404, 29405, 30356, 47487, 47488];

    public override Task OnApplyAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        // Сбрасываем кулдаун всех рангов Shield Slam — игрок знает максимум один из них в активный момент,
        // но clear по всем — дешёвый словарный lookup, не несёт оверхеда.
        foreach (var sid in ShieldSlamRanks)
            session.Cast.SpellCooldowns.Remove(sid);
        return Task.CompletedTask;
    }
}
