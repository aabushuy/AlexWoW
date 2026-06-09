using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий прочего состояния персонажа: изученные спеллы, ауры-переключатели, ярлыки панелей,
/// account-data блобы (БД alexwow_auth). SRP-часть DAL (#24). Контекст из пула на операцию.
/// </summary>
public sealed class EfCharacterStateRepository(IDbContextFactory<AuthDbContext> factory) : ICharacterStateRepository
{
    public async Task<IReadOnlyList<uint>> GetLearnedSpellsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterSpells.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).Select(x => x.Spell).ToListAsync(ct);
    }

    public async Task AddLearnedSpellAsync(uint ownerGuid, uint spell, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Идемпотентно (как INSERT IGNORE): добавляем, только если ещё не изучено.
        if (await db.CharacterSpells.AnyAsync(x => x.OwnerGuid == ownerGuid && x.Spell == spell, ct))
            return;
        db.CharacterSpells.Add(new CharacterSpell { OwnerGuid = ownerGuid, Spell = spell });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(uint Spell, byte Form)>> GetAurasAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterAuras.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).Select(x => new { x.Spell, x.Form }).ToListAsync(ct);
        return rows.Select(x => (x.Spell, x.Form)).ToList();
    }

    public async Task AddAuraAsync(uint ownerGuid, uint spell, byte form, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.CharacterAuras.FindAsync([ownerGuid, spell], ct);
        if (e is null)
            db.CharacterAuras.Add(new CharacterAura { OwnerGuid = ownerGuid, Spell = spell, Form = form });
        else
            e.Form = form;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAuraAsync(uint ownerGuid, uint spell, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterAuras.Where(x => x.OwnerGuid == ownerGuid && x.Spell == spell).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyDictionary<byte, uint>> GetActionButtonsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ActionButtons.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).Select(x => new { x.Button, x.PackedData }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Button, x => x.PackedData);
    }

    public async Task SetActionButtonAsync(uint ownerGuid, byte button, uint packed, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (packed == 0)
        {
            await db.ActionButtons.Where(x => x.OwnerGuid == ownerGuid && x.Button == button).ExecuteDeleteAsync(ct);
            return;
        }
        var e = await db.ActionButtons.FindAsync([ownerGuid, button], ct);
        if (e is null)
            db.ActionButtons.Add(new CharacterActionButton { OwnerGuid = ownerGuid, Button = button, PackedData = packed });
        else
            e.PackedData = packed;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(uint Time, byte[] Data)?> GetAccountDataAsync(uint ownerId, bool isChar, byte dataType, CancellationToken ct = default)
    {
        var isCharByte = (byte)(isChar ? 1 : 0);
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.AccountDataBlobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.IsChar == isCharByte && x.DataType == dataType, ct);
        return e is null ? null : (e.UpdateTime, e.Data ?? []);
    }

    public async Task<IReadOnlyDictionary<byte, uint>> GetAccountDataTimesAsync(uint ownerId, bool isChar, CancellationToken ct = default)
    {
        var isCharByte = (byte)(isChar ? 1 : 0);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AccountDataBlobs.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.IsChar == isCharByte)
            .Select(x => new { x.DataType, x.UpdateTime }).ToListAsync(ct);
        return rows.ToDictionary(x => x.DataType, x => x.UpdateTime);
    }

    public async Task UpsertAccountDataAsync(uint ownerId, bool isChar, byte dataType, uint time, byte[] data, CancellationToken ct = default)
    {
        var isCharByte = (byte)(isChar ? 1 : 0);
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.AccountDataBlobs.FindAsync([ownerId, isCharByte, dataType], ct);
        if (e is null)
        {
            db.AccountDataBlobs.Add(new AccountDataBlob
            {
                OwnerId = ownerId, IsChar = isCharByte, DataType = dataType, UpdateTime = time, Data = data,
            });
        }
        else
        {
            e.UpdateTime = time;
            e.Data = data;
        }
        await db.SaveChangesAsync(ct);
    }
}
