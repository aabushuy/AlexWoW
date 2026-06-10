namespace AlexWoW.WorldServer.World;

/// <summary>Очки талантов (M9.6). Формула CMaNGOS <c>Player::CalculateTalentsPoints</c>:
/// max(0, level - base), base = 9 (обычные классы) или 55 (Рыцарь Смерти, класс 6). Квест-награды очков
/// игнорируем. Потраченные очки = сумма (rank+1) по изученным талантам.</summary>
public static class TalentMath
{
    private const byte DeathKnight = 6;

    /// <summary>Максимум очков талантов по классу и уровню.</summary>
    public static uint MaxPoints(byte classId, byte level)
    {
        var baseLevel = classId == DeathKnight ? 55 : 9;
        return level > baseLevel ? (uint)(level - baseLevel) : 0u;
    }
}
