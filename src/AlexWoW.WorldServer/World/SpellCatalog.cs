namespace AlexWoW.WorldServer.World;

/// <summary>
/// Каталог спеллов (M6.4) — ДАННЫЕ эффекта/каста, отделённые от оркестрации (<see cref="Handlers.SpellCaster"/>)
/// и сборки пакетов (<see cref="Protocol.SpellPackets"/>) по SRP. Сейчас захардкожен (ранг 1) — это
/// единственная точка будущей замены на дамп <c>spell_template</c> (mangos: школы/каст-таймы/эффекты/ранги/
/// SpellLevel): меняем только этот класс, потребители (caster/effects/toggles) не трогаем.
/// </summary>
public static class SpellCatalog
{
    /// <summary>
    /// МАСКИ школ магии (SpellSchoolMask, u8) — в SMSG_SPELLNONMELEEDAMAGELOG поле school читается как
    /// маска, не индекс (CMaNGOS шлёт <c>uint8(schoolMask)</c>): Fire=0x4, Frost=0x10 (а НЕ 2/4).
    /// </summary>
    public const byte SchoolFire = 0x04;
    public const byte SchoolFrost = 0x10;
    public const byte SchoolHoly = 0x02; // для хила (в heal-логе школа не передаётся — для полноты)

    /// <summary>
    /// Минимальный «справочник спеллов» (эффект): id → школа, величина эффекта (урон ИЛИ хил), время
    /// каста (мс), стоимость маны, кулдаун (мс; 0 — только GCD у клиента), хил-ли. Rank-1 как в WotLK. M6.4.
    /// </summary>
    public sealed record SpellInfo(byte School, int MinAmount, int MaxAmount, int CastMs, uint ManaCost,
        int CooldownMs, bool IsHeal = false);

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [133] = new(SchoolFire, 14, 22, 1500, ManaCost: 30, CooldownMs: 0),     // Fireball rank 1
        [116] = new(SchoolFrost, 14, 20, 1500, ManaCost: 25, CooldownMs: 0),    // Frostbolt rank 1
        [2136] = new(SchoolFire, 24, 32, 0, ManaCost: 40, CooldownMs: 8000),    // Fire Blast rank 1 (мгновенный, КД 8с)
        [2050] = new(SchoolHoly, 45, 56, 1500, ManaCost: 30, CooldownMs: 0, IsHeal: true), // Lesser Heal rank 1
    };

    /// <summary>Эффект спелла по id (true — известен).</summary>
    public static bool TryGet(uint spellId, out SpellInfo info) => Spells.TryGetValue(spellId, out info!);

    /// <summary>Спеллы, выдаваемые игроку в SMSG_INITIAL_SPELLS (для каста). M6.4.</summary>
    public static readonly int[] GrantedCombatSpells = { 133, 116, 2136, 2050 };

    // Группы эксклюзивных переключателей (M7 #21): один активен в группе.
    public const byte GroupShapeshift = 1;   // стойки воина / формы друида
    public const byte GroupPaladinAura = 2;  // ауры паладина
    public const byte GroupHunterAspect = 3; // аспекты охотника

    /// <summary>Переключатель: форма шейпшифта (0 — без формы) + группа эксклюзивности. M7 #21.</summary>
    public readonly record struct Toggle(byte Form, byte Group);

    /// <summary>
    /// Спеллы-переключатели (M6.12/M7 #21): мгновенный каст без маны/цели → перманентная аура (персист
    /// через релог). Форма (стойки воина → панель стоек). Эксклюзивны в своей группе. ⚠️ Только РАНГ 1 —
    /// высшие ранги имеют другие spell-id (нужен Spell.dbc; полноценно — в расширении системы аур).
    /// </summary>
    private static readonly Dictionary<uint, Toggle> Toggles = new()
    {
        // Стойки воина (форма → панель стоек): Battle=17, Defensive=18, Berserker=19.
        [2457] = new(17, GroupShapeshift),
        [71] = new(18, GroupShapeshift),
        [2458] = new(19, GroupShapeshift),
        // Ауры паладина (эксклюзивны).
        [465] = new(0, GroupPaladinAura),    // Devotion Aura
        [7294] = new(0, GroupPaladinAura),   // Retribution Aura
        [19746] = new(0, GroupPaladinAura),  // Concentration Aura
        [32223] = new(0, GroupPaladinAura),  // Crusader Aura
        [19876] = new(0, GroupPaladinAura),  // Shadow Resistance Aura
        [19888] = new(0, GroupPaladinAura),  // Frost Resistance Aura
        [19891] = new(0, GroupPaladinAura),  // Fire Resistance Aura
        // Аспекты охотника (эксклюзивны).
        [13165] = new(0, GroupHunterAspect), // Aspect of the Hawk
        [5118] = new(0, GroupHunterAspect),  // Aspect of the Cheetah
        [13163] = new(0, GroupHunterAspect), // Aspect of the Monkey
        [13159] = new(0, GroupHunterAspect), // Aspect of the Pack
        [20043] = new(0, GroupHunterAspect), // Aspect of the Wild
        [13161] = new(0, GroupHunterAspect), // Aspect of the Beast
        [34074] = new(0, GroupHunterAspect), // Aspect of the Viper
        [61846] = new(0, GroupHunterAspect), // Aspect of the Dragonhawk
    };

    /// <summary>Переключатель (стойка/аура/аспект) по id (true — это переключатель).</summary>
    public static bool TryGetToggle(uint spellId, out Toggle toggle) => Toggles.TryGetValue(spellId, out toggle);
}
