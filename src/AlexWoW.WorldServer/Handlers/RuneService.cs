using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Net.SessionState;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Руны Рыцаря смерти (Фаза 2 — RUNE.1): серверо-авторитетный ресурс DK. 6 рунных слотов с раскладкой
/// Blood,Blood,Unholy,Unholy,Frost,Frost (эталон mangos <c>runeSlotTypes</c>); каждая руна тратится абилкой
/// и восстанавливается по кулдауну (RUNE.2). Состояние клиенту — <c>SMSG_RESYNC_RUNES</c> (полный снимок) +
/// поле <c>UNIT_FIELD_POWER1+POWER_RUNE</c> (число готовых рун). Эталон — CMaNGOS
/// <c>Player::InitRunes/ResyncRunes</c>. Сила рун (runic power) — отдельный ресурс (<see cref="CombatResourcesService"/>).
/// </summary>
internal sealed class RuneService
{
    /// <summary>Класс Рыцаря смерти (CLASS_DEATH_KNIGHT). Руны есть только у него.</summary>
    internal const byte DeathKnightClass = 6;

    /// <summary>Число рун у DK (MAX_RUNES).</summary>
    internal const int MaxRunes = 6;

    /// <summary>Полный кулдаун восстановления одной руны (мс). Эталон mangos <c>RUNE_COOLDOWN = 2*5*IN_MS</c> = 10с.
    /// Ускорение от Unholy Presence / рейтинга скорости — позже (RUNE.2 TODO).</summary>
    internal const int RuneCooldownMs = 10000;

    /// <summary>Индекс ресурса рун среди UNIT power (POWER_RUNE = 5). Число готовых рун — в этом поле.</summary>
    private const byte PowerRune = 5;

    /// <summary>Раскладка слотов по типам (эталон mangos <c>runeSlotTypes</c>): 2 крови, 2 нечестия, 2 мороза.</summary>
    private static readonly RuneType[] SlotLayout =
        [RuneType.Blood, RuneType.Blood, RuneType.Unholy, RuneType.Unholy, RuneType.Frost, RuneType.Frost];

    /// <summary>True, если у класса есть руны (только DK).</summary>
    internal static bool HasRunes(byte charClass) => charClass == DeathKnightClass;

    /// <summary>
    /// Инициализирует 6 рунных слотов DK при входе в мир: тип по раскладке, текущий = базовому, все готовы
    /// (КД 0). Не-DK — очищает массив (рун нет). НЕ шлёт пакеты (вызывается до спавна; снимок уйдёт в
    /// <see cref="SendResyncAsync"/> после спавна).
    /// </summary>
    internal static void Initialize(WorldSession session, byte charClass)
    {
        if (!HasRunes(charClass))
        {
            session.Combat.Runes = [];
            return;
        }

        var runes = new RuneSlot[MaxRunes];
        for (var i = 0; i < MaxRunes; i++)
            runes[i] = new RuneSlot { BaseType = SlotLayout[i], CurrentType = SlotLayout[i], CooldownMs = 0 };
        session.Combat.Runes = runes;
    }

    /// <summary>Число готовых (не на кулдауне) рун.</summary>
    internal static int ReadyCount(WorldSession session)
    {
        var count = 0;
        foreach (var r in session.Combat.Runes)
            if (r.Ready)
                count++;
        return count;
    }

    /// <summary>
    /// Реген рун по кулдауну (RUNE.2, из <see cref="World.WorldTick.UpdateAsync"/>): уменьшает остаток КД
    /// каждой руны на прошедший интервал; руны восстанавливаются параллельно (эталон mangos
    /// <c>Regenerate(POWER_RUNE)</c>). Когда руна становится готовой — шлёт снимок (<see cref="SendResyncAsync"/>),
    /// обновляя число готовых рун. No-op у не-DK. Ускорение регена (Unholy Presence / рейтинг скорости) — TODO.
    /// </summary>
    internal async Task TickAsync(WorldSession session, long now, CancellationToken ct)
    {
        var runes = session.Combat.Runes;
        if (runes.Length == 0)
            return;

        var last = session.Combat.LastRuneTickMs;
        if (last == 0)
        {
            session.Combat.LastRuneTickMs = now; // первая инициализация базы времени
            return;
        }

        var diff = (int)(now - last);
        if (diff <= 0)
            return;
        session.Combat.LastRuneTickMs = now;

        var becameReady = false;
        for (var i = 0; i < runes.Length; i++)
        {
            if (runes[i].CooldownMs <= 0)
                continue;
            runes[i].CooldownMs -= diff;
            if (runes[i].CooldownMs <= 0)
            {
                runes[i].CooldownMs = 0;
                becameReady = true;
            }
        }

        if (becameReady)
            await SendResyncAsync(session, ct);
    }

    /// <summary>
    /// Шлёт клиенту полный снимок состояния рун: <c>SMSG_RESYNC_RUNES</c> + поле POWER_RUNE (число готовых).
    /// Вызывается после спавна (RUNE.1) и при любом изменении состояния рун (трата/реген — RUNE.2/RUNE.3).
    /// No-op у не-DK (рун нет).
    /// </summary>
    internal async Task SendResyncAsync(WorldSession session, CancellationToken ct)
    {
        var runes = session.Combat.Runes;
        if (runes.Length == 0)
            return;

        var snapshot = new (byte, int)[runes.Length];
        for (var i = 0; i < runes.Length; i++)
            snapshot[i] = ((byte)runes[i].CurrentType, runes[i].CooldownMs);

        await session.SendAsync(WorldOpcode.SmsgResyncRunes,
            CombatPackets.BuildResyncRunes(snapshot, RuneCooldownMs), ct);

        // Поле POWER_RUNE = число готовых рун (макрос UnitPower(unit,"RUNES") у клиента).
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid,
                m => m.SetUInt32(UpdateField.UnitPower1 + PowerRune, (uint)ReadyCount(session))), ct);
    }
}
