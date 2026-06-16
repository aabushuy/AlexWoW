using System.Globalization;
using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// §178 (Доработка А) Каталог вторичных характеристик для окна-редактора в аддоне. Определяет редактируемые
/// статы сессии: машинный ключ, рус. подпись, чтение текущего значения и запись с клампом. Значения живут в
/// <see cref="Net.SessionState.SessionCastState"/>/<see cref="Net.SessionState.SessionCombatState"/>. ⚠️ Часть
/// (мили-крит/уклон/парри/блок/броня/оружие) перезаписывается <c>RefreshMeleeAsync</c> при смене
/// экипировки/уровня — это dev-инструмент для быстрой проверки, не постоянный стат-эдитор. Спелл-крит живёт
/// сессию. DI-синглтон, потребители — <see cref="AddonProtocol"/> (чтение) и <see cref="SetStatCommand"/> (запись).
/// </summary>
internal sealed class DevStatsCatalog
{
    private sealed record Stat(
        string Key, string Label,
        Func<WorldSession, double> Get, Action<WorldSession, double> Set,
        double Min, double Max);

    private static readonly Stat[] Defs =
    [
        new("critspell", "Крит заклинаний, %", s => s.Cast.SpellCritChance, (s, v) => s.Cast.SpellCritChance = (int)v, 0, 100),
        new("critmelee", "Крит ближнего боя, %", s => s.Combat.MeleeCritPct, (s, v) => s.Combat.MeleeCritPct = (float)v, 0, 100),
        new("dodge", "Уклонение, %", s => s.Combat.DodgePct, (s, v) => s.Combat.DodgePct = (float)v, 0, 100),
        new("parry", "Парирование, %", s => s.Combat.ParryPct, (s, v) => s.Combat.ParryPct = (float)v, 0, 100),
        new("block", "Блок, %", s => s.Combat.BlockPct, (s, v) => s.Combat.BlockPct = (float)v, 0, 100),
        new("armor", "Броня", s => s.Combat.ArmorValue, (s, v) => s.Combat.ArmorValue = (uint)Math.Max(0, v), 0, 1_000_000),
        new("wpnmin", "Урон оружия (мин)", s => s.Combat.WeaponMinDamage, (s, v) => s.Combat.WeaponMinDamage = (float)v, 0, 1_000_000),
        new("wpnmax", "Урон оружия (макс)", s => s.Combat.WeaponMaxDamage, (s, v) => s.Combat.WeaponMaxDamage = (float)v, 0, 1_000_000),
        new("wpnspeed", "Скорость оружия, мс", s => s.Combat.MainHandSpeedMs, (s, v) => s.Combat.MainHandSpeedMs = (uint)Math.Max(1, v), 1, 100_000),
    ];

    /// <summary>Строки кадра редактора: <c>S|key|label|value</c> (value — текущее значение, инвариант).</summary>
    public IReadOnlyList<string> Build(WorldSession session)
        => [.. Defs.Select(d => $"S|{d.Key}|{d.Label}|{Fmt(d.Get(session))}")];

    /// <summary>Применить значение по ключу (с клампом в [Min;Max]). false — неизвестный ключ или не число.</summary>
    public bool TrySet(WorldSession session, string key, string rawValue, out string label)
    {
        label = "";
        var def = Array.Find(Defs, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        if (def is null)
            return false;
        // Принимаем и точку (инвариант), и запятую (локаль клиента) как разделитель дробной части.
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && !double.TryParse(rawValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return false;
        def.Set(session, Math.Clamp(v, def.Min, def.Max));
        label = def.Label;
        return true;
    }

    /// <summary>Целые — без дробной части, дробные — до 2 знаков; всегда инвариантная точка.</summary>
    private static string Fmt(double v)
        => v == Math.Floor(v)
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}
