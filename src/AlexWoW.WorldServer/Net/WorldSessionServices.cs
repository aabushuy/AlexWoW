using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Parameter object зависимостей <see cref="WorldSession"/> (M7 #35): собирается DI один раз и передаётся
/// фабрикой в каждую сессию — без service locator. Мосты сняты (S8 — игровые сервисы, S9 #43 — репозитории):
/// здесь только нужное самой сессии — роутер, handshake, персист аур и позиции, мир, опции, логгер.
/// </summary>
internal sealed class WorldSessionServices(
    IOptions<WorldServerOptions> options,
    ICharacterRepository characters,
    WorldState world,
    WorldPacketRouter router,
    AuthChallengeSender authChallenge,
    AuraPersistenceService auraPersistence,
    SpellTestCaptureService spellTestCapture,
    AlexWoW.WorldServer.Handlers.Group.GroupSyncService groupSync,
    ILogger<WorldSession> logger)
{
    public WorldServerOptions Options { get; } = options.Value;
    // Нужно самой сессии: сохранение позиции при логауте/разрыве (SavePositionIfInWorldAsync).
    public ICharacterRepository Characters { get; } = characters;
    public WorldState World { get; } = world;
    public WorldPacketRouter Router { get; } = router;
    public AuthChallengeSender AuthChallenge { get; } = authChallenge;
    // Нужно самой сессии: персист временных аур при выходе из мира (M10.5).
    public AuraPersistenceService AuraPersistence { get; } = auraPersistence;
    // Нужно самой сессии: закрыть осиротевшую сессию захвата проверки заклинаний при логауте (M12 Spell QA).
    public SpellTestCaptureService SpellTestCapture { get; } = spellTestCapture;
    // Нужно самой сессии: пометить offline для группы при логауте + online при логине. GROUP.T2.
    public AlexWoW.WorldServer.Handlers.Group.GroupSyncService GroupSync { get; } = groupSync;
    public ILogger<WorldSession> Logger { get; } = logger;
}
