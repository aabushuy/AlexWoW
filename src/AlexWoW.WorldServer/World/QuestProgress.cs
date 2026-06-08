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
    /// <summary>У квеста есть item-цели (их не трекаем → не авто-завершаем). M6.5.</summary>
    public bool HasItemObjectives { get; init; }
    /// <summary>Квест выполнен (можно сдавать).</summary>
    public bool Complete { get; set; }

    /// <summary>Все цели-существа достигнуты (или их нет).</summary>
    public bool CreatureObjectivesMet()
    {
        for (var i = 0; i < 4; i++)
            if (Creature[i] != 0 && Count[i] < Required[i])
                return false;
        return true;
    }
}
