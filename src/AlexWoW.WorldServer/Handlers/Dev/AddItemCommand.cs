namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary><c>.additem ID [count]</c> / <c>.item ID [count]</c> — выдать предмет в инвентарь. M9.4.</summary>
internal sealed class AddItemCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["additem", "item"];
    public string Help => ".additem ID [count]";
    public int Order => 30;
    public bool RequiresWorld => false;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 1 || !uint.TryParse(ctx.Args[0], out var itemId))
        {
            await ctx.ReplyAsync("Использование: .additem ID [count]", ct);
            return;
        }
        var qty = ctx.Args.Count >= 2 && uint.TryParse(ctx.Args[1], out var q) ? q : 1u;
        var item = await InventoryGrant.TryGiveAsync(ctx.Session, itemId, qty, ct);
        await ctx.ReplyAsync(item is null ? "Нет места в сумке" : $"Выдан предмет {itemId} x{qty}", ct);
    }
}
