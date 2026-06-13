using AlexWoW.WorldServer.Net.SessionState;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.runes</c> — проверка системы рун DK (RUNE.1). Без аргумента показывает состояние 6 слотов;
/// <c>.runes ready</c> делает все руны готовыми; <c>.runes spend [blood|frost|unholy]</c> отправляет
/// одну руну на кулдаун (для проверки затемнения иконки — реген вернёт её в RUNE.2). Аналог <c>.combo</c>.
/// </summary>
internal sealed class RuneCommand(RuneService runes) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["runes"];
    public string Help => ".runes [ready|spend <тип>|death [slot]] — состояние/проверка рун DK (RUNE.1/5)";
    public int Order => 87; // рядом с .combo (86)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        if (session.Combat.Runes.Length == 0)
        {
            await ctx.ReplyAsync("Рун нет — нужен персонаж Рыцаря смерти.", ct);
            return;
        }

        var sub = ctx.ArgLower(0);
        switch (sub)
        {
            case "ready":
                for (var i = 0; i < session.Combat.Runes.Length; i++)
                    session.Combat.Runes[i].CooldownMs = 0;
                await runes.SendResyncAsync(session, ct);
                await ctx.ReplyAsync("Все руны готовы.", ct);
                return;

            case "spend":
                var type = ParseType(ctx.ArgLower(1));
                var slot = FindReadySlot(session, type);
                if (slot < 0)
                {
                    await ctx.ReplyAsync($"Нет готовой руны{(type is { } t ? $" типа {t}" : "")}.", ct);
                    return;
                }
                session.Combat.Runes[slot].CooldownMs = RuneService.RuneCooldownMs;
                await runes.SendResyncAsync(session, ct);
                await ctx.ReplyAsync($"Руна #{slot} ({session.Combat.Runes[slot].CurrentType}) на кулдауне.", ct);
                return;

            case "death":
                // RUNE.5: конвертировать слот (или первую руну крови) в death-руну.
                var dslot = int.TryParse(ctx.ArgLower(1), out var di) && di >= 0 && di < session.Combat.Runes.Length
                    ? di
                    : Array.FindIndex(session.Combat.Runes, r => r.CurrentType == RuneType.Blood);
                if (dslot < 0)
                {
                    await ctx.ReplyAsync("Нет руны для конвертации.", ct);
                    return;
                }
                await runes.ConvertAsync(session, dslot, RuneType.Death, makeReady: true, ct);
                await ctx.ReplyAsync($"Руна #{dslot} → death (готова).", ct);
                return;

            default:
                await ctx.ReplyAsync(Status(session), ct);
                return;
        }
    }

    /// <summary>Тип руны из аргумента (blood/frost/unholy/death). null — любой тип.</summary>
    private static RuneType? ParseType(string arg) => arg switch
    {
        "blood" or "кровь" => RuneType.Blood,
        "frost" or "мороз" => RuneType.Frost,
        "unholy" or "нечестие" => RuneType.Unholy,
        "death" or "смерть" => RuneType.Death,
        _ => null,
    };

    /// <summary>Индекс первой готовой руны заданного типа (или любой готовой, если тип не задан). −1 — нет.</summary>
    private static int FindReadySlot(Net.WorldSession session, RuneType? type)
    {
        for (var i = 0; i < session.Combat.Runes.Length; i++)
        {
            ref var r = ref session.Combat.Runes[i];
            if (r.Ready && (type is null || r.CurrentType == type))
                return i;
        }
        return -1;
    }

    private static string Status(Net.WorldSession session)
    {
        var ready = RuneService.ReadyCount(session);
        var parts = new string[session.Combat.Runes.Length];
        for (var i = 0; i < session.Combat.Runes.Length; i++)
        {
            var r = session.Combat.Runes[i];
            parts[i] = r.Ready ? r.CurrentType.ToString() : $"{r.CurrentType}({r.CooldownMs / 1000.0:0.0}с)";
        }
        return $"Руны [{ready}/{session.Combat.Runes.Length} готовы]: " + string.Join(", ", parts);
    }
}
