using AlexWoW.Web;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AlexWoW.Web.Services.Kanban;

/// <summary>
/// Подгрузка минимального тултипа спелла для регрессионных тикетов канбана (Phase E плана).
/// Источник — <c>mangos.spell_template</c>. Только web-просмотр, не пересекается с серверным
/// <see cref="AlexWoW.Database.Abstractions.ISpellTemplateRepository"/> (там 100+ полей под движок каста).
/// Здесь нужны только поля, видимые в игровом тултипе.
/// </summary>
public sealed class SpellPreviewService(IOptions<WebOptions> options, SpellIconService icons)
{
    private readonly string _cs = options.Value.WorldConnectionString;

    public bool Configured => !string.IsNullOrWhiteSpace(_cs);

    public async Task<SpellPreview?> GetAsync(uint spellId, CancellationToken ct)
    {
        if (!Configured) return null;
        await using var c = new MySqlConnection(_cs);
        await c.OpenAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<SpellRow>(new CommandDefinition(
            "SELECT Id, SpellName, SpellLevel, SpellFamilyName, SchoolMask, ManaCost, PowerType, " +
            "       Effect1, EffectApplyAuraName1, EffectBasePoints1, EffectDieSides1, " +
            "       Effect2, EffectBasePoints2, Effect3, EffectBasePoints3, " +
            "       RecoveryTime, DurationIndex, SpellIconID " +
            "FROM mangos.spell_template WHERE Id = @id",
            new { id = spellId }, cancellationToken: ct));

        return row is null ? null : new SpellPreview(
            Id: row.Id,
            Name: row.SpellName,
            Level: row.SpellLevel,
            FamilyName: FamilyName(row.SpellFamilyName),
            School: SchoolName(row.SchoolMask),
            ManaCost: row.ManaCost,
            PowerType: PowerType(row.PowerType),
            Effect1: EffectName(row.Effect1),
            EffectAura1: row.EffectApplyAuraName1 == 0 ? null : AuraName(row.EffectApplyAuraName1),
            BasePoints1: row.EffectBasePoints1 + 1, // CMaNGOS-конвенция: BasePoints хранится со смещением -1
            DieSides1: row.EffectDieSides1,
            Effect2: row.Effect2 == 0 ? null : EffectName(row.Effect2),
            Effect3: row.Effect3 == 0 ? null : EffectName(row.Effect3),
            RecoveryMs: row.RecoveryTime,
            DurationIndex: row.DurationIndex,
            IconUrl: icons.IconUrl(row.SpellIconID));
    }

    // ─── Маппинги (минимальные, в плюс к коду — для UI) ─────────────────────

    private static string SchoolName(uint mask) => mask switch
    {
        1 => "Физическая", 2 => "Священная", 4 => "Огонь", 8 => "Природа",
        16 => "Лёд", 32 => "Тень", 64 => "Тайная магия",
        _ => $"маска {mask}",
    };

    private static string PowerType(int p) => p switch
    {
        0 => "мана", 1 => "ярость", 2 => "фокус", 3 => "энергия",
        5 => "здоровье", 6 => "рунич. сила", 7 => "руны",
        _ => $"power#{p}",
    };

    private static string FamilyName(uint f) => f switch
    {
        0 => "Общие/расовые", 3 => "Маг", 4 => "Воин", 5 => "Чернокнижник",
        6 => "Жрец", 7 => "Друид", 8 => "Разбойник", 9 => "Охотник",
        10 => "Паладин", 11 => "Шаман", 15 => "Рыцарь смерти",
        _ => $"family#{f}",
    };

    // Самые частые SpellEffect (см. tools/regression-import/template.py — синхронно).
    private static string EffectName(int eff) => eff switch
    {
        0 => "—",
        2 => "Прямой урон школой (SCHOOL_DAMAGE)",
        3 => "Dummy (скриптовый)",
        6 => "Наложение ауры (APPLY_AURA)",
        8 => "Восстановление ресурса (ENERGIZE)",
        10 => "Прямое лечение (HEAL)",
        24 => "Создать предмет",
        26 => "Открыть замок",
        38 => "Прерывание каста",
        64 => "Триггер другого спелла",
        77 => "Скриптовый эффект",
        108 => "Применить глиф",
        113 => "Profiсiency",
        _ => $"effect#{eff}",
    };

    private static string AuraName(int aura) => aura switch
    {
        3 => "Dummy",
        4 => "Конфьюз", 7 => "Страх", 8 => "Периодическое лечение (HoT)",
        12 => "Стан", 13 => "+ урон",
        15 => "Damage shield", 22 => "+ сопротивление",
        23 => "Триггер периодически", 24 => "Периодическое восстановление",
        27 => "Periodic leech",
        29 => "+ статы", 31 => "+ скорость передвижения", 33 => "- скорость", 50 => "Прок-триггер",
        99 => "+ ATK Power", 107 => "Spellmod (flat)", 108 => "Spellmod (pct)",
        158 => "+ хил", 216 => "+ скорость каста",
        _ => $"aura#{aura}",
    };

    private sealed record SpellRow
    {
        public uint Id { get; init; }
        public string SpellName { get; init; } = "";
        public uint SpellLevel { get; init; }
        public uint SpellFamilyName { get; init; }
        public uint SchoolMask { get; init; }
        public uint ManaCost { get; init; }
        public int PowerType { get; init; }
        public int Effect1 { get; init; }
        public int EffectApplyAuraName1 { get; init; }
        public int EffectBasePoints1 { get; init; }
        public int EffectDieSides1 { get; init; }
        public int Effect2 { get; init; }
        public int EffectBasePoints2 { get; init; }
        public int Effect3 { get; init; }
        public int EffectBasePoints3 { get; init; }
        public uint RecoveryTime { get; init; }
        public uint DurationIndex { get; init; }
        public uint SpellIconID { get; init; }
    }
}

/// <summary>Срез spell_template для preview-блока на /Ticket.</summary>
public sealed record SpellPreview(
    uint Id,
    string Name,
    uint Level,
    string FamilyName,
    string School,
    uint ManaCost,
    string PowerType,
    string Effect1,
    string? EffectAura1,
    int BasePoints1,
    int DieSides1,
    string? Effect2,
    string? Effect3,
    uint RecoveryMs,
    uint DurationIndex,
    string IconUrl);
