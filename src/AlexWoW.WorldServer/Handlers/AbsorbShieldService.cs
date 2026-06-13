using AlexWoW.WorldServer.Net;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Absorb-щиты (Фаза 2 — ABS.1): поглощение входящего урона по игроку. Щит — само-бафф с пулом поглощения
/// (<see cref="PeriodicEffect.AbsorbRemaining"/>), привязанным к маске школ (PW:Shield/Ice Barrier — все
/// школы; Fire/Frost Ward — своя). Входящий урон (после митигейшна) гасится подходящими щитами до исчерпания
/// пула; опустошённый щит спадает досрочно (снимаем ауру-иконку). Эталон — CMaNGOS <c>Unit::CalculateAbsorb</c>.
/// </summary>
internal sealed class AbsorbShieldService(AuraService auras, ManaRegenService manaRegen)
{
    /// <summary>
    /// Гасит <paramref name="damage"/> активными щитами, поглощающими школу <paramref name="damageSchoolMask"/>.
    /// Возвращает суммарно поглощённое (вызывающий вычитает из урона). Опустошённые щиты снимает (эффект + иконка).
    /// Mana Shield (ABS.2) поглощает за счёт маны: за 1 ед. урона тратится множитель маны, лимит — текущая мана.
    /// </summary>
    internal async Task<uint> AbsorbAsync(WorldSession session, byte damageSchoolMask, uint damage, CancellationToken ct)
    {
        if (damage == 0)
            return 0;

        uint absorbed = 0;
        var manaDrained = false;
        var shields = session.Progression.Periodics
            .Where(p => p.TargetGuid == 0 && p.AbsorbRemaining > 0 && (p.AbsorbSchoolMask & damageSchoolMask) != 0)
            .ToList();

        foreach (var shield in shields)
        {
            if (damage == 0)
                break;
            var take = Math.Min(shield.AbsorbRemaining, (int)damage);

            // ABS.2: Mana Shield — поглощение лимитировано маной (mana / множитель), трата = поглощено × множитель.
            if (shield.ManaShieldMultiplier > 0f)
            {
                var maxByMana = (int)(session.Cast.Mana / shield.ManaShieldMultiplier);
                take = Math.Min(take, maxByMana);
                if (take <= 0)
                    continue; // нет маны — щит держится, но ничего не гасит
                var manaCost = (uint)(take * shield.ManaShieldMultiplier);
                session.Cast.Mana = session.Cast.Mana > manaCost ? session.Cast.Mana - manaCost : 0;
                manaDrained = true;
            }

            shield.AbsorbRemaining -= take;
            damage -= (uint)take;
            absorbed += (uint)take;
            if (shield.AbsorbRemaining <= 0)
            {
                // Щит исчерпан — спадает досрочно: убираем эффект и снимаем ауру-иконку у клиента.
                session.Progression.Periodics.Remove(shield);
                await auras.RemoveAsync(session, shield.SpellId, ct);
                session.Logger.LogDebug("ABSORB '{User}': щит {Spell} исчерпан, спал", session.Account, shield.SpellId);
            }
        }

        if (manaDrained)
            await manaRegen.SendManaUpdateAsync(session, ct);

        return absorbed;
    }
}
