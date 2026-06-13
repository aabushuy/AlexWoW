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

    /// <summary>Раскладка слотов по типам (эталон mangos <c>runeSlotTypes</c>): 2 крови, 2 нечестия, 2 мороза.</summary>
    private static readonly RuneType[] SlotLayout =
        [RuneType.Blood, RuneType.Blood, RuneType.Unholy, RuneType.Unholy, RuneType.Frost, RuneType.Frost];

    /// <summary>Семейство спеллов Рыцаря смерти (SpellFamilyName, CMaNGOS SPELLFAMILY_DEATHKNIGHT).</summary>
    internal const uint DeathKnightFamily = 15;

    /// <summary>Стоимость рунной абилки: сколько рун каждого типа тратит + сколько силы рун даёт (в RP, не ×10).</summary>
    internal readonly record struct RuneCost(byte Blood, byte Frost, byte Unholy, int RunicPowerGain)
    {
        internal int Total => Blood + Frost + Unholy;
    }

    /// <summary>
    /// Стоимость рун по DK-абилке (RUNE.3), ключ — <b>SpellFamilyFlags</b> (одинаковы у всех РАНГОВ абилки,
    /// в отличие от spellId/RuneCostID, которые меняются по рангу — поэтому на ур.80 высокий ранг не находился
    /// по spellId). Источник стоимостей 3.3.5a — SpellRuneCost.dbc (через RuneCostID), которого нет ни в БД, ни
    /// DBC-файлом; значения сведены вручную. Матчится только семейство DK (15) + PowerType=POWER_RUNE.
    /// </summary>
    private static readonly Dictionary<ulong, RuneCost> RuneCosts = new()
    {
        [0x2]                  = new(0, 1, 0, 10),  // Icy Touch — 1 мороз
        [0x1]                  = new(0, 0, 1, 10),  // Plague Strike — 1 нечестие
        [0x400000]             = new(1, 0, 0, 10),  // Blood Strike — 1 кровь
        [0x1000000]            = new(1, 0, 0, 10),  // Heart Strike — 1 кровь
        [0x40000]              = new(1, 0, 0, 10),  // Blood Boil — 1 кровь
        [0x1000000000000]      = new(1, 0, 0, 10),  // Pestilence — 1 кровь
        [0x8000000]            = new(1, 0, 0, 0),   // Rune Tap — 1 кровь (силу рун не даёт)
        [0x10]                 = new(0, 1, 1, 15),  // Death Strike — 1 мороз + 1 нечестие
        [0x2000000000000]      = new(0, 1, 1, 15),  // Obliterate — 1 мороз + 1 нечестие
        [0x800000000000000]    = new(0, 1, 1, 15),  // Scourge Strike — 1 мороз + 1 нечестие
        [0x200000000]          = new(0, 1, 0, 10),  // Howling Blast — 1 мороз
        [211106232532996]      = new(0, 1, 0, 10),  // Chains of Ice — 1 мороз (составной флаг)
        [0x20]                 = new(1, 1, 1, 15),  // Death and Decay — по одной каждого типа
    };

    /// <summary>
    /// Стоимость рун абилки по её <see cref="SpellCatalog.SpellInfo"/> или null (не рунная DK-абилка). Матч по
    /// семейству DK + SpellFamilyFlags (рангонезависимо) и PowerType=POWER_RUNE (иначе прок/предметный спелл
    /// того же семейства мог бы ошибочно списать руны). RUNE.3.
    /// </summary>
    internal static RuneCost? GetCost(World.SpellCatalog.SpellInfo info)
        => info.FamilyName == DeathKnightFamily && info.FamilyFlags != 0
           && RuneCosts.TryGetValue(info.FamilyFlags, out var c) ? c : null;

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

    /// <summary>Спелл Кровавая хватка (Blood Tap) — конвертирует руну крови в death и активирует её. RUNE.5.</summary>
    internal const uint BloodTapSpellId = 45529;

    /// <summary>
    /// Blood Tap (RUNE.5): конвертирует одну руну крови в death-руну и делает её готовой. Предпочитает руну
    /// крови на кулдауне (активирует её), иначе берёт готовую. Шлёт <c>SMSG_CONVERT_RUNE</c> + снимок.
    /// No-op, если рун крови нет (не DK / уже все death).
    /// </summary>
    internal async Task BloodTapAsync(WorldSession session, CancellationToken ct)
    {
        var runes = session.Combat.Runes;
        // Руна крови на кулдауне в приоритете (Blood Tap её и активирует); иначе — любая руна крови.
        var slot = FindBloodSlot(runes, readyState: false);
        if (slot < 0)
            slot = FindBloodSlot(runes, readyState: true);
        if (slot < 0)
            return;

        await ConvertAsync(session, slot, RuneType.Death, makeReady: true, ct);
    }

    /// <summary>Индекс руны с текущим типом Blood и заданной готовностью (−1 — нет).</summary>
    private static int FindBloodSlot(RuneSlot[] runes, bool readyState)
    {
        for (var i = 0; i < runes.Length; i++)
            if (runes[i].CurrentType == RuneType.Blood && runes[i].Ready == readyState)
                return i;
        return -1;
    }

    /// <summary>
    /// Конвертирует слот руны в <paramref name="newType"/> (RUNE.5). <paramref name="makeReady"/> сбрасывает
    /// кулдаун (Blood Tap активирует руну). Шлёт <c>SMSG_CONVERT_RUNE</c> (перекраска) + снимок состояния.
    /// </summary>
    internal async Task ConvertAsync(WorldSession session, int slot, RuneType newType, bool makeReady, CancellationToken ct)
    {
        if (slot < 0 || slot >= session.Combat.Runes.Length)
            return;
        session.Combat.Runes[slot].CurrentType = newType;
        if (makeReady)
            session.Combat.Runes[slot].CooldownMs = 0;

        await session.SendAsync(WorldOpcode.SmsgConvertRune,
            CombatPackets.BuildConvertRune((byte)slot, (byte)newType), ct);
        await SendResyncAsync(session, ct);
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

        // ВАЖНО: только SMSG_RESYNC_RUNES — как TC/CMaNGOS. НЕ слать следом UNIT_FIELD_POWER(POWER_RUNE):
        // апдейт поля рун (в нём только число готовых, без кулдаунов) заставляет клиентский рунный фрейм
        // пересобраться из поля и сбросить анимацию кулдауна — руны переставали «гаснуть». Кулдаун/готовность
        // полностью несёт сам RESYNC (байт пройденного КД 0..255), длительность — из PLAYER_RUNE_REGEN.
        await session.SendAsync(WorldOpcode.SmsgResyncRunes,
            CombatPackets.BuildResyncRunes(snapshot, RuneCooldownMs), ct);
    }
}
