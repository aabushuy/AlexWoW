namespace AlexWoW.WorldServer.Protocol;

/// <summary>
/// Языковые спеллы по расе. Знание языка в WoW = наличие соответствующего спелла,
/// иначе клиент локально блокирует /say («Вы не знаете этого языка»).
/// Первым идёт язык фракции (Общий/Орочий) — на нём по умолчанию говорит /say.
/// </summary>
public static class LanguageSpells
{
    private const int Common = 668;
    private const int Orcish = 669;

    private static readonly Dictionary<byte, int[]> ByRace = new()
    {
        [1] = [Common],            // Human
        [2] = [Orcish],            // Orc
        [3] = [Common, 672],       // Dwarf — Dwarven
        [4] = [Common, 671],       // Night Elf — Darnassian
        [5] = [Orcish, 17737],     // Undead — Gutterspeak
        [6] = [Orcish, 670],       // Tauren — Taurahe
        [7] = [Common, 7340],      // Gnome — Gnomish
        [8] = [Orcish, 7341],      // Troll — Troll (Zandali)
        [10] = [Orcish, 813],      // Blood Elf — Thalassian
        [11] = [Common],           // Draenei
    };

    public static IReadOnlyList<int> ForRace(byte race)
        => ByRace.TryGetValue(race, out var spells) ? spells : [Common];
}

/// <summary>
/// Языковые НАВЫКИ по расе (skill line id, не путать со spell id). Именно из них клиент
/// строит список языков для /say. Задаются в полях PLAYER_SKILL_INFO самого игрока.
/// </summary>
public static class LanguageSkills
{
    private const int CommonSkill = 98;
    private const int OrcishSkill = 109;

    private static readonly Dictionary<byte, int[]> ByRace = new()
    {
        [1] = [CommonSkill],         // Human — Common
        [2] = [OrcishSkill],         // Orc — Orcish
        [3] = [CommonSkill, 111],    // Dwarf — Dwarven
        [4] = [CommonSkill, 113],    // Night Elf — Darnassian
        [5] = [OrcishSkill, 673],    // Undead — Gutterspeak
        [6] = [OrcishSkill, 115],    // Tauren — Taurahe
        [7] = [CommonSkill, 313],    // Gnome — Gnomish
        [8] = [OrcishSkill, 315],    // Troll — Troll
        [10] = [OrcishSkill, 137],   // Blood Elf — Thalassian
        [11] = [CommonSkill, 759],   // Draenei — Draenei
    };

    public static IReadOnlyList<int> ForRace(byte race)
        => ByRace.TryGetValue(race, out var skills) ? skills : [CommonSkill];
}
