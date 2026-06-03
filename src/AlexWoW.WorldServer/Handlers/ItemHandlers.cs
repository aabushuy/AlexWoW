using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Предметы (M6.1): ответ на CMSG_ITEM_QUERY_SINGLE из item_template (тултипы).</summary>
public static class ItemHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgItemQuerySingle)]
    public static async Task OnItemQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var entry = reader.UInt32();
        // далее u64 guid — для ответа не нужен.

        Database.Models.ItemTemplateData? t = null;
        try { t = await session.WorldDb.GetItemTemplateAsync(entry, ct); }
        catch (Exception ex) { session.Logger.LogDebug("ITEM_QUERY {Entry}: БД мира недоступна ({Msg})", entry, ex.Message); }

        if (t is null)
        {
            // «Нет данных»: entry | 0x80000000.
            await session.SendAsync(WorldOpcode.SmsgItemQuerySingleResponse,
                new ByteWriter(4).UInt32(entry | 0x80000000u).ToArray(), ct);
            return;
        }

        await session.SendAsync(WorldOpcode.SmsgItemQuerySingleResponse, ItemQuery.BuildResponse(t), ct);
    }
}
