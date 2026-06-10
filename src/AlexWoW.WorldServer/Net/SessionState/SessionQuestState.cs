namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Квестовое состояние сессии: журнал и сданные квесты.
/// Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionQuestState
{
    /// <summary>Журнал квестов: слот (0..24) → прогресс (null — пусто). Персист — позже. M6.5.</summary>
    internal World.QuestProgress?[] QuestSlots { get; } = new World.QuestProgress?[Protocol.UpdateField.QuestLogSlots];
    /// <summary>Сданные квесты (для предусловий PrevQuestId и анти-повтора). Персист — позже. M6.5.</summary>
    internal HashSet<uint> CompletedQuests { get; } = new();

    /// <summary>Сброс при выходе из мира (как в LeaveWorld и раньше).</summary>
    internal void Reset()
    {
        Array.Clear(QuestSlots); // M6.5: журнал квестов (в памяти) сбрасывается при выходе
        CompletedQuests.Clear();
    }
}
