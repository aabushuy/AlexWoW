using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Панели действий (M7 #17): персист ярлыков (CMSG_SET_ACTION_BUTTON → character_action,
/// SMSG_ACTION_BUTTONS при входе) и видимости доп. панелей (CMSG_SET_ACTIONBAR_TOGGLES →
/// PLAYER_FIELD_BYTES[2] + персист). Это НЕ account-data: содержимое кнопок и набор панелей —
/// отдельные опкоды (сверено с CMaNGOS Player::SendInitialActionButtons/HandleSetActionButtonOpcode).
/// </summary>
public static class ActionBarHandlers
{
    /// <summary>Кол-во кнопок панелей (3.3.5: MAX_ACTION_BUTTONS).</summary>
    private const int MaxActionButtons = 144;

    [WorldOpcodeHandler(WorldOpcode.CmsgSetActionButton)]
    public static async Task OnSetActionButton(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
            return;
        var r = packet.Reader();
        var button = r.UInt8();
        // Клиент шлёт button(u8) + packedData(u32) = action(24)|type(8). packed=0 → снять ярлык.
        var packed = r.UInt32();
        try { await session.Characters.SetActionButtonAsync(session.InWorldGuid, button, packed, ct); }
        catch (Exception ex) { session.Logger.LogDebug("SET_ACTION_BUTTON {Btn}: {Msg}", button, ex.Message); }
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgSetActionbarToggles)]
    public static async Task OnSetActionbarToggles(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || session.Character is null)
            return;
        var actionBars = packet.Reader().UInt8();
        session.Character.ActionBars = actionBars;

        // Отразить в поле игрока (PLAYER_FIELD_BYTES байт 2) + персист — иначе доп. панели слетают при входе.
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetBytes(UpdateField.PlayerFieldBytes, 0, 0, actionBars, 0)), ct);
        try { await session.Characters.SetActionBarsAsync(session.InWorldGuid, actionBars, ct); }
        catch (Exception ex) { session.Logger.LogDebug("SET_ACTIONBAR_TOGGLES: {Msg}", ex.Message); }
    }

    /// <summary>
    /// SMSG_ACTION_BUTTONS при входе (M7 #17): behavior=INITIAL(1) + 144×u32 packed (0 — пусто).
    /// Восстанавливает ярлыки панелей из character_action. Зовётся из OnPlayerLogin.
    /// </summary>
    internal static async Task SendInitialActionButtonsAsync(WorldSession session, CancellationToken ct)
    {
        IReadOnlyDictionary<byte, uint> buttons;
        try { buttons = await session.Characters.GetActionButtonsAsync(session.InWorldGuid, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("ACTION_BUTTONS load '{User}': {Msg}", session.Account, ex.Message);
            buttons = new Dictionary<byte, uint>();
        }

        var w = new ByteWriter(1 + MaxActionButtons * 4).UInt8(1); // 1 = INITIAL (как CMaNGOS)
        for (var button = 0; button < MaxActionButtons; button++)
            w.UInt32(buttons.TryGetValue((byte)button, out var packed) ? packed : 0u);
        await session.SendAsync(WorldOpcode.SmsgActionButtons, w.ToArray(), ct);
    }
}
