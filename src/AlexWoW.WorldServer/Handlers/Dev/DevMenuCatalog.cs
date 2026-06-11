using System.Text;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Серверный каталог dev-меню для аддона (Devcommands #79): дерево «категория → (подкатегория) → лист».
/// Раньше дерево хардкодилось в Lua аддона; теперь его отдаёт сервер (единый источник правды), а аддон лишь
/// рисует пришедшее. Статические группы (тренеры/станки/персонаж/баффы) + динамическая ветка «Телепорт» из
/// таблицы <c>dev_teleport</c> (alexwow_auth). Формат строк — см. <see cref="Builder"/> и AddonProtocol.
/// DI-синглтон (M7 S8), потребитель — AddonProtocol.
/// </summary>
internal sealed class DevMenuCatalog(ITeleportRepository teleports)
{
    /// <summary>Префикс addon-сообщений (общий для запроса и ответа).</summary>
    public const string Prefix = "AlexDev";

    /// <summary>Строки-узлы каталога (без кадров BEGIN/END — их добавляет AddonProtocol).</summary>
    public async Task<IReadOnlyList<string>> BuildAsync(WorldSession session, CancellationToken ct)
    {
        var b = new Builder();

        var classes = b.Category("Тренеры классов");
        b.Cmd(classes, "Воин", ".trainer warrior");
        b.Cmd(classes, "Паладин", ".trainer paladin");
        b.Cmd(classes, "Охотник", ".trainer hunter");
        b.Cmd(classes, "Разбойник", ".trainer rogue");
        b.Cmd(classes, "Жрец", ".trainer priest");
        b.Cmd(classes, "Рыцарь смерти", ".trainer dk");
        b.Cmd(classes, "Шаман", ".trainer shaman");
        b.Cmd(classes, "Маг", ".trainer mage");
        b.Cmd(classes, "Чернокнижник", ".trainer warlock");
        b.Cmd(classes, "Друид", ".trainer druid");
        b.Cmd(classes, "Снять", ".trainer off");

        var profs = b.Category("Тренеры профессий");
        b.Cmd(profs, "Портняжное", ".proftrainer tailoring");
        b.Cmd(profs, "Кузнечное", ".proftrainer blacksmithing");
        b.Cmd(profs, "Кожевничество", ".proftrainer leatherworking");
        b.Cmd(profs, "Алхимия", ".proftrainer alchemy");
        b.Cmd(profs, "Наложение чар", ".proftrainer enchanting");
        b.Cmd(profs, "Инженерное", ".proftrainer engineering");
        b.Cmd(profs, "Ювелирное", ".proftrainer jewelcrafting");
        b.Cmd(profs, "Горное дело", ".proftrainer mining");
        b.Cmd(profs, "Травничество", ".proftrainer herbalism");
        b.Cmd(profs, "Снятие шкур", ".proftrainer skinning");
        b.Cmd(profs, "Кулинария", ".proftrainer cooking");
        b.Cmd(profs, "Первая помощь", ".proftrainer firstaid");
        b.Cmd(profs, "Рыбная ловля", ".proftrainer fishing");
        b.Cmd(profs, "Снять", ".proftrainer off");

        var craft = b.Category("Крафт-станки");
        b.Cmd(craft, "Наковальня", ".craft anvil");
        b.Cmd(craft, "Горн", ".craft forge");
        b.Cmd(craft, "Костёр", ".craft cookfire");
        b.Cmd(craft, "Почтовый ящик", ".craft mailbox");
        b.Cmd(craft, "Убрать все", ".craft off");

        var reagent = b.Category("Вендор реагентов");
        b.Cmd(reagent, "Поставить", ".reagentvendor");
        b.Cmd(reagent, "Снять", ".reagentvendor off");

        var character = b.Category("Персонаж");
        b.Prompt(character, "Уровень…", ".level ", "Установить уровень (1–80):");
        b.Prompt(character, "Опыт…", ".xp ", "Добавить опыт:");
        b.Prompt(character, "Выдать предмет…", ".additem ", "ID предмета [кол-во]:");
        b.Prompt(character, "Изучить спелл…", ".learn ", "ID спелла:");
        b.Cmd(character, "Выучить всё у тренера", ".learnall");

        var buffs = b.Category("Баффы");
        b.Prompt(buffs, "Наложить бафф…", ".buff ", "ID спелла [секунды]:");
        b.Prompt(buffs, "Снять бафф…", ".unbuff ", "ID спелла:");

        var misc = b.Category("Прочее");
        b.Cmd(misc, "Тренировочный манекен", ".dummy");
        b.Cmd(misc, "Лечебный манекен", ".dummy heal");
        b.Cmd(misc, "Снести dev-сущности", ".devclean");

        // M12 Spell QA: захват проверки заклинаний (.spelltest) — ручной режим и авто-прогон.
        var spellTest = b.Category("Spell QA");
        b.Cmd(spellTest, "Старт захвата", ".spelltest start");
        b.Cmd(spellTest, "Стоп захвата", ".spelltest stop");
        b.Cmd(spellTest, "Статус", ".spelltest status");
        b.Cmd(spellTest, "Авто-прогон ×5", ".spelltest run");
        b.Prompt(spellTest, "Авто-прогон ×N…", ".spelltest run ", "Повторов на спелл:");

        // Динамическая ветка «Телепорт» из БД (alexwow_auth.dev_teleport), сгруппирована по фракции.
        var teleport = b.Category("Телепорт");
        var locations = await teleports.GetAllAsync(ct);
        var alliance = b.Sub(teleport, "Альянс");
        var horde = b.Sub(teleport, "Орда");
        var neutral = b.Sub(teleport, "Нейтральные");
        foreach (var loc in locations)
        {
            var parent = loc.Faction switch { 1 => alliance, 2 => horde, _ => neutral };
            b.Tp(parent, loc.Name, loc.Id);
        }

        return b.Lines;
    }

    /// <summary>
    /// Сборщик строк-узлов. Каждый узел: <c>N|id|parentId|kind|label|payload…</c>, разделитель — '|'
    /// (в метках/командах его нет). kind: <c>cat</c> (категория/подкатегория, parentId 0 = корень),
    /// <c>cmd</c> (лист-команда, payload = команда для SAY), <c>prompt</c> (лист-ввод, payload =
    /// префикс|подсказка), <c>tp</c> (лист-телепорт, payload = id города; метка = имя города).
    /// </summary>
    private sealed class Builder
    {
        private int _id;
        public List<string> Lines { get; } = [];

        public int Category(string label) => Node(0, "cat", label);
        public int Sub(int parent, string label) => Node(parent, "cat", label);
        public void Cmd(int parent, string label, string command) => Node(parent, "cmd", label, command);
        public void Prompt(int parent, string label, string prefix, string hint) => Node(parent, "prompt", label, prefix, hint);
        public void Tp(int parent, string cityName, uint cityId)
            => Node(parent, "tp", cityName, cityId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private int Node(int parent, string kind, string label, params string[] payload)
        {
            var id = ++_id;
            var sb = new StringBuilder().Append("N|").Append(id).Append('|').Append(parent)
                .Append('|').Append(kind).Append('|').Append(label);
            foreach (var p in payload)
                sb.Append('|').Append(p);
            Lines.Add(sb.ToString());
            return id;
        }
    }
}
