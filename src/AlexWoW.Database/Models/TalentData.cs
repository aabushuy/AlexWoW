namespace AlexWoW.Database.Models;

/// <summary>
/// Запись таланта (talent ⨝ talent_tab из mangos) для валидации изучения (M9.7). rank — 0-индексный
/// (0..4). Dapper-DTO — record со <c>{ get; init; }</c> (грабля проекта: позиционные records не маппятся).
/// </summary>
public sealed record TalentData
{
    public uint TalentId { get; init; }
    public uint TalentTab { get; init; }
    public uint Tier { get; init; }          // ряд дерева (тир-гейт: нужно 5*Tier очков в этом tab)
    public uint ClassMask { get; init; }     // из talent_tab — какому классу принадлежит дерево
    public uint DependsOn { get; init; }     // prereq talentId (0 — нет)
    public uint DependsOnRank { get; init; } // требуемый ранг пререквизита (0-индекс)
    public uint Rank1 { get; init; }
    public uint Rank2 { get; init; }
    public uint Rank3 { get; init; }
    public uint Rank4 { get; init; }
    public uint Rank5 { get; init; }

    /// <summary>Спелл-id для ранга 0..4 (0 — нет такого ранга).</summary>
    public uint RankSpell(int rank) => rank switch
    {
        0 => Rank1,
        1 => Rank2,
        2 => Rank3,
        3 => Rank4,
        4 => Rank5,
        _ => 0u,
    };

    /// <summary>Максимальный ранг таланта (число непустых RankID).</summary>
    public int MaxRank
    {
        get
        {
            var n = 0;
            for (var r = 0; r < 5; r++) if (RankSpell(r) != 0) n = r + 1;
            return n;
        }
    }
}
