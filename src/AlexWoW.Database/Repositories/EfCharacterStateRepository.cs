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

    public async Task RemoveLearnedSpellAsync(uint ownerGuid, uint spell, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterSpells.Where(x => x.OwnerGuid == ownerGuid && x.Spell == spell).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<(uint TalentId, byte Rank)>> GetTalentsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterTalents.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).Select(x => new { x.TalentId, x.Rank }).ToListAsync(ct);
        return rows.Select(x => (x.TalentId, x.Rank)).ToList();
    }

    public async Task SetTalentRankAsync(uint ownerGuid, uint talentId, byte rank, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.CharacterTalents.FindAsync([ownerGuid, talentId], ct);
        if (e is null)
            db.CharacterTalents.Add(new CharacterTalent { OwnerGuid = ownerGuid, TalentId = talentId, Rank = rank });
        else
            e.Rank = rank;
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearTalentsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterTalents.Where(x => x.OwnerGuid == ownerGuid).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<(ushort SkillId, ushort Value, ushort Max)>> GetSkillsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterSkills.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid)
            .Select(x => new { x.SkillId, x.Value, x.Max }).ToListAsync(ct);
        return rows.Select(x => (x.SkillId, x.Value, x.Max)).ToList();
    }

    public async Task UpsertSkillAsync(uint ownerGuid, ushort skillId, ushort value, ushort max, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.CharacterSkills.FindAsync([ownerGuid, skillId], ct);
        if (e is null)
            db.CharacterSkills.Add(new CharacterSkill { OwnerGuid = ownerGuid, SkillId = skillId, Value = value, Max = max });
        else
        {
            e.Value = value;
            e.Max = max;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(uint Spell, byte Form, uint RemainingMs)>> GetAurasAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterAuras.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid)
            .Select(x => new { x.Spell, x.Form, x.RemainingMs }).ToListAsync(ct);
        return rows.Select(x => (x.Spell, x.Form, x.RemainingMs)).ToList();
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

    public async Task SaveTimedAurasAsync(uint ownerGuid, IReadOnlyList<(uint Spell, uint RemainingMs)> auras, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Удаляем прежние временны́е (remaining>0); переключатели (remaining=0) не трогаем.
        await db.CharacterAuras.Where(x => x.OwnerGuid == ownerGuid && x.RemainingMs > 0).ExecuteDeleteAsync(ct);
        foreach (var (spell, rem) in auras)
            db.CharacterAuras.Add(new CharacterAura { OwnerGuid = ownerGuid, Spell = spell, Form = 0, RemainingMs = rem });
        await db.SaveChangesAsync(ct);
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
                OwnerId = ownerId,
                IsChar = isCharByte,
                DataType = dataType,
                UpdateTime = time,
                Data = data,
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
