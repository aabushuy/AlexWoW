using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelCharacter = AlexWoW.Database.Models.Character;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий ядра персонажа (таблица characters + склонения имени, БД alexwow_auth).
/// SRP-часть DAL (#24): только жизненный цикл/атрибуты персонажа. Контекст из пула на операцию.
/// </summary>
public sealed class EfCharacterRepository(IDbContextFactory<AuthDbContext> factory) : ICharacterRepository
{
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
        // Зачистка связанных строк (FK-каскадов в схеме нет) — часть жизненного цикла персонажа.
        await db.CharacterItems.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.DeclinedNames.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.QuestStatuses.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.CharacterSpells.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        await db.AccountDataBlobs.Where(x => x.OwnerId == guid && x.IsChar == 1).ExecuteDeleteAsync(ct);
        await db.CharacterAuras.Where(x => x.OwnerGuid == guid).ExecuteDeleteAsync(ct);
        return true;
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
