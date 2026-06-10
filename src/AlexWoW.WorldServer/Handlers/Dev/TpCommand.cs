using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.tp &lt;id&gt;</c> — телепорт в город из таблицы <c>dev_teleport</c> (alexwow_auth). Идентификатор
/// приходит из dev-меню аддона (после подтверждения в поп-апе). Та же карта — мгновенно, другая — через
/// загрузочный экран (<see cref="TeleportService"/>). Devcommands #79.
/// </summary>
internal sealed class TpCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["tp", "teleport"];
    public string Help => ".tp <id>";
    public int Order => 15;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var id))
        {
            await ctx.ReplyAsync("Использование: .tp <id города>", ct);
            return;
        }

        var loc = await ctx.Session.Teleports.GetByIdAsync(id, ct);
        if (loc is null)
        {
            await ctx.ReplyAsync($"Город #{id} не найден в dev_teleport.", ct);
            return;
        }

        await ctx.ReplyAsync($"Телепортация: {loc.Name}…", ct);
        await TeleportService.TeleportAsync(ctx.Session, loc.Map, loc.X, loc.Y, loc.Z, loc.O, ct);
    }
}
