// Порт CMaNGOS-WoTLK: src/game/Entities/PetHandler.cpp (HandlePetAction/Rename/NameQuery/Abandon)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Entities/PetHandler.cpp. GPL-2.0.

using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Pet;

/// <summary>
/// Опкод-модуль пета (PET.T1 + T3): name query / action / set action / rename / abandon.
/// </summary>
internal sealed class PetHandlers(PetRegistry registry, PetPersistenceService persist) : IOpcodeHandlerModule
{
    /// <summary>ACT_* флаги (high byte action button data).</summary>
    private const byte ActPassive = 0x01;
    private const byte ActReaction = 0x06;
    private const byte ActCommand = 0x07;

    /// <summary>COMMAND_* значения для ACT_COMMAND.</summary>
    private const uint CommandStay = 0;
    private const uint CommandFollow = 1;
    private const uint CommandAttack = 2;
    private const uint CommandDismiss = 3;

    [WorldOpcodeHandler(WorldOpcode.CmsgPetNameQuery)]
    public async Task OnPetNameQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var petNumber = reader.UInt32();
        var petGuid = reader.UInt64();
        _ = petGuid;

        var pet = registry.GetByOwner((ulong)session.InWorldGuid);
        if (pet is null)
            return;

        var nameBytes = Encoding.UTF8.GetBytes(pet.Name);
        var w = new ByteWriter(4 + nameBytes.Length + 1 + 4 + 1)
            .UInt32(petNumber)
            .Bytes(nameBytes).UInt8(0)
            .UInt32((uint)(pet.SummonedAt - DateTime.UnixEpoch).TotalSeconds)
            .UInt8(0); // declined
        await session.SendAsync(WorldOpcode.SmsgPetNameQueryResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgPetAction)]
    public async Task OnPetAction(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var petGuid = reader.UInt64();
        var data = reader.UInt32();
        var targetGuid = reader.UInt64();
        _ = petGuid; _ = targetGuid;

        var pet = registry.GetByOwner((ulong)session.InWorldGuid);
        if (pet is null)
            return;

        var spellid = data & 0x00FFFFFF;
        var flag = (byte)((data >> 24) & 0xFF);

        switch (flag)
        {
            case ActCommand:
                switch (spellid)
                {
                    case CommandStay:
                        pet.CommandState = PetCommandState.Stay;
                        break;
                    case CommandFollow:
                        pet.CommandState = PetCommandState.Follow;
                        break;
                    case CommandAttack:
                        pet.CommandState = PetCommandState.Attack;
                        // Цель сохраняется отдельно (T2.1 — поле Pet.AttackTarget).
                        break;
                    case CommandDismiss:
                        await DismissAsync(pet, ct);
                        return;
                }
                await persist.UpdateAsync(pet, ct);
                await SendPetModeAsync(session, pet, ct);
                break;

            case ActReaction:
                if (spellid <= 2)
                {
                    pet.ReactState = (PetReactState)spellid;
                    await persist.UpdateAsync(pet, ct);
                    await SendPetModeAsync(session, pet, ct);
                }
                break;

            case ActPassive:
                // PetCast spell отложено в T3.1.
                break;
        }

        session.Logger.LogDebug("PET '{User}' action flag=0x{Flag:X2} spellid={Sp} pet={Id}",
            session.Account, flag, spellid, pet.Id);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgPetSetAction)]
    public Task OnPetSetAction(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        // T3.1: расстановка action bar. Сейчас принимаем и игнорируем (клиент кладёт на UI).
        _ = packet;
        _ = session;
        _ = ct;
        return Task.CompletedTask;
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgPetRename)]
    public async Task OnPetRename(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var petGuid = reader.UInt64();
        var newName = reader.CString();
        _ = petGuid;

        var pet = registry.GetByOwner((ulong)session.InWorldGuid);
        if (pet is null)
            return;
        if (newName.Length is < 2 or > 12)
        {
            await session.SendAsync(WorldOpcode.SmsgPetNameInvalid, [], ct);
            return;
        }

        pet.Name = newName;
        await persist.UpdateAsync(pet, ct);
        session.Logger.LogInformation("PET '{User}' renamed to '{Name}'", session.Account, newName);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgPetAbandon)]
    public async Task OnPetAbandon(WorldSession session, IncomingPacket _, CancellationToken ct)
    {
        var pet = registry.GetByOwner((ulong)session.InWorldGuid);
        if (pet is null)
            return;
        await DismissAsync(pet, ct);
        session.Logger.LogInformation("PET '{User}' abandoned pet entry {Entry}", session.Account, pet.Entry);
    }

    private async Task DismissAsync(World.Pet pet, CancellationToken ct)
    {
        await persist.DeleteAsync(pet, ct);
        registry.Remove(pet.OwnerGuid);
    }

    /// <summary>SMSG_PET_MODE: текущий react/command. CMaNGOS Pet::SendPetMode.</summary>
    private static Task SendPetModeAsync(WorldSession session, World.Pet pet, CancellationToken ct)
    {
        var bytes = new ByteWriter(12).UInt64(0).UInt32(((uint)pet.ReactState << 8) | (uint)pet.CommandState).ToArray();
        return session.SendAsync(WorldOpcode.SmsgPetMode, bytes, ct);
    }
}
