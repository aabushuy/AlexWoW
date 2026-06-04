using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Вход в мир (M4): player login + полная последовательность входа, логаут, time sync.</summary>
public static class WorldEntryHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgPlayerLogin)]
    public static async Task OnPlayerLogin(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();

        var character = await session.Characters.GetByGuidAsync(guid, ct);
        if (character is null || character.AccountId != session.AccountId)
        {
            session.Logger.LogWarning("PLAYER_LOGIN: персонаж guid={Guid} не найден/чужой для '{User}'", guid, session.Account);
            return;
        }

        session.InWorldGuid = character.Guid;
        session.PosX = character.X;
        session.PosY = character.Y;
        session.PosZ = character.Z;

        // Полная последовательность входа (порядок как в эталонных ядрах) — иначе клиент не отдаёт управление.
        var verify = new ByteWriter(20)
            .UInt32(character.Map)
            .Single(character.X).Single(character.Y).Single(character.Z)
            .Single(0f);
        await session.SendAsync(WorldOpcode.SmsgLoginVerifyWorld, verify.ToArray(), ct);

        await CharScreenHandlers.SendAccountDataTimesAsync(session, 0xEA, ct);

        await session.SendAsync(WorldOpcode.SmsgFeatureSystemStatus,
            new ByteWriter(2).UInt8(2).UInt8(0).ToArray(), ct);

        await SendInitialSpellsAsync(session, character.Race, ct);

        var tutorials = new ByteWriter(32);
        for (var i = 0; i < 8; i++)
            tutorials.UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgTutorialFlags, tutorials.ToArray(), ct);

        await SendLoginTimeSpeedAsync(session, ct);

        // M6.1: инвентарь — выдать стартовый набор голым персонажам, загрузить и создать item-объекты
        // у клиента ДО спавна игрока (self-update ссылается на guid'ы предметов в слотах).
        if (!await session.Characters.HasItemsAsync(character.Guid, ct))
            await StartingGear.GiveAsync(session, character.Guid, character.Race, character.Class, ct);
        session.Inventory.Clear();
        session.Inventory.AddRange(await session.Characters.GetItemsAsync(character.Guid, ct));
        session.Money = character.Money; // M6.2: деньги для торговли
        if (session.Inventory.Count > 0)
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildItemsCreate(session.Inventory, character.Guid), ct);

        var spawn = PlayerSpawn.BuildCreateObject(character,
            character.X, character.Y, character.Z, 0f, (uint)Environment.TickCount, isSelf: true, session.Inventory);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, spawn, ct);

        // Без time sync игрок не управляется.
        await session.SendAsync(WorldOpcode.SmsgTimeSyncReq, new ByteWriter(4).UInt32(0).ToArray(), ct);

        session.Logger.LogInformation("PLAYER_LOGIN '{Name}' (guid={Guid}) → мир: map={Map} ({X};{Y};{Z})",
            character.Name, guid, character.Map, character.X, character.Y, character.Z);

        // M5.5: проверка рельефа — высота земли в точке входа против сохранённой Z.
        var ground = session.Terrain.GetHeight(character.Map, character.X, character.Y);
        if (ground is { } g)
            session.Logger.LogInformation("Рельеф: земля в ({X};{Y}) = {Ground:F2} (Z персонажа {Z:F2}, дельта {Delta:F2})",
                character.X, character.Y, g, character.Z, character.Z - g);

        // M5.1/M5.6: показать существ и гейм-объекты из БД мира вокруг (диф-видимость).
        await SpawnHandlers.RefreshVisibleNpcsAsync(session, character.Map, character.X, character.Y, ct);
        await SpawnHandlers.RefreshVisibleGameObjectsAsync(session, character.Map, character.X, character.Y, ct);

        // M5.3: зарегистрировать в мире и обоюдно спавнить с соседними игроками.
        session.Character = character;
        var player = new World.WorldPlayer { Guid = character.Guid, Character = character, Session = session };
        session.Player = player;
        await session.World.EnterWorldAsync(player, ct);

        // Клиент теряет экипировку соседей, если их create приходит во время загрузочного экрана.
        // Досылаем create соседей повторно, когда загрузка точно завершена (две попытки).
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, ct);
                await session.World.ResendNearbyEquipmentToAsync(player, ct);
                await Task.Delay(2500, ct);
                await session.World.ResendNearbyEquipmentToAsync(player, ct);
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug("Повторная досылка соседей '{User}': {Msg}", session.Account, ex.Message);
            }
        }, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutRequest)]
    public static async Task OnLogoutRequest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.SavePositionIfInWorldAsync(ct);
        await session.LeaveWorldAsync(ct); // снять с реестра + DESTROY соседям

        await session.SendAsync(WorldOpcode.SmsgLogoutResponse,
            new ByteWriter(5).UInt32(0).UInt8(1).ToArray(), ct); // reason=0, instant=1
        await session.SendAsync(WorldOpcode.SmsgLogoutComplete, [], ct);
        session.Logger.LogInformation("LOGOUT '{User}' → возврат к выбору персонажа", session.Account);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutCancel)]
    public static Task OnLogoutCancel(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgLogoutCancelAck, [], ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgTimeSyncResp)]
    public static Task OnTimeSyncResp(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        session.Logger.LogInformation("CMSG_TIME_SYNC_RESP получен — поток в порядке");
        return Task.CompletedTask;
    }

    private static async Task SendInitialSpellsAsync(WorldSession session, byte race, CancellationToken ct)
    {
        var spells = LanguageSpells.ForRace(race);
        var w = new ByteWriter(8 + spells.Count * 6)
            .UInt8(0)
            .UInt16((ushort)spells.Count);
        foreach (var spell in spells)
            w.UInt32((uint)spell).UInt16(0); // 3.3.5: spellId — u32 + u16
        w.UInt16(0); // нет кулдаунов
        await session.SendAsync(WorldOpcode.SmsgInitialSpells, w.ToArray(), ct);
    }

    private static async Task SendLoginTimeSpeedAsync(WorldSession session, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var packed = (uint)(
            now.Minute
            | (now.Hour << 6)
            | ((int)now.DayOfWeek << 11)
            | ((now.Day - 1) << 14)
            | ((now.Month - 1) << 20)
            | ((now.Year - 2000) << 24));
        var w = new ByteWriter(12)
            .UInt32(packed)
            .Single(0.01666667f)
            .UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgLoginSetTimeSpeed, w.ToArray(), ct);
    }
}
