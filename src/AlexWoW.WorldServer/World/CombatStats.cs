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
}
