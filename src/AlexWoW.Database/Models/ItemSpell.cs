namespace AlexWoW.Database.Models;

/// <summary>Спелл на предмете (ItemSpells): id + триггер + заряды + кулдаун + категория.</summary>
public readonly record struct ItemSpell(uint Id, uint Trigger, int Charges, int Cooldown, uint Category, int CategoryCooldown);
