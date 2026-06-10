namespace AlexWoW.WorldServer.World;

/// <summary>Один навык персонажа в книге умений (профессия и пр.). Мутируется при прокачке. M11.1.</summary>
public sealed class PlayerSkill
{
    public required ushort SkillId { get; init; }
    public ushort Value { get; set; }
    public ushort Max { get; set; }
}

/// <summary>
/// Книга навыков сессии (M11.1). Слоты <c>PLAYER_SKILL_INFO</c> заполняются: сначала языковые
/// (по расе, фиксированы), затем эти персист-навыки. Слот навыка = <see cref="LanguageSlots"/> + индекс
/// в порядке добавления — стабилен в рамках сессии, поэтому одиночный VALUES-апдейт поля адресуется верно.
/// </summary>
public sealed class PlayerSkillBook
{
    private readonly List<PlayerSkill> _skills = [];
    private readonly Dictionary<ushort, PlayerSkill> _byId = [];

    /// <summary>Число языковых слотов (0..L-1), занятых до профессий; задаётся при входе по расе.</summary>
    public int LanguageSlots { get; private set; }

    /// <summary>Навыки (профессии и пр.) в порядке слотов после языковых.</summary>
    public IReadOnlyList<PlayerSkill> Skills => _skills;

    public void Init(int languageSlots, IEnumerable<(ushort Id, ushort Value, ushort Max)> persisted)
    {
        LanguageSlots = languageSlots;
        _skills.Clear();
        _byId.Clear();
        foreach (var (id, value, max) in persisted)
            AddOrSet(id, value, max);
    }

    public void Clear()
    {
        _skills.Clear();
        _byId.Clear();
        LanguageSlots = 0;
    }

    public PlayerSkill? Get(ushort skillId) => _byId.GetValueOrDefault(skillId);

    public bool Has(ushort skillId) => _byId.ContainsKey(skillId);

    /// <summary>Индекс слота навыка в блоке PLAYER_SKILL_INFO (с учётом языковых), либо -1.</summary>
    public int SlotOf(ushort skillId)
    {
        var i = _skills.FindIndex(s => s.SkillId == skillId);
        return i < 0 ? -1 : LanguageSlots + i;
    }

    /// <summary>Добавляет навык (или обновляет значение/потолок существующего). Возвращает запись.</summary>
    public PlayerSkill AddOrSet(ushort skillId, ushort value, ushort max)
    {
        if (_byId.TryGetValue(skillId, out var sk))
        {
            sk.Value = value;
            sk.Max = max;
            return sk;
        }
        sk = new PlayerSkill { SkillId = skillId, Value = value, Max = max };
        _skills.Add(sk);
        _byId[skillId] = sk;
        return sk;
    }
}
