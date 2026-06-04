namespace AlexWoW.WorldServer.World;

/// <summary>
/// Сролленный лут конкретного трупа (M6.6): деньги + слоты предметов с флагом «забран».
/// Роллится один раз при смерти существа, живёт на <see cref="WorldCreature"/> до респавна.
/// </summary>
public sealed class CreatureLoot
{
    /// <summary>Деньги (медь); 0 после взятия.</summary>
    public uint Gold { get; set; }

    /// <summary>Слоты предметов (индекс стабилен — клиент ссылается на него при взятии).</summary>
    public required List<LootSlot> Slots { get; init; }

    /// <summary>Весь лут разобран (деньги взяты и все предметы забраны) — труп больше не lootable.</summary>
    public bool IsEmpty => Gold == 0 && Slots.All(s => s.Taken);
}

/// <summary>Один предмет в луте трупа. M6.6.</summary>
public sealed class LootSlot
{
    public required byte Index { get; init; }
    public required uint ItemId { get; init; }
    public required uint Count { get; init; }
    public required uint DisplayId { get; init; }
    public bool Taken { get; set; }
}
