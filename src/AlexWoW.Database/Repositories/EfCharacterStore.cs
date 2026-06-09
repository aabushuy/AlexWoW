using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelCharacter = AlexWoW.Database.Models.Character;
using ModelInventoryItem = AlexWoW.Database.Models.InventoryItem;
using ModelQuestStatusRow = AlexWoW.Database.Models.QuestStatusRow;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF Core реализация фасада <see cref="ICharacterStore"/> (персонажи + инвентарь + квест-статусы +
/// состояние, БД alexwow_auth). Срез 4 рефактора DAL (#23): заменяет Dapper-<c>CharactersDatabase</c>.
/// Контекст берётся из пула на КАЖДУЮ операцию (singleton-safe при многопоточных сессиях, как прежняя
/// модель Dapper «подключение на запрос»). Схему alexwow_auth ведёт EF-миграция (применяет auth-сервер /
/// baseline на проде) — здесь EnsureSchema лишь пробует соединение для retry-цикла листенера.
/// </summary>
public sealed class EfCharacterStore(IDbContextFactory<AuthDbContext> factory) : ICharacterStore
{
    // ---- ICharacterRepository (ядро персонажа) ----

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        // Миграции запускает auth-сервер (единый мигратор); world лишь дожидается готовности БД.
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Database.CanConnectAsync(ct))
            throw new InvalidOperationException("БД alexwow_auth недоступна");
    }

    public async Task<IReadOnlyList<ModelCharacter>> GetByAccountAsync(uint accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Characters.AsNoTracking()
            .Where(x => x.AccountId == accountId).OrderBy(x => x.Guid).ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    public async Task<ModelCharacter?> GetByGuidAsync(uint guid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.Characters.AsNoTracking().FirstOrDefaultAsync(x => x.Guid == guid, ct);
        return e is null ? null : ToModel(e);
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Characters.AsNoTracking().AnyAsync(x => x.Name == name, ct);
    }

    public async Task<int> CountByAccountAsync(uint accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Characters.AsNoTracking().CountAsync(x => x.AccountId == accountId, ct);
    }

    public async Task<uint> CreateAsync(ModelCharacter character, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Только поля, которые задаёт создание персонажа; money/xp/action_bars/created_at — на дефолты БД
        // (money DEFAULT 1000000 и т.д.), как было в Dapper-INSERT.
        var e = new Character
        {
            AccountId = character.AccountId,
            Name = character.Name,
            Race = character.Race,
            Class = character.Class,
            Gender = character.Gender,
            Skin = character.Skin,
            Face = character.Face,
            HairStyle = character.HairStyle,
            HairColor = character.HairColor,
            FacialHair = character.FacialHair,
            Level = character.Level,
            Zone = character.Zone,
            Map = character.Map,
            PositionX = character.X,
            PositionY = character.Y,
            PositionZ = character.Z,
        };
        db.Characters.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Guid;
    }

    public async Task SavePositionAsync(uint guid, float x, float y, float z, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Characters.Where(c => c.Guid == guid).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PositionX, x)
            .SetProperty(c => c.PositionY, y)
            .SetProperty(c => c.PositionZ, z), ct);
    }

    public async Task SetDeclinedNamesAsync(uint ownerGuid, string[] names, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.DeclinedNames.FindAsync([ownerGuid], ct);
        if (e is null)
        {
            e = new DeclinedName { OwnerGuid = ownerGuid };
            db.DeclinedNames.Add(e);
        }
        e.N0 = names.ElementAtOrDefault(0) ?? "";
        e.N1 = names.ElementAtOrDefault(1) ?? "";
        e.N2 = names.ElementAtOrDefault(2) ?? "";
        e.N3 = names.ElementAtOrDefault(3) ?? "";
        e.N4 = names.ElementAtOrDefault(4) ?? "";
        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<uint>> GetGuidsWithDeclinedNamesAsync(
        IReadOnlyCollection<uint> guids, CancellationToken ct = default)
    {
        if (guids.Count == 0)
            return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.DeclinedNames.AsNoTracking()
            .Where(x => guids.Contains(x.OwnerGuid)
                && (x.N0 != "" || x.N1 != "" || x.N2 != "" || x.N3 != "" || x.N4 != ""))
            .Select(x => x.OwnerGuid).ToListAsync(ct);
        return rows.ToHashSet();
    }

    public async Task<string[]?> GetDeclinedNamesAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.DeclinedNames.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerGuid == ownerGuid, ct);
        return e is null ? null : [e.N0, e.N1, e.N2, e.N3, e.N4];
    }

    public async Task SetMoneyAsync(uint guid, uint money, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Characters.Where(c => c.Guid == guid)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Money, money), ct);
    }

    public async Task SetLevelXpAsync(uint guid, byte level, uint xp, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Characters.Where(c => c.Guid == guid).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Level, level)
            .SetProperty(c => c.Xp, xp), ct);
    }

    public async Task SetActionBarsAsync(uint guid, byte actionBars, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Characters.Where(c => c.Guid == guid)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ActionBars, actionBars), ct);
    }

    public async Task<bool> DeleteAsync(uint guid, uint accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var affected = await db.Characters.Where(c => c.Guid == guid && c.AccountId == accountId)
            .ExecuteDeleteAsync(ct);
        if (affected == 0)
            return false;
        // Зачистка связанных строк (как в Dapper-версии — FK-каскадов в схеме нет).
        await db.CharacterItems.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.DeclinedNames.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.QuestStatuses.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.CharacterSpells.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.AccountDataBlobs.Where(x => x.OwnerId == guid && x.IsChar == 1).ExecuteDeleteAsync(ct);
        await db.CharacterAuras.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        return true;
    }

    // ---- IInventoryRepository ----

    public async Task<bool> HasItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterItems.AsNoTracking().AnyAsync(x => x.OwnerGuid == ownerGuid, ct);
    }

    public async Task<IReadOnlyList<ModelInventoryItem>> GetItemsAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CharacterItems.AsNoTracking()
            .Where(x => x.OwnerGuid == ownerGuid).OrderBy(x => x.Bag).ThenBy(x => x.Slot).ToListAsync(ct);
        return rows.Select(x => new ModelInventoryItem
        {
            ItemGuid = x.ItemGuid, OwnerGuid = x.OwnerGuid, ItemEntry = x.ItemEntry,
            Bag = x.Bag, Slot = x.Slot, StackCount = x.StackCount,
        }).ToList();
    }

    public async Task<uint> AddItemAsync(uint ownerGuid, uint itemEntry, byte bag, byte slot,
        uint stackCount = 1, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = new CharacterItem
        {
            OwnerGuid = ownerGuid, ItemEntry = itemEntry, Bag = bag, Slot = slot, StackCount = stackCount,
        };
        db.CharacterItems.Add(e);
        await db.SaveChangesAsync(ct);
        return e.ItemGuid;
    }

    public async Task RemoveItemAsync(uint itemGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid).ExecuteDeleteAsync(ct);
    }

    public async Task MoveItemAsync(uint itemGuid, byte bag, byte slot, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Bag, bag)
            .SetProperty(x => x.Slot, slot), ct);
    }

    public async Task SetItemStackAsync(uint itemGuid, uint stackCount, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CharacterItems.Where(x => x.ItemGuid == itemGuid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StackCount, stackCount), ct);
    }

    // ---- IQuestRepository ----

    public async Task<IReadOnlyList<ModelQuestStatusRow>> GetQuestStatusesAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.QuestStatuses.AsNoTracking().Where(x => x.OwnerGuid == ownerGuid).ToListAsync(ct);
        return rows.Select(x => new ModelQuestStatusRow
        {
            QuestId = x.QuestId, Slot = x.Slot, Status = x.Status,
            Counter0 = x.Counter0, Counter1 = x.Counter1, Counter2 = x.Counter2, Counter3 = x.Counter3,
        }).ToList();
    }

    public async Task UpsertQuestStatusAsync(uint ownerGuid, uint questId, byte slot, byte status,
        ushort c0, ushort c1, ushort c2, ushort c3, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.QuestStatuses.FindAsync([ownerGuid, questId], ct);
        if (e is null)
        {
            e = new CharacterQuestStatus { OwnerGuid = ownerGuid, QuestId = questId };
            db.QuestStatuses.Add(e);
        }
        e.Slot = slot;
        e.Status = status;
        e.Counter0 = c0;
        e.Counter1 = c1;
        e.Counter2 = c2;
        e.Counter3 = c3;
        await db.SaveChangesAsync(ct);
    }

    // ---- ICharacterStateRepository ----

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

    private static ModelCharacter ToModel(Character e) => new()
    {
        Guid = e.Guid,
        AccountId = e.AccountId,
        Name = e.Name,
        Race = e.Race,
        Class = e.Class,
        Gender = e.Gender,
        Skin = e.Skin,
        Face = e.Face,
        HairStyle = e.HairStyle,
        HairColor = e.HairColor,
        FacialHair = e.FacialHair,
        Level = e.Level,
        Xp = e.Xp,
        Zone = e.Zone,
        Map = e.Map,
        X = e.PositionX,
        Y = e.PositionY,
        Z = e.PositionZ,
        Money = e.Money,
        ActionBars = e.ActionBars,
    };
}
