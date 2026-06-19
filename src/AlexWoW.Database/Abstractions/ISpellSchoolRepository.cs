namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Узкий read-only доступ к колонке <c>SchoolMask</c> таблицы <c>mangos.spell_template</c> (KB14): нужен
/// аддону AlexQATester для сортировки списка регрессионных тикетов по школе магии. Отдельный репозиторий
/// (SRP) — чтобы не тащить mangos-строку подключения в <see cref="IKanbanBoardRepository"/>.
/// </summary>
public interface ISpellSchoolRepository
{
    /// <summary>Настроена ли строка подключения (пусто = всегда возвращаем пустой словарь).</summary>
    bool Configured { get; }

    /// <summary>Получить <c>SchoolMask</c> для указанных <paramref name="spellIds"/>; отсутствующие — нет ключа.</summary>
    Task<IReadOnlyDictionary<int, int>> GetSchoolMasksAsync(IReadOnlyCollection<int> spellIds, CancellationToken ct = default);
}
