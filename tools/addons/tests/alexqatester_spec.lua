--[[ Headless-тесты логики аддона AlexQATester (вне клиента WoW).

  Запуск:  luajit alexqatester_spec.lua   (из tools/addons/tests)
  Грузит мок WoW API, затем Core.lua, и проверяет:
    - Sanitize: срез символов без глифа в шрифтах 3.3.5a
    - ulen / utrunc: подсчёт и обрезка по UTF-8 символам (без разрыва многобайтовых)
    - ParseTitle: разбор «#<id> - <Title>» → номер + текст
    - парсер списка QBEGIN/Q|.../QEND + сортировка по Title ASC, QDONE-удаление
    - парсер деталей спелла DBEGIN/DD/DR/DEND

  Выход: код 1 при любом провале (для CI), 0 если всё зелёное.
]]

local here = (arg[0] or ""):match("^(.*[/\\])") or "./"
local mock = dofile(here .. "wow_stub.lua")
dofile(here .. "../AlexQATester/Core.lua")

local A = AlexQATester
assert(A, "Core.lua должен определить глобал AlexQATester")

-- ---- мини-фреймворк ассертов ----
local total, failed = 0, 0
local function check(name, got, want)
  total = total + 1
  if got ~= want then
    failed = failed + 1
    print(string.format("FAIL  %-44s got %q  want %q", name, tostring(got), tostring(want)))
  else
    print(string.format("ok    %s", name))
  end
end

local function send(line) mock.fire("CHAT_MSG_ADDON", A.PREFIX, line) end

-- Байтовые литералы спецсимволов (UTF-8), которые приходят с сервера.
local MIDDLE_DOT = "\194\183"          -- U+00B7
local ARROW_R    = "\226\134\146"      -- U+2192 →
local ELLIPSIS   = "\226\128\166"      -- U+2026 …

-- ---- Sanitize / ulen / utrunc ----
check("Sanitize: nil -> nil",        A.Sanitize(nil),                      nil)
check("Sanitize: middle dot -> -",   A.Sanitize("a" .. MIDDLE_DOT .. "b"), "a-b")
check("ulen: mixed",                 A.ulen("a" .. ARROW_R .. "b"),        3)
check("utrunc: ascii truncation",    A.utrunc("hello", 3),                 "hel" .. ELLIPSIS)
check("utrunc: multibyte boundary",  A.utrunc(MIDDLE_DOT:rep(5), 2),       MIDDLE_DOT:rep(2) .. ELLIPSIS)

-- ---- ParseTitle ----
local n1, t1 = A.ParseTitle("#101 - Foo Bar")
check("ParseTitle: id",        n1, 101)
check("ParseTitle: title",     t1, "Foo Bar")
local n2, t2 = A.ParseTitle("#5 - A - B")          -- тире внутри заголовка сохраняется
check("ParseTitle: nested dash id",    n2, 5)
check("ParseTitle: nested dash title", t2, "A - B")
local n3, t3 = A.ParseTitle("без префикса")
check("ParseTitle: no prefix id",    n3, nil)
check("ParseTitle: no prefix title", t3, "без префикса")

-- ---- Список тикетов: парс + сортировка по Title ASC (без учёта регистра) ----
send("QBEGIN")
send("Q|102|#102 - Zebra|s2|e2")
send("Q|101|#101 - apple|s1|e1")
send("Q|103|#103 - Mango|s3|e3|133|4")   -- + spellId/school
send("Q|abc|#0 - Bad id|s|e")            -- нечисловой id — пропустить
send("QEND")

check("list: count (bad id skipped)", #A.tasks,         3)
check("list: sorted #1 = apple",      A.tasks[1].id,    101)
check("list: sorted #2 = Mango",      A.tasks[2].id,    103)
check("list: sorted #3 = Zebra",      A.tasks[3].id,    102)
check("list: title kept as display",  A.tasks[1].title, "#101 - apple")
check("list: name parsed",            A.tasks[1].name,  "apple")
check("list: steps",                  A.tasks[1].steps, "s1")
check("list: Mango spellId",          A.tasks[2].spellId, 133)
check("list: Mango school",           A.tasks[2].school,  4)
check("list: apple spellId nil",      A.tasks[1].spellId, nil)

-- ---- Детали спелла: DBEGIN/DD/DR/DEND ----
send("DBEGIN|133")
send("DD|Школа|Огонь")
send("DD|Эффект|Прямой урон школой (SCHOOL_DAMAGE): 100-120")
send("DR|2835|2|Грубый камень")
send("DR|2840|1|Медный слиток")
send("DEND")

local det = A.details[133]
check("detail: cached for 133",     det ~= nil,            true)
check("detail: meta count",         det and #det.meta,     2)
check("detail: meta[1] label",      det and det.meta[1].label, "Школа")
check("detail: meta[1] value",      det and det.meta[1].value, "Огонь")
check("detail: reagent count",      det and #det.reagents, 2)
check("detail: reagent[1] id",      det and det.reagents[1].itemId, 2835)
check("detail: reagent[1] count",   det and det.reagents[1].count,  2)
check("detail: reagent[2] name",    det and det.reagents[2].name,   "Медный слиток")

-- ---- QDONE удаляет тикет по id ----
send("QDONE|101|Done")
check("QDONE: removes one",   #A.tasks,        2)
check("QDONE: removed apple", A.tasks[1].id,   103)   -- остались Mango(103), Zebra(102), сорт сохранён

-- ---- Чужой префикс игнорируется ----
mock.fire("CHAT_MSG_ADDON", "OtherAddon", "QBEGIN")
mock.fire("CHAT_MSG_ADDON", "OtherAddon", "QEND")
check("foreign prefix ignored", #A.tasks, 2)

-- ---- итог ----
print(string.format("\n%d checks, %d failed", total, failed))
os.exit(failed > 0 and 1 or 0)
