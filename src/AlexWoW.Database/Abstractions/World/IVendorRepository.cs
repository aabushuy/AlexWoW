using AlexWoW.Database.Models;

namespace AlexWoW.Database.Abstractions;

/// <summary>Ассортимент вендоров БД мира (npc_vendor[_template], дамп mangos). SRP-репозиторий (#25).</summary>
public interface IVendorRepository
{
    /// <summary>Ассортимент вендора по entry существа (только за золото, без условий).</summary>
    Task<IReadOnlyList<VendorItem>> GetVendorItemsAsync(uint entry, CancellationToken ct = default);
}
