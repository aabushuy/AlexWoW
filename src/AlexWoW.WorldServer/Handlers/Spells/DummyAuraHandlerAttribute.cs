namespace AlexWoW.WorldServer.Handlers.Spells;

/// <summary>
/// Маркер класса-обработчика per-spellId DUMMY-ауры (SPELL.T2). Сканируется
/// <see cref="DummyAuraRegistry"/> при старте: spellId → класс с <see cref="IDummyAuraHandler"/>.
/// AllowMultiple — один обработчик может закрывать сразу несколько spellId (ранги таланта).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class DummyAuraHandlerAttribute(params uint[] spellIds) : Attribute
{
    public uint[] SpellIds { get; } = spellIds;
}
