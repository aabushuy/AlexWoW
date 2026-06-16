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

        // Порядок корней = порядок создания категорий ниже (CommitCatalog собирает roots по `order`).
        // §175: новая структура меню (Персонаж → Баффы → Враги → Манекены → Телепорт → Тренеры → Крафт →
        // Прочее → Spell QA), вложенные подкатегории, без многоточий в подписях.

        // 1. Персонаж
        var character = b.Category("Персонаж");
        b.Prompt(character, "Уровень", ".level ", "Установить уровень (1–80):");
        b.Prompt(character, "Опыт", ".xp ", "Добавить опыт:");
        b.Prompt(character, "Выдать предмет", ".additem ", "ID предмета [кол-во]:");
        b.Prompt(character, "Изучить спелл", ".learn ", "ID спелла:");
        b.Cmd(character, "Выучить всё у тренера", ".learnall");

        // 2. Баффы
        var buffs = b.Category("Баффы");
        var buffsApply = b.Sub(buffs, "Наложить");
        b.Prompt(buffsApply, "Бафф", ".buff ", "ID спелла [секунды]:");
        b.Prompt(buffsApply, "Дебафф", ".debuff ", "ID спелла [секунды] (стенд для диспела):");
        var buffsRemove = b.Sub(buffs, "Снять");
        b.Prompt(buffsRemove, "Снять по id", ".unbuff ", "ID спелла:");
        b.Cmd(buffsRemove, "Снять все", ".unbuff all"); // §176
        // §178/§179 «Характеристики» — лист kind=stats, открывает в аддоне окно редактора вторичных
        // характеристик (крит/уклон/броня/оружие…). Запрос/запись значений — addon-протокол stats/.setstat.
        b.StatsEditor(buffs, "Характеристики");

        // 3. Враги (.spawnenemy): по типу существа → prompt «уровень [кол-во]» (как «Выдать предмет»).
        var enemies = b.Category("Враги");
        b.Prompt(enemies, "Гуманоид", ".spawnenemy humanoid ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Животное", ".spawnenemy beast ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Демон", ".spawnenemy demon ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Нежить", ".spawnenemy undead ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Дракон", ".spawnenemy dragonkin ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Элементаль", ".spawnenemy elemental ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Великан", ".spawnenemy giant ", "Уровень [кол-во]:");
        b.Prompt(enemies, "Механизм", ".spawnenemy mechanical ", "Уровень [кол-во]:");

        // 4. Манекены
        var dummies = b.Category("Манекены");
        b.Cmd(dummies, "Тренировочный (урон)", ".dummy");
        b.Cmd(dummies, "Лечебный", ".dummy heal");
        b.Cmd(dummies, "Атакующий (защита)", ".dummy attack");
        b.Cmd(dummies, "Кастующий (прерывание)", ".dummy caster");

        // 5. Телепорт — динамическая ветка из БД (alexwow_auth.dev_teleport), сгруппирована по фракции.
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

        // 6. Тренеры → Классы / Профессии
        var trainers = b.Category("Тренеры");
        var classes = b.Sub(trainers, "Классы");
        b.Cmd(classes, "Разучить всё", ".trainer off");
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
        // §177: профессии — прямое «Выучить/Забыть» у самого игрока (.prof <prof> learn|forget),
        // без спавна тренера. Команда .proftrainer остаётся доступной вручную, но из меню убрана.
        var profs = b.Sub(trainers, "Профессии");
        ProfLearnForget(b, profs, "Портняжное", "tailoring");
        ProfLearnForget(b, profs, "Кузнечное", "blacksmithing");
        ProfLearnForget(b, profs, "Кожевничество", "leatherworking");
        ProfLearnForget(b, profs, "Алхимия", "alchemy");
        ProfLearnForget(b, profs, "Наложение чар", "enchanting");
        ProfLearnForget(b, profs, "Инженерное", "engineering");
        ProfLearnForget(b, profs, "Ювелирное", "jewelcrafting");
        ProfLearnForget(b, profs, "Горное дело", "mining");
        ProfLearnForget(b, profs, "Травничество", "herbalism");
        ProfLearnForget(b, profs, "Снятие шкур", "skinning");
        ProfLearnForget(b, profs, "Кулинария", "cooking");
        ProfLearnForget(b, profs, "Первая помощь", "firstaid");
        ProfLearnForget(b, profs, "Рыбная ловля", "fishing");

        // 7. Крафт → Станки / Реагенты
        var craft = b.Category("Крафт");
        var stations = b.Sub(craft, "Станки");
        b.Cmd(stations, "Наковальня", ".craft anvil");
        b.Cmd(stations, "Горн", ".craft forge");
        b.Cmd(stations, "Костёр", ".craft cookfire");
        b.Cmd(stations, "Почтовый ящик", ".craft mailbox");
        b.Cmd(stations, "Убрать все", ".craft off");
        var reagent = b.Sub(craft, "Реагенты");
        b.Cmd(reagent, "Поставить", ".reagentvendor");
        b.Cmd(reagent, "Снять", ".reagentvendor off");

        // 8. Прочее
        var misc = b.Category("Прочее");
        b.Cmd(misc, "Снести dev-сущности", ".devclean");

        // 9. Spell QA: захват проверки заклинаний (.spelltest) — ручной режим и авто-прогон.
        var spellTest = b.Category("Spell QA");
        b.Cmd(spellTest, "Старт захвата", ".spelltest start");
        b.Cmd(spellTest, "Стоп захвата", ".spelltest stop");
        b.Cmd(spellTest, "Статус", ".spelltest status");
        b.Cmd(spellTest, "Авто-прогон ×5", ".spelltest run");
        b.Prompt(spellTest, "Авто-прогон ×N", ".spelltest run ", "Повторов на спелл:");

        return b.Lines;
    }

    /// <summary>Профессия в меню (§177): подкатегория с листьями «Выучить»/«Забыть» (.prof KEY learn|forget).</summary>
    private static void ProfLearnForget(Builder b, int parent, string label, string key)
    {
        var node = b.Sub(parent, label);
        b.Cmd(node, "Выучить", $".prof {key} learn");
        b.Cmd(node, "Забыть", $".prof {key} forget");
    }

    /// <summary>
    /// Сборщик строк-узлов. Каждый узел: <c>N|id|parentId|kind|label|payload…</c>, разделитель — '|'
    /// (в метках/командах его нет). kind: <c>cat</c> (категория/подкатегория, parentId 0 = корень),
    /// <c>cmd</c> (лист-команда, payload = команда для SAY), <c>prompt</c> (лист-ввод, payload =
    /// префикс|подсказка), <c>tp</c> (лист-телепорт, payload = id города; метка = имя города),
    /// <c>stats</c> (§178, лист-открывашка окна редактора вторичных характеристик, без payload).
    /// </summary>
    private sealed class Builder
    {
        private int _id;
        public List<string> Lines { get; } = [];

        public int Category(string label) => Node(0, "cat", label);
        public int Sub(int parent, string label) => Node(parent, "cat", label);
        public void Cmd(int parent, string label, string command) => Node(parent, "cmd", label, command);
        public void StatsEditor(int parent, string label) => Node(parent, "stats", label); // §178: лист-открывашка окна редактора статов
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
