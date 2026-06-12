using System.Reflection;
using System.Text.Json;

namespace AlexWoW.DataStores;

/// <summary>
/// Боевые рейтинги из client GameTable-DBC (3.3.5a, встроенный <c>data/combat_ratings.json</c>): крит и
/// уклонение от ловкости по классу/уровню. Формулы — эталон CMaNGOS (Player::GetMeleeCritFromAgility /
/// GetDodgeFromAgility). Если данные не загрузились — <see cref="Available"/> false, значения 0 (фолбэк).
/// </summary>
public sealed class CombatRatings
{
    private const int MaxClasses = 12;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Базовое уклонение в % на класс (эталон PLAYER_BASE_DODGE). Индекс = id класса.
    private static readonly float[] BaseDodge =
    [
        0.0000f, 3.6640f, 3.4943f, -4.0873f, 2.0957f, 3.4178f,
        3.6640f, 2.1080f, 3.6587f, 2.4211f, 0.0000f, 5.6097f,
    ];

    // Множитель «крит-на-ловкость → уклонение-на-ловкость» (эталон PLAYER_AGI_TO_CRIT_TO_DODGE). Индекс = класс.
    private static readonly float[] AgiToCritToDodge =
    [
        0.0f, 0.85f / 1.15f, 1.00f / 1.15f, 1.11f / 1.15f, 2.00f / 1.15f, 1.00f / 1.15f,
        0.85f / 1.15f, 1.60f / 1.15f, 1.00f / 1.15f, 0.97f / 1.15f, 0.0f, 2.00f / 1.15f,
    ];

    private readonly int _gtMaxLevel;
    private readonly float[] _critBase;   // индекс = class-1
    private readonly float[] _critRatio;  // индекс = (class-1)*gtMaxLevel + (level-1)

    public bool Available { get; }

    public CombatRatings()
    {
        try
        {
            var asm = typeof(CombatRatings).Assembly;
            using var stream = asm.GetManifestResourceStream("AlexWoW.DataStores.data.combat_ratings.json")
                ?? throw new FileNotFoundException("combat_ratings.json не встроен в сборку");
            var doc = JsonSerializer.Deserialize<Dto>(stream, JsonOpts) ?? throw new InvalidDataException("пустой combat_ratings.json");
            _gtMaxLevel = doc.GtMaxLevel > 0 ? doc.GtMaxLevel : 100;
            _critBase = doc.CritBase ?? [];
            _critRatio = doc.CritRatio ?? [];
            Available = _critBase.Length > 0 && _critRatio.Length > 0;
        }
        catch
        {
            _gtMaxLevel = 100;
            _critBase = [];
            _critRatio = [];
            Available = false;
        }
    }

    /// <summary>Крит ближнего боя (%) от базы класса и ловкости. 0, если данных нет/класс вне диапазона.</summary>
    public float MeleeCritPercent(byte cls, byte level, float agi)
    {
        if (!TryRatio(cls, level, out var ratio) || cls - 1 >= _critBase.Length)
            return 0f;
        return (_critBase[cls - 1] + agi * ratio) * 100f;
    }

    /// <summary>Уклонение (%) = базовое по классу + вклад ловкости (пропорционален крит-на-ловкость).</summary>
    public float DodgePercent(byte cls, byte level, float agi)
    {
        var baseDodge = cls < BaseDodge.Length ? BaseDodge[cls] : 0f;
        if (!TryRatio(cls, level, out var ratio))
            return baseDodge;
        var fromAgi = 100f * agi * ratio * (cls < AgiToCritToDodge.Length ? AgiToCritToDodge[cls] : 0f);
        return baseDodge + fromAgi;
    }

    private bool TryRatio(byte cls, byte level, out float ratio)
    {
        ratio = 0f;
        if (!Available || cls is < 1 or >= MaxClasses)
            return false;
        var lvl = Math.Clamp((int)level, 1, _gtMaxLevel);
        var idx = (cls - 1) * _gtMaxLevel + (lvl - 1);
        if (idx < 0 || idx >= _critRatio.Length)
            return false;
        ratio = _critRatio[idx];
        return true;
    }

    private sealed class Dto
    {
        public int GtMaxLevel { get; set; }
        public float[]? CritBase { get; set; }
        public float[]? CritRatio { get; set; }
    }
}
