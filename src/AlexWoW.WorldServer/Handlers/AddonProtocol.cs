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
    DevMenuCatalog devMenu, DevStatsCatalog devStats, IItemSearchRepository items,
    IKanbanBoardRepository kanban, ISpellDetailRepository spellDetails, ITeleportRepository teleports)
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

        if (body.StartsWith("itembyid|", StringComparison.Ordinal))
        {
            await SendItemByIdAsync(session, body, ct);
            return;
        }

        if (body.StartsWith("itemsearch", StringComparison.Ordinal))
        {
            await SendItemSearchAsync(session, body, ct);
            return;
        }

        if (body == "qatasks" || body.StartsWith("qatasks|", StringComparison.Ordinal))
        {
            await SendQaTasksAsync(session, body, ct);
            return;
        }

        if (body.StartsWith("qasubmit", StringComparison.Ordinal))
        {
            await HandleQaSubmitAsync(session, body, ct);
            return;
        }

        if (body.StartsWith("qaspell|", StringComparison.Ordinal))
        {
            await SendSpellDetailAsync(session, body, ct);
            return;
        }

        if (body == "devteleports")
        {
            await SendTeleportsAsync(session, ct);
            return;
        }

        if (body == "auras")
        {
            await SendAurasAsync(session, ct);
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
    /// KB8/KB14: задачи на тестирование текущего персонажа — кадр <c>QBEGIN</c> … <c>Q|id|title|steps|expected</c> …
    /// <c>QEND</c>. Тело клиента: <c>qatasks</c> или <c>qatasks|&lt;kind&gt;</c> (general/abilities/talents/professions —
    /// разделение по вкладкам аддона; неизвестное значение → general). Без админ-гейта (фильтр по tester_guid).
    /// '|' в полях → '/', чтобы не ломать разбор. БД project недоступна/не настроена → пустой кадр.
    /// </summary>
    private async Task SendQaTasksAsync(WorldSession session, string body, CancellationToken ct)
    {
        var kind = ParseListKind(body);
        await SendLineAsync(session, "QBEGIN", ct);
        if (kanban.Configured && session.Character is { } ch)
        {
            IReadOnlyList<KanbanTesterTask> tasks;
            try { tasks = await kanban.GetTesterTasksAsync(ch.Guid, kind, ct); }
            catch (Exception ex)
            {
                session.Logger.LogDebug(ex, "ADDON qatasks: БД project недоступна ({Msg})", ex.Message);
                tasks = [];
            }
            foreach (var t in tasks)
                // Title с префиксом #id — клиентский Lua показывает только заголовок (второе поле),
                // номер тикета в отдельном поле не использует. Префикс помогает тестеру писать «по #209…» в чат.
                // SpellId/School заполнены только для regression-вкладок; для general оба = пусто (клиент → nil).
                await SendLineAsync(session,
                    $"Q|{t.Id}|{Clean($"#{t.Id} · {t.Title}")}|{Clean(t.TestSteps)}|{Clean(t.ExpectedResult)}|{t.SpellId?.ToString() ?? ""}|{t.SchoolMask?.ToString() ?? ""}",
                    ct);
        }
        await SendLineAsync(session, "QEND", ct);
    }

    /// <summary>
    /// KB14: детали спелла для блока детализации аддона (вкладки Абилки/Таланты/Профессии). Тело:
    /// <c>qaspell|&lt;spellId&gt;</c>. Кадр <c>DBEGIN|id</c> … <c>DD|label|value</c> (школа/семейство/уровень/
    /// ресурс/эффекты) … <c>DR|itemId|count|name</c> (реагенты рецепта) … <c>DEND</c>. Отдельные токены —
    /// чтобы клиент не путал с каталогом меню/статами. Само описание спелла берёт клиент (тултип spell.dbc).
    /// </summary>
    private async Task SendSpellDetailAsync(WorldSession session, string body, CancellationToken ct)
    {
        var bar = body.IndexOf('|');
        if (bar < 0 || !uint.TryParse(body.AsSpan(bar + 1), out var spellId))
            return;

        SpellDetail? d;
        try { d = await spellDetails.GetAsync(spellId, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "ADDON qaspell: БД мира недоступна ({Msg})", ex.Message);
            d = null;
        }

        await SendLineAsync(session, $"DBEGIN|{spellId}", ct);
        if (d is not null)
        {
            await SendLineAsync(session, $"DD|Школа|{Clean(d.School)}", ct);
            await SendLineAsync(session, $"DD|Семейство|{Clean(d.Family)}", ct);
            if (d.Level > 0)
                await SendLineAsync(session, $"DD|Уровень|{d.Level}", ct);
            if (d.ManaCost > 0)
                await SendLineAsync(session, $"DD|Ресурс|{d.ManaCost} {Clean(d.PowerType)}", ct);
            foreach (var eff in d.Effects)
                await SendLineAsync(session, $"DD|Эффект|{Clean(eff)}", ct);
            foreach (var r in d.Reagents)
                await SendLineAsync(session, $"DR|{r.ItemId}|{r.Count}|{Clean(r.Name)}", ct);
        }
        await SendLineAsync(session, "DEND", ct);
    }

    private static KanbanTesterListKind ParseListKind(string body)
    {
        var bar = body.IndexOf('|');
        if (bar < 0) return KanbanTesterListKind.General;
        return body.AsSpan(bar + 1) switch
        {
            "abilities" => KanbanTesterListKind.Abilities,
            "talents" => KanbanTesterListKind.Talents,
            "professions" => KanbanTesterListKind.Professions,
            _ => KanbanTesterListKind.General,
        };
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

    /// <summary>
    /// Поиск предмета по точному id для панели «Реагенты» (режим «по id»): тело <c>itembyid|&lt;id&gt;</c>,
    /// ответ тем же кадром, что и поиск по имени (<c>IBEGIN</c>/<c>I|…</c>/<c>IEND</c>) — клиент переиспользует
    /// разбор рынка. Admin-гейт.
    /// </summary>
    private async Task SendItemByIdAsync(WorldSession session, string body, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;
        var bar = body.IndexOf('|');
        IReadOnlyList<ItemTemplateData> results = [];
        if (bar >= 0 && uint.TryParse(body.AsSpan(bar + 1), out var id))
        {
            try { results = await items.SearchAsync(new ItemSearchFilter { Entry = id, Limit = 1 }, ct); }
            catch (Exception ex)
            {
                session.Logger.LogDebug(ex, "ADDON itembyid: БД мира недоступна ({Msg})", ex.Message);
            }
        }
        await SendLineAsync(session, "IBEGIN", ct);
        foreach (var it in results)
            await SendLineAsync(session, $"I|{it.Entry}|{it.Quality}|{it.ItemLevel}|{it.RequiredLevel}|{it.Name.Replace('|', ' ')}", ct);
        await SendLineAsync(session, "IEND", ct);
    }

    /// <summary>
    /// Список точек телепорта для панели «Телепорт» аддона: <c>TBEGIN</c> … <c>T|id|faction|name</c> …
    /// <c>TEND</c>. Порядок — Альянс(1) → Орда(2) → Нейтральные(0), внутри фракции — по SortOrder из БД.
    /// Источник — <see cref="ITeleportRepository"/> (та же таблица, что у <c>.tp</c> и каталога меню). Admin-гейт.
    /// </summary>
    private async Task SendTeleportsAsync(WorldSession session, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;
        IReadOnlyList<TeleportLocation> locs;
        try { locs = await teleports.GetAllAsync(ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "ADDON devteleports: БД недоступна ({Msg})", ex.Message);
            locs = [];
        }
        await SendLineAsync(session, "TBEGIN", ct);
        foreach (var loc in locs.OrderBy(l => l.Faction switch { 1 => 0, 2 => 1, _ => 2 }))
            await SendLineAsync(session, $"T|{loc.Id}|{loc.Faction}|{Clean(loc.Name)}", ct);
        await SendLineAsync(session, "TEND", ct);
    }

    /// <summary>
    /// Активные ауры игрока для панели «Бафф» аддона: <c>ABEGIN</c> … <c>A|spellId</c> … <c>AEND</c>.
    /// Иконку/имя клиент берёт сам (GetSpellInfo), снятие — <c>.unbuff &lt;spellId&gt;</c>. Admin-гейт.
    /// </summary>
    private async Task SendAurasAsync(WorldSession session, CancellationToken ct)
    {
        if (!session.IsAdmin)
            return;
        await SendLineAsync(session, "ABEGIN", ct);
        foreach (var a in session.Progression.Auras)
            await SendLineAsync(session, $"A|{a.SpellId}", ct);
        await SendLineAsync(session, "AEND", ct);
    }

    // Заменяем U+00B7 middle dot на ASCII '-' — клиентский WoW-фонт в списке тикетов аддона
    // не содержит этого глифа и рисует '?'. На пергаменте detail-панели другой фонт показывает
    // правильно, но единый ASCII убирает разницу. БД/Web этим не затрагивается.
    // Нельзя заменить на '|' — это разделитель полей Q-протокола ниже.
    private static string Clean(string s) => s.Replace('|', '/').Replace('·', '-');

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
