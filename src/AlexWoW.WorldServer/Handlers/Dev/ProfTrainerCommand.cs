using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.proftrainer &lt;prof&gt;</c> — поставить тренера профессии у игрока (только 1, повтор заменяет);
/// <c>.proftrainer off</c> — снять. Реюз каркаса dev-сущностей (D1); резолв entry — по подписи существа
/// (в дампе нет skill_line_ability), берём самого полного — Grand Master. D2.
/// </summary>
internal sealed class ProfTrainerCommand(IWorldRepository worldDb) : IDevCommand
{
    /// <summary>Имена профессий → ключевое слово в SubName тренера.</summary>
    private static readonly Dictionary<string, string> ProfKeyword = new()
    {
        ["tailoring"] = "Tailor",
        ["blacksmithing"] = "Blacksmith",
        ["leatherworking"] = "Leatherwork",
        ["alchemy"] = "Alchem",
        ["enchanting"] = "Enchant",
        ["engineering"] = "Engineer",
        ["jewelcrafting"] = "Jewelcraft",
        ["mining"] = "Mining",
        ["herbalism"] = "Herbalism",
        ["skinning"] = "Skinning",
        ["cooking"] = "Cooking",
        ["firstaid"] = "First Aid",
        ["fishing"] = "Fishing",
    };

    public IReadOnlyList<string> Names { get; } = ["proftrainer"];
    public string Help => ".proftrainer <prof>|off";
    public int Order => 100;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        var arg = ctx.ArgLower(0);

        if (arg == "off")
        {
            var removed = await session.World.DespawnDevNpcAsync(session, World.DevSlot.ProfTrainer, ct);
            await ctx.ReplyAsync(removed ? "Тренер профессии снят" : "Тренер профессии не поставлен", ct);
            return;
        }
        if (!ProfKeyword.TryGetValue(arg, out var keyword))
        {
            await ctx.ReplyAsync("Профессия: tailoring/blacksmithing/leatherworking/alchemy/enchanting/engineering/jewelcrafting/mining/herbalism/skinning/cooking/firstaid/fishing (или off)", ct);
            return;
        }

        uint? entry;
        try { entry = await worldDb.GetProfessionTrainerEntryAsync(keyword, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "PROFTRAINER cmd: БД мира недоступна ({Msg})", ex.Message);
            await ctx.ReplyAsync("БД мира недоступна", ct);
            return;
        }
        if (entry is null)
        {
            await ctx.ReplyAsync($"Тренер профессии '{arg}' не найден в БД", ct);
            return;
        }

        var ok = await session.World.SummonDevNpcAsync(session, entry.Value, World.DevSlot.ProfTrainer, ct);
        await ctx.ReplyAsync(ok ? $"Тренер профессии '{arg}' поставлен" : "Не удалось поставить тренера", ct);
    }
}
