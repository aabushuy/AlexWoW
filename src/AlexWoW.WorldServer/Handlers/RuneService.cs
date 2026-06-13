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

    /// <summary>Стоимость рунной абилки: сколько рун каждого типа тратит + сколько силы рун даёт (в RP, не ×10).</summary>
    internal readonly record struct RuneCost(byte Blood, byte Frost, byte Unholy, int RunicPowerGain)
    {
        internal int Total => Blood + Frost + Unholy;
    }

    /// <summary>
    /// Стоимость рун по DK-абилке (RUNE.3). Источник 3.3.5a — SpellRuneCost.dbc (через RuneCostID), которого
    /// нет ни в БД, ни DBC-файлом; значения сведены вручную по игровым данным для ключевых абилок. Спеллы с
    /// PowerType=POWER_RUNE вне таблицы рунами не гейтятся (кастуются свободно). Сила рун даётся при трате.
    /// </summary>
    private static readonly Dictionary<uint, RuneCost> RuneCosts = new()
    {
        [45477] = new(0, 1, 0, 10),  // Icy Touch — 1 мороз
        [45462] = new(0, 0, 1, 10),  // Plague Strike — 1 нечестие
        [45902] = new(1, 0, 0, 10),  // Blood Strike — 1 кровь
        [55050] = new(1, 0, 0, 10),  // Heart Strike — 1 кровь
        [48721] = new(1, 0, 0, 10),  // Blood Boil — 1 кровь
        [50842] = new(1, 0, 0, 10),  // Pestilence — 1 кровь
        [48982] = new(1, 0, 0, 0),   // Rune Tap — 1 кровь (силу рун не даёт)
        [49998] = new(0, 1, 1, 15),  // Death Strike — 1 мороз + 1 нечестие
        [49020] = new(0, 1, 1, 15),  // Obliterate — 1 мороз + 1 нечестие
        [55090] = new(0, 1, 1, 15),  // Scourge Strike — 1 мороз + 1 нечестие
        [49184] = new(0, 1, 0, 10),  // Howling Blast — 1 мороз
        [45524] = new(0, 1, 0, 10),  // Chains of Ice — 1 мороз
        [43265] = new(1, 1, 1, 15),  // Death and Decay — по одной каждого типа
    };

    /// <summary>Стоимость рун абилки или null (не рунная / нет в таблице). RUNE.3.</summary>
    internal static RuneCost? GetCost(uint spellId)
        => RuneCosts.TryGetValue(spellId, out var c) ? c : null;

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

    /// <summary>
    /// Хватает ли готовых рун на стоимость (RUNE.3). Death-руна (RUNE.5) — джокер под любой тип, поэтому
    /// сначала считаем готовые руны по типу, дефицит покрываем пулом готовых death-рун.
    /// </summary>
    internal static bool CanAfford(WorldSession session, RuneCost cost)
        => CanAfford(session.Combat.Runes, cost);

    /// <summary>Чистая проверка достаточности рун (для юнит-тестов и сессии). См. <see cref="CanAfford(WorldSession, RuneCost)"/>.</summary>
    internal static bool CanAfford(IReadOnlyList<RuneSlot> runes, RuneCost cost)
    {
        int blood = 0, frost = 0, unholy = 0, death = 0;
        foreach (var r in runes)
        {
            if (!r.Ready)
                continue;
            switch (r.CurrentType)
            {
                case RuneType.Blood: blood++; break;
                case RuneType.Frost: frost++; break;
                case RuneType.Unholy: unholy++; break;
                case RuneType.Death: death++; break;
            }
        }

        // Дефицит каждого типа добираем из общего пула death-рун.
        death -= Math.Max(0, cost.Blood - blood);
        death -= Math.Max(0, cost.Frost - frost);
        death -= Math.Max(0, cost.Unholy - unholy);
        return death >= 0;
    }

    /// <summary>
    /// Тратит руны на каст (RUNE.3): по каждому типу гасит готовые руны этого типа, дефицит — готовыми
    /// death-рунами (джокер). Гашение = постановка на кулдаун (<see cref="RuneCooldownMs"/>). Шлёт снимок.
    /// Предполагает пройденный <see cref="CanAfford"/>. Возвращает прибавку силы рун (RP, не ×10).
    /// </summary>
    internal async Task<int> SpendAsync(WorldSession session, RuneCost cost, CancellationToken ct)
    {
        SpendType(session, RuneType.Blood, cost.Blood);
        SpendType(session, RuneType.Frost, cost.Frost);
        SpendType(session, RuneType.Unholy, cost.Unholy);
        await SendResyncAsync(session, ct);
        return cost.RunicPowerGain;
    }

    /// <summary>Гасит <paramref name="count"/> готовых рун типа <paramref name="type"/>; нехватку — death-рунами.</summary>
    private static void SpendType(WorldSession session, RuneType type, int count)
    {
        var runes = session.Combat.Runes;
        // Сначала руны нужного типа.
        for (var i = 0; i < runes.Length && count > 0; i++)
            if (runes[i].Ready && runes[i].CurrentType == type)
            {
                runes[i].CooldownMs = RuneCooldownMs;
                count--;
            }
        // Затем death-руны (джокер).
        for (var i = 0; i < runes.Length && count > 0; i++)
            if (runes[i].Ready && runes[i].CurrentType == RuneType.Death)
            {
                runes[i].CooldownMs = RuneCooldownMs;
                count--;
            }
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
