namespace AlexWoW.WorldServer.World;

/// <summary>
/// Прогресс активного квеста в журнале (M6.5 инкр.3): цели-существа (kill/talk) с счётчиками + флаг
/// завершения. Item-цели пока не отслеживаются (квесты с ними не авто-завершаются). В памяти; персист — позже.
/// </summary>
public sealed class QuestProgress
{
    public required uint QuestId { get; init; }
    /// <summary>ReqCreatureOrGo entry по цели 0..3 (GO — отрицательное); 0 — нет цели.</summary>
    public int[] Creature { get; } = new int[4];
    public uint[] Required { get; } = new uint[4];
    public uint[] Count { get; } = new uint[4];
    /// <summary>Item-цели (M6.10): entry требуемого предмета по цели 0..3 (0 — нет) и нужное количество.
    /// Прогресс item-целей клиент считает сам по сумкам; сервер лишь определяет завершённость по инвентарю.</summary>
    public uint[] ReqItem { get; } = new uint[4];
    public uint[] ReqItemCount { get; } = new uint[4];
    /// <summary>У квеста есть item-цели. M6.5.</summary>
    public bool HasItemObjectives { get; init; }
    /// <summary>Квест выполнен (можно сдавать).</summary>
    public bool Complete { get; set; }

    /// <summary>
    /// Все цели достигнуты: существа (по счётчикам) И предметы (по инвентарю через <paramref name="countItem"/>).
    /// M6.10 — item-цели учитываются (раньше квесты с ними не авто-завершались).
    /// </summary>
    public bool ObjectivesMet(Func<uint, uint> countItem)
    {
        for (var i = 0; i < 4; i++)
        {
            if (Creature[i] != 0 && Count[i] < Required[i])
                return false;
            if (ReqItem[i] != 0 && countItem(ReqItem[i]) < ReqItemCount[i])
                return false;
        }
        return true;
    }
}
