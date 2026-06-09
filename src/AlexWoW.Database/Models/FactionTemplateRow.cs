namespace AlexWoW.Database.Models;

/// <summary>Строка faction_template (из FactionTemplate.dbc) — реакции фракций для авто-агро. M6.7.</summary>
public sealed record FactionTemplateRow
{
    public uint Id { get; init; }
    public uint Faction { get; init; }
    public uint OurMask { get; init; }
    public uint FriendMask { get; init; }
    public uint HostileMask { get; init; }
    public uint Enemy1 { get; init; }
    public uint Enemy2 { get; init; }
    public uint Enemy3 { get; init; }
    public uint Enemy4 { get; init; }
    public uint Friend1 { get; init; }
    public uint Friend2 { get; init; }
    public uint Friend3 { get; init; }
    public uint Friend4 { get; init; }
}
