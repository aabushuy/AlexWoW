namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.skill &lt;skillId&gt; &lt;value&gt; [max]</c> — выдать/выставить навык персонажу (тест M11.1).
/// Без max — потолок = value. Пример: <c>.skill 164 1 75</c> (кузнечное 1/75). Персист в character_skill.
/// </summary>
internal sealed class SkillCommand : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["skill"];
    public string Help => ".skill <skillId> <value> [max]";
    public int Order => 110;
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        if (ctx.Args.Count < 2
            || !ushort.TryParse(ctx.Args[0], out var skillId)
            || !ushort.TryParse(ctx.Args[1], out var value))
        {
            await ctx.ReplyAsync("Использование: .skill <skillId> <value> [max]", ct);
            return;
        }
        var max = ctx.Args.Count >= 3 && ushort.TryParse(ctx.Args[2], out var m) ? m : value;
        if (max < value)
            max = value;

        await ctx.Session.Skills.GrantAsync(ctx.Session, skillId, value, max, ct); // мост сессии (до S8)
        await ctx.ReplyAsync($"Навык {skillId} = {value}/{max}", ct);
    }
}
