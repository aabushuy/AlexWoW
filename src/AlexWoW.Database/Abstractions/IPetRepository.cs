using AlexWoW.Database.Entities;

namespace AlexWoW.Database.Abstractions;

/// <summary>Persistence пета (PET.T5).</summary>
public interface IPetRepository
{
    Task<IReadOnlyList<CharacterPet>> LoadAllAsync(CancellationToken ct = default);
    Task<uint> InsertAsync(CharacterPet pet, CancellationToken ct = default);
    Task UpdateAsync(CharacterPet pet, CancellationToken ct = default);
    Task DeleteAsync(uint petId, CancellationToken ct = default);
    Task DeleteByOwnerAsync(uint ownerGuid, CancellationToken ct = default);
}
