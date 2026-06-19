using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database.Repositories;

/// <summary>EF-репозиторий персистенции пета (PET.T5).</summary>
public sealed class EfPetRepository(IDbContextFactory<AuthDbContext> factory) : IPetRepository
{
    public async Task<IReadOnlyList<CharacterPet>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterPets.AsNoTracking().ToListAsync(ct);
    }

    public async Task<uint> InsertAsync(CharacterPet pet, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.CharacterPets.Add(pet);
        await db.SaveChangesAsync(ct);
        return pet.Id;
    }

    public async Task UpdateAsync(CharacterPet pet, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.CharacterPets.Update(pet);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(uint petId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterPets.Where(p => p.Id == petId).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteByOwnerAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterPets.Where(p => p.OwnerGuid == ownerGuid).ExecuteDeleteAsync(ct);
    }
}
