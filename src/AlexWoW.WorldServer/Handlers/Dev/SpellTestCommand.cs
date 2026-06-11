using AlexWoW.Database.Models;
using AlexWoW.WorldServer.World;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// <c>.spelltest start|stop|run [N]|status</c> — захват проверки заклинаний (M12 Spell QA). Ручной режим:
/// тестировщик стартует сессию, кастует по манекенам (<c>.dummy</c> / <c>.dummy heal</c>), останавливает.
/// Авто-режим (<c>run</c>): сервер сам прогоняет все известные боевые абилки класса по манекенам N раз.
/// Результаты — в БД (<see cref="SpellTestCaptureService"/>); анализ и тикет — на админ-странице Web.
/// </summary>
internal sealed class SpellTestCommand(SpellTestCaptureService capture, SpellTestHarnessService harness) : IDevCommand
{
    public IReadOnlyList<string> Names { get; } = ["spelltest"];
    public string Help => ".spelltest start [note]|stop|run [N]|status";
    public int Order => 85; // рядом с .dummy (80)
    public bool RequiresWorld => true;

    public async Task ExecuteAsync(DevCommandContext ctx, CancellationToken ct)
    {
        switch (ctx.ArgLower(0))
        {
            case "start":
                var note = ctx.Args.Count > 1 ? string.Join(' ', ctx.Args.Skip(1)) : null;
                var started = await capture.StartAsync(ctx.Session, SpellTestMode.Manual, note, ct);
                await ctx.ReplyAsync(started
                    ? "Захват тестов запущен. Кастуйте по манекенам, затем .spelltest stop"
                    : "Захват уже активен (или нет персонажа в мире)", ct);
                break;

            case "stop":
                var stopped = await capture.StopAsync(ctx.Session, ct);
                await ctx.ReplyAsync(stopped ? "Захват остановлен" : "Захват не был активен", ct);
                break;

            case "run":
                var n = int.TryParse(ctx.ArgLower(1), out var v) && v > 0 ? v : 5;
                await ctx.ReplyAsync($"Авто-прогон абилок класса ×{n}…", ct);
                var tested = await harness.RunAsync(ctx.Session, n, ct);
                await ctx.ReplyAsync(tested < 0
                    ? "Нужен персонаж в мире"
                    : $"Прогон завершён: протестировано спеллов {tested} (×{n})", ct);
                break;

            default:
                await ctx.ReplyAsync(capture.IsActive(ctx.Session)
                    ? $"Захват активен. Записей: {capture.RecordedCount(ctx.Session)}"
                    : "Захват не активен. .spelltest start — начать, .spelltest run — авто-прогон", ct);
                break;
        }
    }
}
