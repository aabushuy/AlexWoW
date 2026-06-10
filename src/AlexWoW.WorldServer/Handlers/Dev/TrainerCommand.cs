using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.trainer &lt;class&gt;</c> — поставить классового тренера у игрока (только 1, повтор заменяет);
/// <c>.trainer off</c> — снять. Entry резолвится data-driven (<c>GetClassTrainerEntryAsync</c>), спавн —
/// через каркас dev-сущностей (<see cref="World.CreatureDirector.SummonDevNpcAsync"/>). D1.
/// </summary>
internal sealed class TrainerCommand : IDevCommand
{
    /// <summary>Имена классов → id (WotLK).</summary>
    private static readonly Dictionary<string, byte> ClassByName = new()
    {
        ["warrior"] = 1, ["paladin"] = 2, ["hunter"] = 3, ["rogue"] = 4, ["priest"] = 5,
        ["dk"] = 6, ["deathknight"] = 6, ["shaman"] = 7, ["mage"] = 8, ["warlock"] = 9, ["druid"] = 11,
    };

    public IReadOnlyList<string> Names { get; } = ["trainer"];
    public string Help => ".trainer <class>|off";
    public int Order => 90;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        var arg = ctx.ArgLower(0);

        if (arg == "off")
        {
            var removed = await session.World.DespawnDevNpcAsync(session, World.DevSlot.Trainer, ct);
            await ctx.ReplyAsync(removed ? "Тренер снят" : "Тренер не поставлен", ct);
            return;
        }
        if (!ClassByName.TryGetValue(arg, out var classId))
        {
            await ctx.ReplyAsync("Класс: warrior/paladin/hunter/rogue/priest/dk/shaman/mage/warlock/druid (или off)", ct);
            return;
        }

        uint? entry;
        try { entry = await session.WorldDb.GetClassTrainerEntryAsync(classId, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("TRAINER cmd: БД мира недоступна ({Msg})", ex.Message);
            await ctx.ReplyAsync("БД мира недоступна", ct);
            return;
        }
        if (entry is null)
        {
            await ctx.ReplyAsync($"Тренер класса '{arg}' не найден в БД", ct);
            return;
        }

        var ok = await session.World.SummonDevNpcAsync(session, entry.Value, World.DevSlot.Trainer, ct);
        await ctx.ReplyAsync(ok ? $"Тренер класса '{arg}' поставлен" : "Не удалось поставить тренера", ct);
    }
}
