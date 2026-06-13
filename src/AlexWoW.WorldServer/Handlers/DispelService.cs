using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Диспел аур (Фаза 2 — DSP): снятие аур по типу диспела (Magic/Curse/Disease/Poison). Защитный диспел
/// (DSP.1) снимает ОДНУ свою дебафф-ауру совпадающего типа (Cleanse/Remove Curse/Dispel Magic). Тип ауры
/// читаем из каталога (<see cref="SpellCatalog.SpellInfo.DispelType"/>), снимаем через <see cref="AuraService"/>.
/// Атакующий Purge/Spellsteal по врагу — DSP.2. Эталон — CMaNGOS <c>Spell::EffectDispel</c>.
/// </summary>
internal sealed class DispelService(SpellCatalog spellCatalog, AuraService auras)
{
    /// <summary>
    /// DSP.1: снимает первую свою дебафф-ауру (AFLAG_NEGATIVE), чей тип входит в <paramref name="dispelMask"/>.
    /// Возвращает spellId снятой ауры (0 — нечего снимать). Один диспел = одна аура (mass dispel — вне scope).
    /// </summary>
    internal async Task<uint> DispelSelfAsync(WorldSession session, byte dispelMask, CancellationToken ct)
    {
        foreach (var aura in session.Progression.Auras.Where(IsDebuff).ToList())
        {
            var info = await spellCatalog.GetAsync(aura.SpellId, ct);
            if (info is { DispelType: > 0 } && (dispelMask & (1 << info.DispelType)) != 0)
            {
                await auras.RemoveAsync(session, aura.SpellId, ct);
                return aura.SpellId;
            }
        }
        return 0;
    }

    private static bool IsDebuff(ActiveAura aura) => (aura.Flags & AuraFlags.Negative) != 0;
}
