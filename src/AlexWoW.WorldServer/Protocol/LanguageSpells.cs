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
