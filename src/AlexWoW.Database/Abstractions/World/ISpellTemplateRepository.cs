using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Спеллы из дампа Spell.dbc (<c>spell_template</c>, БД mangos). SRP-репозиторий (M10.2).</summary>
public interface ISpellTemplateRepository
{
    /// <summary>Данные спелла по id или null, если такого спелла нет в дампе.</summary>
    Task<SpellTemplateData?> GetSpellAsync(uint id, CancellationToken ct = default);
}
