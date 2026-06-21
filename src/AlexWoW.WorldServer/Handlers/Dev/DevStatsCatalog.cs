using System.Globalization;
using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>Что пушить клиенту после записи стата (Ф2 dev-редактор). None — только серверный combat-кэш.</summary>
internal enum StatPush { None, Stats, Health, Mana, Rage, Energy, Runic, AttackPower }

/// <summary>
/// §178/Ф2 Каталог редактируемых характеристик для dev-панелей аддона («Основное» и «Характеристики»).
/// Машинный ключ, группа, рус. подпись, чтение/запись с клампом, и что пушить клиенту после записи.
/// Все правки — <b>session-only оверрайды</b> (живут до смены уровня/экипировки/релога), как и было у
/// вторичных статов. Первичные статы пишут <see cref="Net.SessionState.SessionCombatState.BaseStr"/>… и
/// пушатся через <c>PeriodicsService.SendStatsAsync</c>; ресурсы — через хелперы регена/ресурсов. DI-синглтон,
/// потребители — <see cref="AddonProtocol"/> (чтение старого кадра) и <see cref="SetStatCommand"/> (запись).
/// </summary>
internal sealed class DevStatsCatalog
{
    private sealed record Stat(
        string Key, string Group, string Label,
        Func<WorldSession, double> Get, Action<WorldSession, double> Set,
        double Min, double Max, StatPush Push);

    private static readonly Stat[] Defs =
    [
        // Основные (первичные) — session-оверрайд BaseX, пуш через SendStatsAsync (UNIT_FIELD_STAT + MaxHP/MaxMana).
        new("str", "Основные", "Сила", s => s.Combat.BaseStr, (s, v) => s.Combat.BaseStr = (uint)Math.Max(0, v), 0, 100_000, StatPush.Stats),
        new("agi", "Основные", "Ловкость", s => s.Combat.BaseAgi, (s, v) => s.Combat.BaseAgi = (uint)Math.Max(0, v), 0, 100_000, StatPush.Stats),
        new("sta", "Основные", "Выносливость", s => s.Combat.BaseSta, (s, v) => s.Combat.BaseSta = (uint)Math.Max(0, v), 0, 100_000, StatPush.Stats),
        new("int", "Основные", "Интеллект", s => s.Combat.BaseInt, (s, v) => s.Combat.BaseInt = (uint)Math.Max(0, v), 0, 100_000, StatPush.Stats),
        new("spi", "Основные", "Дух", s => s.Combat.BaseSpi, (s, v) => s.Combat.BaseSpi = (uint)Math.Max(0, v), 0, 100_000, StatPush.Stats),
        // Ресурсы — текущие значения (ярость/руническая хранятся ×10).
        new("hp", "Ресурсы", "Здоровье", s => s.Combat.Health, (s, v) => s.Combat.Health = (uint)Math.Clamp(v, 0, s.Combat.MaxHealth), 0, 10_000_000, StatPush.Health),
        new("mana", "Ресурсы", "Мана", s => s.Cast.Mana, (s, v) => s.Cast.Mana = (uint)Math.Clamp(v, 0, s.Cast.MaxMana), 0, 10_000_000, StatPush.Mana),
        new("rage", "Ресурсы", "Ярость", s => s.Combat.Rage / 10.0, (s, v) => s.Combat.Rage = (uint)(Math.Clamp(v, 0, 100) * 10), 0, 100, StatPush.Rage),
        new("energy", "Ресурсы", "Энергия", s => s.Combat.Energy, (s, v) => s.Combat.Energy = (uint)Math.Clamp(v, 0, 100), 0, 100, StatPush.Energy),
        new("runic", "Ресурсы", "Рунич. сила", s => s.Combat.RunicPower / 10.0, (s, v) => s.Combat.RunicPower = (uint)(Math.Clamp(v, 0, 100) * 10), 0, 100, StatPush.Runic),
        // Сила атаки (мили/дальний) — session-оверрайд BaseX AP, читается формулой автоатаки; пуш UNIT_FIELD_ATTACK_POWER.
        new("attackpower", "Ближний бой", "Сила атаки", s => s.Combat.BaseMeleeAttackPower, (s, v) => s.Combat.BaseMeleeAttackPower = (uint)Math.Max(0, v), 0, 1_000_000, StatPush.AttackPower),
        new("rangedap", "Дальний бой", "Сила атаки", s => s.Combat.BaseRangedAttackPower, (s, v) => s.Combat.BaseRangedAttackPower = (uint)Math.Max(0, v), 0, 1_000_000, StatPush.AttackPower),
        // Вторичные (существующие) — кэш combat-резолвера, перезаписывается RefreshMeleeAsync. Группы — для UI.
        new("critmelee", "Ближний бой", "Крит ближнего боя, %", s => s.Combat.MeleeCritPct, (s, v) => s.Combat.MeleeCritPct = (float)v, 0, 100, StatPush.None),
        new("wpnmin", "Ближний бой", "Урон оружия (мин)", s => s.Combat.WeaponMinDamage, (s, v) => s.Combat.WeaponMinDamage = (float)v, 0, 1_000_000, StatPush.None),
        new("wpnmax", "Ближний бой", "Урон оружия (макс)", s => s.Combat.WeaponMaxDamage, (s, v) => s.Combat.WeaponMaxDamage = (float)v, 0, 1_000_000, StatPush.None),
        new("wpnspeed", "Ближний бой", "Скорость оружия, мс", s => s.Combat.MainHandSpeedMs, (s, v) => s.Combat.MainHandSpeedMs = (uint)Math.Max(1, v), 1, 100_000, StatPush.None),
        new("critspell", "Магия", "Крит заклинаний, %", s => s.Cast.SpellCritChance, (s, v) => s.Cast.SpellCritChance = (int)v, 0, 100, StatPush.None),
        new("dodge", "Защита", "Уклонение, %", s => s.Combat.DodgePct, (s, v) => s.Combat.DodgePct = (float)v, 0, 100, StatPush.None),
        new("parry", "Защита", "Парирование, %", s => s.Combat.ParryPct, (s, v) => s.Combat.ParryPct = (float)v, 0, 100, StatPush.None),
        new("block", "Защита", "Блок, %", s => s.Combat.BlockPct, (s, v) => s.Combat.BlockPct = (float)v, 0, 100, StatPush.None),
        new("armor", "Защита", "Броня", s => s.Combat.ArmorValue, (s, v) => s.Combat.ArmorValue = (uint)Math.Max(0, v), 0, 1_000_000, StatPush.None),
    ];

    /// <summary>Строки старого кадра редактора: <c>S|key|label|value</c> (value — текущее, инвариант).</summary>
    public IReadOnlyList<string> Build(WorldSession session)
        => [.. Defs.Select(d => $"S|{d.Key}|{d.Label}|{Fmt(d.Get(session))}")];

    /// <summary>Применить значение по ключу (кламп в [Min;Max]) и вернуть, что пушить. false — неизвестный ключ/не число.</summary>
    public bool TrySet(WorldSession session, string key, string rawValue, out string label, out StatPush push)
    {
        label = ""; push = StatPush.None;
        var def = Array.Find(Defs, d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        if (def is null)
            return false;
        // Принимаем и точку (инвариант), и запятую (локаль клиента) как разделитель дробной части.
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && !double.TryParse(rawValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return false;
        def.Set(session, Math.Clamp(v, def.Min, def.Max));
        label = def.Label; push = def.Push;
        return true;
    }

    /// <summary>Целые — без дробной части, дробные — до 2 знаков; всегда инвариантная точка.</summary>
    private static string Fmt(double v)
        => v == Math.Floor(v)
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}
