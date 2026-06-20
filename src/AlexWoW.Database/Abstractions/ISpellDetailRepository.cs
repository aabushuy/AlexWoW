using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Read-only доступ к деталям спелла (<c>mangos.spell_template</c> + имена реагентов из
/// <c>mangos.item_template</c>) для блока детализации аддона AlexQATester. Отдельный репозиторий (SRP) —
/// не пересекается с движковым <see cref="ISpellTemplateRepository"/> (там поля под каст). Пустая строка
/// подключения / отсутствующий спелл → <see langword="null"/>.
/// </summary>
public interface ISpellDetailRepository
{
    /// <summary>Настроена ли строка подключения (пусто = всегда <see langword="null"/>).</summary>
    bool Configured { get; }

    /// <summary>Детали спелла по id; нет такого спелла → <see langword="null"/>.</summary>
    Task<SpellDetail?> GetAsync(uint spellId, CancellationToken ct = default);
}
