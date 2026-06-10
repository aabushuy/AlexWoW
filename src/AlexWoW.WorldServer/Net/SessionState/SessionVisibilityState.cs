using AlexWoW.WorldServer.Protocol;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Видимость сессии: показанные клиенту NPC/гейм-объекты/игроки, троттлинг пересчёта и реестры
/// dev-сущностей. Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionVisibilityState
{
    /// <summary>
    /// Существа (NPC), показанные клиенту этой сессии (guid → авторитетная сущность). M5/M6.3.
    /// Потокобезопасный: пишет поток сессии (видимость), читает поток тика (рассылка боя/HP).
    /// </summary>
    internal System.Collections.Concurrent.ConcurrentDictionary<ulong, WorldCreature> VisibleNpcs { get; } = new();

    /// <summary>Гейм-объекты, показанные клиенту этой сессии (guid → спавн). M5.6b.</summary>
    internal Dictionary<ulong, GoSpawn> VisibleGos { get; } = [];

    /// <summary>
    /// Реестр dev-сущностей-существ этой сессии (слот → guid): класс-тренер/проф-тренер/вендор реагентов.
    /// Per-session (привязаны к месту/виду игрока): replace по слоту, снятие через <c>.devclean</c>, и
    /// «липкость» в видимости (см. <see cref="IsDevNpc"/>) — не сносятся при ходьбе. D1.
    /// </summary>
    internal Dictionary<string, ulong> DevNpcs { get; } = [];

    /// <summary>Является ли NPC dev-сущностью этой сессии — чтобы пересчёт видимости не слал DESTROY. D1.</summary>
    internal bool IsDevNpc(ulong guid) => DevNpcs.Count > 0 && DevNpcs.ContainsValue(guid);

    /// <summary>
    /// Реестр dev-гейм-объектов этой сессии (слот → guid): крафт-станки/почта (<c>.craft</c>). Спавнятся
    /// прямой посылкой и НЕ кладутся в <see cref="VisibleGos"/> — пересчёт видимости их не трогает (липкость
    /// «бесплатно»). Снимаются через <c>.craft off</c>/<c>.devclean</c>. D3.
    /// </summary>
    internal Dictionary<string, ulong> DevGos { get; } = [];

    /// <summary>
    /// Другие игроки, показанные клиенту этой сессии (set guid'ов). Доступ из нескольких потоков
    /// (сосед спавнит нас из своего потока) — потокобезопасный. Динамическая видимость игроков (M6).
    /// </summary>
    internal System.Collections.Concurrent.ConcurrentDictionary<ulong, byte> VisiblePlayers { get; } = new();

    /// <summary>Позиция последнего пересчёта видимости NPC (троттлинг по дистанции). M5.6.</summary>
    internal float LastVisX { get; set; }
    internal float LastVisY { get; set; }

    /// <summary>Сброс при выходе из мира — только очистка коллекций (LastVisX/Y переживают выход,
    /// как и раньше). Снятие dev-NPC с глобального реестра мира делает LeaveWorld ДО этого вызова.</summary>
    internal void Reset()
    {
        DevNpcs.Clear();
        DevGos.Clear();      // D3: dev-станки (клиент выгрузил мир)
        VisibleNpcs.Clear(); // клиент выгрузил мир — при повторном входе пересоздаём с нуля
        VisibleGos.Clear();
        VisiblePlayers.Clear();
    }
}
