namespace AlexWoW.Database.Models;

/// <summary>Урон оружия (ItemDamageType): min/max + школа.</summary>
public readonly record struct ItemDamage(float Min, float Max, uint School);
