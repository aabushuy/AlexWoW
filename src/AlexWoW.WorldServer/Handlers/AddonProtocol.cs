using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Handlers.Dev;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Протокол «аддон ↔ сервер» (Devcommands #79). Транспорт — addon-сообщения поверх чат-опкодов 3.3.5a:
/// клиент шлёт <c>CMSG_MESSAGECHAT</c> с <c>language = LANG_ADDON (0xFFFFFFFF)</c> (тип WHISPER, тело
/// <c>"PREFIX\tBODY"</c>); сервер отвечает <c>SMSG_MESSAGECHAT</c> с тем же языком — клиент поднимает
/// событие <c>CHAT_MSG_ADDON</c> (в чат не печатается). Сверено с TrinityCore <c>Player::WhisperAddon</c>.
/// Сейчас единственная команда — <c>menu</c>: сервер отдаёт каталог dev-меню (<see cref="DevMenuCatalog"/>).
/// Не опкод-модуль (своих опкодов нет) — DI-сервис, инжектится в <see cref="ChatHandlers"/> (M7 #36).
/// Команды: <c>menu</c> — каталог dev-меню (<see cref="DevMenuCatalog"/>); <c>stats</c> — кадр вторичных
/// характеристик для окна-редактора (§178, <see cref="DevStatsCatalog"/>); <c>itemsearch</c> — поиск
/// предметов для окна «Добавить вещь» (переиспользует <see cref="IItemSearchRepository"/>); <c>qatasks</c>/
/// <c>qasubmit</c> — задачи на тестирование канбан-доски и сабмит результата (KB8, <see cref="IKanbanBoardRepository"/>).
/// </summary>
internal sealed class AddonProtocol(
    DevMenuCatalog devMenu, DevStatsCatalog devStats, IItemSearchRepository items, IKanbanBoardRepository kanban)
{
    public const uint LangAddon = 0xFFFFFFFF;
    private const byte ChatMsgWhisper = 0x07; // тип чата для addon-сообщения (как в TrinityCore)

    /// <summary>Разобрать addon-сообщение от клиента (<paramref name="prefix"/>/<paramref name="body"/>).</summary>
    public async Task HandleAsync(WorldSession session, string prefix, string body, CancellationToken ct)
    {
        if (prefix != DevMenuCatalog.Prefix)
            return;

        if (body == "menu")
        {
            await SendMenuAsync(session, ct);
            return;
        }

        if (body == "stats")
        {
            await SendStatsAsync(session, ct);
            return;
        }

        if (body.StartsWith("itemsearch", StringComparison.Ordinal))
        {
            await SendItemSearchAsync(session, body, ct);
            return;
        }

        if (body == "qatasks")
        {
            await SendQaTasksAsync(session, ct);
            return;
        }

        if (body.StartsWith("qasubmit", StringComparison.Ordinal))
        {
            await HandleQaSubmitAsync(session, body, ct);
            return;
        }

        session.Logger.LogDebug("ADDON '{User}': неизвестная команда '{Body}'", session.Account, body);
    }

    /// <summary>Отдать каталог dev-меню кадрами BEGIN … узлы … END. Не-админу — пустой каталог.</summary>
    private async Task SendMenuAsync(WorldSession session, CancellationToken ct)
    {
        await SendLineAsync(session, "BEGIN", ct);
        if (session.IsAdmin)
        {
            foreach (var line in await devMenu.BuildAsync(session, ct))
                await SendLineAsync(session, line, ct);
        }
        else
        {
            await SendLineAsync(session, "N|1|0|info|Доступно только администраторам", ct);
        }
        await SendLineAsync(session, "END", ct);
        session.Logger.LogDebug("ADDON '{User}': каталог dev-меню отправлен (admin={Admin})", session.Account, session.IsAdmin);
    }

    /// <summary>
    /// §178 (Доработка А) Отдать кадр вторичных характеристик для окна-редактора аддона: <c>SBEGIN</c> …
    /// <c>S|key|label|value</c> … <c>SEND</c>. Отдельные токены кадра (не BEGIN/END) — чтобы аддон не спутал
    /// его с каталогом меню (END меню вызывает перестроение дерева). Не-админу — ничего. Публичный: вызывается
    /// и по запросу <c>stats</c>, и пушем из <see cref="Dev.SetStatCommand"/> после записи (окно обновляется).
    /// </summary>
    public async Task SendStatsAsync(WorldSession session, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;
        await SendLineAsync(session, "SBEGIN", ct);
        foreach (var line in devStats.Build(session))
            await SendLineAsync(session, line, ct);
        await SendLineAsync(session, "SEND", ct);
    }

    /// <summary>
    /// Окно «Добавить вещь» (§182/§183): поиск по item_template и ответ кадром <c>IBEGIN</c> …
    /// <c>I|id|quality|itemlevel|reqlvl|name</c> … <c>IEND</c> (отдельные токены, чтобы не путать с меню/статами).
    /// Тело запроса: <c>itemsearch|class|sub|lvlMin|lvlMax|qualityMin|showAll|name</c> (поля опускаемы):
    /// class — один item-класс (0/2/4) либо пусто (= объём 0,2,4); sub — список подклассов через запятую
    /// (напр. «7,8» — мечи). Без <c>showAll=1</c> серверно фильтруем под класс+уровень персонажа. Сортировка —
    /// по уровню предмета. Не-админу — ничего.
    /// </summary>
    private async Task SendItemSearchAsync(WorldSession session, string body, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;

        var p = body.Split('|');
        var classField = Field(p, 1);
        var subField = Field(p, 2);
        var lvlMin = ParseUInt(Field(p, 3));
        var lvlMax = ParseUInt(Field(p, 4));
        var qualityMin = ParseUInt(Field(p, 5));
        var showAll = Field(p, 6) == "1";
        var name = Field(p, 7);

        // class пусто → объём фичи (экипировка 2,4 + расходники 0); иначе один указанный класс.
        uint[] classes = ParseUInt(classField) is { } c ? [c] : [0, 2, 4];
        var subClasses = subField
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => ParseUInt(s)).Where(v => v is not null).Select(v => v!.Value).ToArray();

        byte? playerClass = null;
        if (!showAll && session.Character is { } ch)
        {
            playerClass = ch.Class;                       // подходящее этому классу (AllowableClass-маска)
            lvlMax = lvlMax is { } lm ? Math.Min(lm, ch.Level) : ch.Level; // не выше уровня персонажа
        }

        var filter = new ItemSearchFilter
        {
            Classes = classes,
            SubClasses = subClasses,
            LevelMin = lvlMin,
            LevelMax = lvlMax,
            QualityMin = qualityMin,
            PlayerClass = playerClass,
            NameContains = string.IsNullOrWhiteSpace(name) ? null : name,
            OrderByItemLevel = true, // §183: сортировка по уровню предмета по умолчанию
            ExcludeTestItems = true, // §183: прятать QA/служебные предметы дампа
            Limit = 100,
        };

        IReadOnlyList<ItemTemplateData> results;
        try { results = await items.SearchAsync(filter, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "ADDON itemsearch: БД мира недоступна ({Msg})", ex.Message);
            results = [];
        }

        await SendLineAsync(session, "IBEGIN", ct);
        foreach (var it in results)
            await SendLineAsync(session, $"I|{it.Entry}|{it.Quality}|{it.ItemLevel}|{it.RequiredLevel}|{it.Name.Replace('|', ' ')}", ct);
        await SendLineAsync(session, "IEND", ct);
    }

    /// <summary>
    /// KB8: задачи на тестирование текущего персонажа — кадр <c>QBEGIN</c> … <c>Q|id|title|steps|expected</c> …
    /// <c>QEND</c>. Без админ-гейта (фильтр по tester_guid = персонаж). '|' в полях → '/', чтобы не ломать разбор
    /// (переводы строк допустимы — это одно сообщение). БД project недоступна/не настроена → пустой кадр.
    /// </summary>
    private async Task SendQaTasksAsync(WorldSession session, CancellationToken ct)
    {
        await SendLineAsync(session, "QBEGIN", ct);
        if (kanban.Configured && session.Character is { } ch)
        {
            IReadOnlyList<KanbanTesterTask> tasks;
            try { tasks = await kanban.GetTesterTasksAsync(ch.Guid, ct); }
            catch (Exception ex)
            {
                session.Logger.LogDebug(ex, "ADDON qatasks: БД project недоступна ({Msg})", ex.Message);
                tasks = [];
            }
            foreach (var t in tasks)
                await SendLineAsync(session, $"Q|{t.Id}|{Clean(t.Title)}|{Clean(t.TestSteps)}|{Clean(t.ExpectedResult)}", ct);
        }
        await SendLineAsync(session, "QEND", ct);
    }

    /// <summary>
    /// KB8: сабмит результата теста из игры — <c>qasubmit|ticketId|pass(0/1)|comment</c>. pass=1 → Done;
    /// pass=0 → комментарий обязателен, статус → «Ready to Implementation» + комментарий к тикету (автор = имя
    /// персонажа). Проверяем, что персонаж — назначенный тестировщик и тикет в статусе Testing. Ответ: одна
    /// строка <c>QDONE|id|status</c> или <c>QERR|id|msg</c> (аддон обновляет список).
    /// </summary>
    private async Task HandleQaSubmitAsync(WorldSession session, string body, CancellationToken ct)
    {
        if (!kanban.Configured || session.Character is not { } ch)
            return;
        var p = body.Split('|', 4);
        if (p.Length < 3 || !int.TryParse(p[1], out var ticketId))
        {
            await SendLineAsync(session, "QERR|0|Некорректный запрос", ct);
            return;
        }
        var pass = p[2] == "1";
        var comment = p.Length >= 4 ? p[3].Trim() : "";

        KanbanTicketRef? tref;
        try { tref = await kanban.GetTicketRefAsync(ticketId, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "ADDON qasubmit: БД project недоступна ({Msg})", ex.Message);
            await SendLineAsync(session, $"QERR|{ticketId}|БД недоступна", ct);
            return;
        }
        // Гейт: тикет существует, персонаж — его тестировщик, отмечена проверка на клиенте, статус Testing.
        if (tref is null || tref.TesterGuid != ch.Guid || !tref.ClientCheck || tref.Status != "Testing")
        {
            await SendLineAsync(session, $"QERR|{ticketId}|Задача недоступна для тестирования", ct);
            return;
        }

        string newStatus;
        if (pass)
        {
            newStatus = "Done";
            if (comment.Length > 0)
                await kanban.AddCommentAsync(ticketId, ch.Name, comment, ct);
        }
        else
        {
            if (comment.Length == 0)
            {
                await SendLineAsync(session, $"QERR|{ticketId}|Нужен комментарий", ct);
                return;
            }
            newStatus = "Ready to Implementation";
            await kanban.AddCommentAsync(ticketId, ch.Name, comment, ct);
        }
        await kanban.SetStatusAsync(ticketId, newStatus, ct);
        session.Logger.LogInformation("QA сабмит '{User}' тикет={Id} pass={Pass} → {Status}",
            session.Account, ticketId, pass, newStatus);
        await SendLineAsync(session, $"QDONE|{ticketId}|{newStatus}", ct);
    }

    private static string Clean(string s) => s.Replace('|', '/');

    private static string Field(string[] parts, int i) => i < parts.Length ? parts[i] : "";
    private static uint? ParseUInt(string s) => uint.TryParse(s, out var v) ? v : null;

    private static Task SendLineAsync(WorldSession session, string line, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgMessageChat, BuildAddonMessage((ulong)session.InWorldGuid, line), ct);

    /// <summary>
    /// SMSG_MESSAGECHAT (3.3.5a) как addon-сообщение: u8 type(WHISPER) + u32 lang(ADDON) + u64 sender +
    /// u32 flags + u64 target (whisper → else-ветка) + SizedCString message(<c>"PREFIX\tline"</c>) + u8 tag.
    /// </summary>
    private static byte[] BuildAddonMessage(ulong guid, string line)
    {
        var payload = Encoding.UTF8.GetBytes(DevMenuCatalog.Prefix + "\t" + line);
        return new ByteWriter(40 + payload.Length)
            .UInt8(ChatMsgWhisper)
            .UInt32(LangAddon)
            .UInt64(guid)                       // sender (сам игрок)
            .UInt32(0)                          // flags
            .UInt64(guid)                       // target (whisper)
            .UInt32((uint)(payload.Length + 1)) // SizedCString: длина с нулём
            .Bytes(payload).UInt8(0)
            .UInt8(0)                           // chat tag
            .ToArray();
    }
}
