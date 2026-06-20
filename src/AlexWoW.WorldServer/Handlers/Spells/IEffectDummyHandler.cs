using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers.Spells;

/// <summary>
/// Обработчик SPELL_EFFECT_DUMMY (Effect=3) per-spellId — кастомная логика «голого» dummy-эффекта
/// (Slam, Execute, Mortal Strike, Bloodthirst и т.п., где CMaNGOS использует <c>Spell::EffectDummy</c>
/// switch). Зеркало <see cref="IDummyAuraHandler"/>, но для эффектов, а не для аур.
/// </summary>
/// <remarks>
/// Контракт: <see cref="ApplyAsync"/> вызывается из <see cref="SpellCastCompletion"/> после прохождения
/// гейтов и SPELL_GO, ПЕРЕД стандартным path'ом ApplyDamage/ApplyHeal. Возвращает <c>true</c> — обработчик
/// «съел» эффект (стандартный путь пропускаем). <c>false</c> — fallback на стандартный путь
/// (актуально, если обработчик частично работает).
/// </remarks>
internal interface IEffectDummyHandler
{
    Task<bool> ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct);
}

/// <summary>Маркер класса-обработчика SPELL_EFFECT_DUMMY для spellId. AllowMultiple — несколько spellId
/// (ранги одной абилки) можно повесить на один класс.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class EffectDummyHandlerAttribute(params uint[] spellIds) : Attribute
{
    public uint[] SpellIds { get; } = spellIds;
}
