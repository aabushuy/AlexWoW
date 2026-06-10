using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Использование гейм-объекта (CMSG_GAMEOBJ_USE, M11.4; DI-модуль M7 S7). Пока обрабатываем только ноды
/// сбора (рудные жилы / травы): гейт по навыку → выдача ресурса → skill-up → истощение ноды.
/// Прочие GO (двери/сундуки) — не наш кейс, игнорируем.
/// </summary>
internal sealed class GameObjectUseHandlers(InventoryGrantService inventoryGrant, SkillsService skills, ChatNotifier chat)
    : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgGameObjUse)]
    public async Task OnGameObjUse(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var guid = packet.Reader().UInt64();
        // entry закодирован в GUID гейм-объекта: (0xF110 << 48) | (entry << 24) | counter.
        var entry = (uint)((guid >> 24) & 0xFFFFFF);
        if (!Professions.Nodes.TryGetValue(entry, out var node))
            return;

        var sk = session.SkillBook.Get(node.SkillId);
        if (sk is null || sk.Value < node.ReqSkill)
        {
            await chat.SendSystemAsync(session,
                $"Требуется {Professions.SkillName(node.SkillId)} ({node.ReqSkill})", ct);
            return;
        }

        // Выдать ресурс (1..max).
        var count = node.MaxCount <= node.MinCount
            ? node.MinCount
            : (uint)Random.Shared.Next((int)node.MinCount, (int)node.MaxCount + 1);
        await inventoryGrant.TryGiveAsync(session, node.Item, count, ct);
        session.Logger.LogInformation("GATHER '{User}': node entry={Entry} → {Count}×{Item} ({Skill})",
            session.Account, entry, count, node.Item, Professions.SkillName(node.SkillId));

        // Прокачка навыка сбора.
        var chance = Professions.SkillUpChance(sk.Value, node.ReqSkill);
        if (chance > 0 && Random.Shared.Next(100) < chance)
            await skills.AddValueAsync(session, node.SkillId, 1, ct);

        // Истощить ноду: dev-ноду снять из реестра, иначе — DESTROY у клиента (разовый сбор).
        var slot = session.DevGos.FirstOrDefault(kv => kv.Value == guid).Key;
        if (slot is not null)
            await session.World.DespawnDevGoAsync(session, slot, ct);
        else
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
    }
}
