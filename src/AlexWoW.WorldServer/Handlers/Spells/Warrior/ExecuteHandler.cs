using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers.Spells.Warrior;

/// <summary>
/// Воин — Казнь (Execute): требует HP цели ≤ 20% (CasterAuraState=2 AURA_STATE_HEALTHLESS_20_PERCENT —
/// гейт уже в spell_template и SpellCastService). Урон = BasePoints+1 + всю ярость выше 15 ×
/// damage_per_rage коэффициента (per-spellId константа из CMaNGOS). После каста — расходует ВСЮ ярость
/// (CMaNGOS <c>Spell::EffectDummy</c> case Execute: `m_caster->SetPower(POWER_RAGE, 0)`).
/// Glyph of Execution (58367) добавляет 10 «вирт.» ярости в коэффициент — отдельный регрессионный тикет.
/// </summary>
[EffectDummyHandler(5308, 20660, 20661, 20662, 25234, 25236, 47470, 47471)]
internal sealed class ExecuteHandler(SpellEffectsService effects) : IEffectDummyHandler
{
    /// <summary>Минимальная ярость для каста (15 = 150 в нашей ×10-нотации).</summary>
    private const uint MinRageX10 = 150;

    /// <summary>Урон на каждую дополнительную единицу ярости (выше 15) — эталон CMaNGOS spell_dummy_effects,
    /// per-rank. Усреднено по рангам 5308→47471 (рост 2 → 38); храним по spellId.</summary>
    private static readonly Dictionary<uint, int> DamagePerRage = new()
    {
        [5308] = 3,    // rank 1 (level 20)
        [20660] = 5,   // rank 2
        [20661] = 7,   // rank 3
        [20662] = 9,   // rank 4
        [25234] = 21,  // rank 5
        [25236] = 26,  // rank 6
        [47470] = 35,  // rank 7
        [47471] = 38,  // rank 8 (level 80)
    };

    public Task<bool> ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct)
    {
        var rage = session.Combat.Rage;
        var extraRageX10 = rage > MinRageX10 ? rage - MinRageX10 : 0; // ярость выше 15 в ×10-нотации
        var extraRage = (int)(extraRageX10 / 10);                     // в «настоящих» единицах
        var perRage = DamagePerRage.GetValueOrDefault(spellId, 3);
        var totalDamage = Math.Max(0, info.DummyBasePoints) + extraRage * perRage;

        // Урон — флэт спелл-урон (не оружие), школа из spell_template (физика).
        var modified = info with
        {
            WeaponDamage = false,
            WeaponPercent = 0,
            MinAmount = totalDamage,
            MaxAmount = totalDamage,
        };

        // CMaNGOS: после Execute расходуется ВСЯ ярость. Стандартный path списал 15 (MinRageX10) на гейте;
        // обнуляем остаток здесь (перед ApplyDamage, чтобы UI-сводка ярости в логах боя соответствовала).
        session.Combat.Rage = 0;

        return ApplyAndReportAsync(session, spellId, modified, targetGuid, now, ct);
    }

    private async Task<bool> ApplyAndReportAsync(WorldSession session, uint spellId,
        SpellCatalog.SpellInfo modified, ulong targetGuid, long now, CancellationToken ct)
    {
        await effects.ApplyDamageAsync(session, spellId, modified, targetGuid, now, ct);
        return true;
    }
}
