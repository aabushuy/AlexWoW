namespace AlexWoW.WorldServer.Protocol;

/// <summary>Производные данные персонажа (модель, фракция, тип ресурса) по расе/классу.</summary>
public static class DisplayData
{
    // Нативные display id моделей (race → [male, female]). Значения из ChrRaces.dbc 3.3.5a.
    private static readonly Dictionary<byte, (uint Male, uint Female)> Models = new()
    {
        [1] = (49, 50),         // Human
        [2] = (51, 52),         // Orc
        [3] = (53, 54),         // Dwarf
        [4] = (55, 56),         // Night Elf
        [5] = (57, 58),         // Undead
        [6] = (59, 60),         // Tauren
        [7] = (1563, 1564),     // Gnome
        [8] = (1478, 1479),     // Troll
        [10] = (15476, 15475),  // Blood Elf
        [11] = (16125, 16126),  // Draenei
    };

    // Faction template по расе (UNIT_FIELD_FACTIONTEMPLATE), ChrRaces.dbc.
    private static readonly Dictionary<byte, uint> Factions = new()
    {
        [1] = 1,
        [2] = 2,
        [3] = 3,
        [4] = 4,
        [5] = 5,
        [6] = 6,
        [7] = 115,
        [8] = 116,
        [10] = 1610,
        [11] = 1629,
    };

    /// <summary>Display id модели по расе и полу (0 = male, 1 = female).</summary>
    public static uint ModelForRace(byte race, byte gender)
    {
        if (!Models.TryGetValue(race, out var pair))
            return 49; // human male по умолчанию
        return gender == 0 ? pair.Male : pair.Female;
    }

    // Display id облика друида (modelID_A из SpellShapeshiftForm.dbc — модели НОЧНОГО ЭЛЬФА; пол не различается,
    // скин — по цвету волос). Орда/таурен — отдельные модели (todo). Формы без модели (стойки воина) — не в таблице.
    private static readonly Dictionary<(byte Race, byte Form), uint> FormModels = new()
    {
        [(4, 1)] = 892,    // Night Elf — Cat Form
        [(4, 5)] = 2281,   // Bear Form
        [(4, 8)] = 2289,   // Dire Bear Form
        [(4, 3)] = 632,    // Travel Form
        [(4, 4)] = 2428,   // Aquatic Form
        [(4, 31)] = 15374, // Moonkin Form
        [(4, 2)] = 15500,  // Tree of Life
        [(4, 29)] = 20872, // Flight Form
        [(4, 27)] = 21244, // Swift Flight Form
    };

    /// <summary>§1 Display id модели облика друида по расе/форме (0 — нет модели → нативная модель расы).
    /// Пока только ночной эльф; таурен — todo. Пол феральные облики не различают.</summary>
    public static uint ModelForForm(byte race, byte form)
        => FormModels.TryGetValue((race, form), out var m) ? m : 0;

    public static uint FactionForRace(byte race)
        => Factions.TryGetValue(race, out var f) ? f : 1;

    /// <summary>Тип ресурса (powertype): 0 mana, 1 rage, 3 energy, 6 runic power.</summary>
    public static byte PowerTypeForClass(byte charClass) => charClass switch
    {
        1 => 1,  // Warrior — rage
        4 => 3,  // Rogue — energy
        6 => 6,  // Death Knight — runic power
        _ => 0,  // остальные — mana
    };

    /// <summary>
    /// Поля и значения текущего ресурса по типу (M9.2): индекс POWER/MAXPOWER (= 0x19/0x21 + powerType)
    /// и стартовые cur/max. Ярость 0/1000 (отображается /10), энергия 100/100, мана — переданный пул.
    /// Без этого у воина показывалась мана (мы писали в слот POWER1=мана для всех классов).
    /// </summary>
    public static (int Field, int MaxField, uint Cur, uint Max) PowerFor(byte powerType, uint mana)
    {
        var (cur, max) = powerType switch
        {
            0 => (mana, mana),    // мана
            1 => (0u, 1000u),     // ярость (клиент делит на 10 → 0/100)
            3 => (100u, 100u),    // энергия
            6 => (0u, 1000u),     // сила рун
            _ => (100u, 100u),
        };
        return (UpdateField.UnitPower1 + powerType, UpdateField.UnitMaxPower1 + powerType, cur, max);
    }

    /// <summary>
    /// Макс. мана для класса (M6.4). Только мана-классы (powertype 0); rage/energy/runic → 0
    /// (мана-система к ним не применяется — кастуют без расхода). Значение упрощённое (флэт), точные
    /// статы по классу/уровню/интеллекту — позже. 150 хватает на ~5 кастов rank-1 → видимый OOM.
    /// </summary>
    public static uint MaxManaForClass(byte charClass, byte level)
        => PowerTypeForClass(charClass) == 0 ? 150u : 0u;

    /// <summary>
    /// Макс. здоровье игрока по уровню (M6.7). Упрощённо (флэт по уровню) — точные статы по
    /// классу/выносливости позже. Достаточно, чтобы существо убивало игрока за несколько свингов.
    /// </summary>
    public static uint MaxHealthForLevel(byte level)
        => (uint)(80 + Math.Max((byte)1, level) * 20);
}
