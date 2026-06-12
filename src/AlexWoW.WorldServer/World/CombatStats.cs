namespace AlexWoW.WorldServer.World;

/// <summary>
/// Вторичные защитные статы (чистая математика, эталон CMaNGOS StatSystem.cpp), базовый случай без гира-
/// рейтингов/аур. Крит/уклонение от ловкости — через <see cref="AlexWoW.DataStores.CombatRatings"/>.
/// Парирование требует оружие, блок — щит (как у игрока в эталоне: CanParry/CanBlock включаются экипировкой).
/// </summary>
public static class CombatStats
{
    /// <summary>Броня = 2×ловкость + броня надетых предметов.</summary>
    public static uint Armor(uint agi, uint itemArmor) => agi * 2 + itemArmor;

    /// <summary>Классы, способные парировать (при оружии): воин/паладин/охотник/разбойник/DK/шаман.</summary>
    public static bool ClassCanParry(byte cls) => cls is 1 or 2 or 3 or 4 or 6 or 7;

    /// <summary>Классы, способные блокировать (при щите): воин/паладин/шаман.</summary>
    public static bool ClassCanBlock(byte cls) => cls is 1 or 2 or 7;

    /// <summary>Парирование (%): базовые 5% при способном классе и надетом оружии, иначе 0.</summary>
    public static float ParryPercent(byte cls, bool hasMeleeWeapon) =>
        ClassCanParry(cls) && hasMeleeWeapon ? 5f : 0f;

    /// <summary>Блок (%): базовые 5% при способном классе и надетом щите + бонус от аур («Блок щитом»),
    /// клампится в [0;100]. Без щита/способного класса — 0.</summary>
    public static float BlockPercent(byte cls, bool hasShield, float auraBonus = 0f) =>
        ClassCanBlock(cls) && hasShield ? Math.Clamp(5f + auraBonus, 0f, 100f) : 0f;

    /// <summary>Базовый навык защиты (skill 95): 5×уровень (400 на 80).</summary>
    public static ushort DefenseSkill(byte level) => (ushort)(level * 5);

    /// <summary>Исход входящего мили-удара (значения = VictimState в SMSG_ATTACKERSTATEUPDATE).</summary>
    public enum MeleeOutcome : byte { Hit = 1, Dodge = 2, Parry = 3 }

    /// <summary>Снижение физ. урона бронёй (эталон WotLK): armor/(armor + 467.5·lvl − 22167.5), кламп [0;0.75].</summary>
    public static double ArmorReduction(uint armor, byte attackerLevel)
    {
        var denom = armor + (467.5 * attackerLevel - 22167.5);
        if (denom <= 0)
            return 0;
        return Math.Clamp(armor / denom, 0.0, 0.75);
    }

    /// <summary>
    /// Разрешает входящий мили-удар по игроку: уклонение/парирование (полный обход), затем митигейшн —
    /// броня, блок (упрощённо −30% за блок-валью) и снижение от аур («Глухая оборона», dmgTakenPct&lt;0).
    /// <paramref name="avoidRoll"/>/<paramref name="blockRoll"/> ∈ [0;1). Чистая функция (тестируемо).
    /// </summary>
    public static (uint Damage, MeleeOutcome Outcome) ResolveIncomingMelee(
        uint raw, float dodgePct, float parryPct, float blockPct, uint armor, byte attackerLevel,
        int dmgTakenPct, double avoidRoll, double blockRoll)
    {
        var r = avoidRoll * 100.0;
        if (r < dodgePct)
            return (0, MeleeOutcome.Dodge);
        if (r < dodgePct + parryPct)
            return (0, MeleeOutcome.Parry);

        double dmg = raw;
        dmg *= 1.0 - ArmorReduction(armor, attackerLevel);
        if (blockRoll * 100.0 < blockPct)
            dmg *= 0.70; // упрощённый блок-валью: −30% за заблокированный удар
        dmg *= Math.Max(0.0, 1.0 + dmgTakenPct / 100.0); // «Глухая оборона»: dmgTakenPct<0 → снижение
        return ((uint)Math.Max(0, Math.Round(dmg)), MeleeOutcome.Hit);
    }
}
