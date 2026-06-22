using AlexWoW.Database.Abstractions;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.tester [on|off]</c> — пометить/снять текущего персонажа как тестировщика QA-доски (KB10). Меню QA →
/// «Сделать тестировщиком» (<c>.tester on</c>) / «Убрать из тестировщиков» (<c>.tester off</c>). Флаг
/// <c>characters.IsTester</c> влияет на выборку задач (qatasks) и авто-подбор тестировщика ИИ (KB11).
/// После записи пушит флаг в аддон (<see cref="AddonProtocol.SendTesterStatusAsync"/>) — QA-панель
/// перерисовывает свою единственную кнопку без повторного запроса.
/// </summary>
internal sealed class TesterCommand(ICharacterRepository characters, AddonProtocol addon) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["tester"];
    public string Help => ".tester on|off";
    public int Order => 120;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Session.Character is not { } ch)
        {
            await ctx.ReplyAsync("Доступно только в мире", ct);
            return;
        }
        var on = ctx.ArgLower(0) != "off"; // ".tester"/".tester on" → вкл; ".tester off" → выкл
        await characters.SetTesterAsync(ch.Guid, on, ct);
        ch.IsTester = on; // синхронизируем сессию
        await ctx.ReplyAsync(on ? "Персонаж назначен тестировщиком QA" : "Персонаж снят с тестирования", ct);
        await addon.SendTesterStatusAsync(ctx.Session, ct); // обновить кнопку QA-панели аддона
    }
}
