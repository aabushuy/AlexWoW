--[[ Headless-тесты логики аддона AlexQATester (вне клиента WoW).

  Запуск:  luajit alexqatester_spec.lua   (из tools/addons/tests)
  Грузит мок WoW API, затем Core.lua, и проверяет:
    - Sanitize: срез символов без глифа в шрифтах 3.3.5a
    - ulen / utrunc: подсчёт и обрезка по UTF-8 символам (без разрыва многобайтовых)
    - парсер кадров протокола QBEGIN/Q|.../QEND/QDONE через реальный CHAT_MSG_ADDON шов

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
    print(string.format("FAIL  %-42s got %q  want %q", name, tostring(got), tostring(want)))
  else
    print(string.format("ok    %s", name))
  end
end

-- Байтовые литералы спецсимволов (UTF-8), которые приходят с сервера.
local MIDDLE_DOT = "\194\183"          -- U+00B7
local ARROW_L    = "\226\134\144"      -- U+2190 ←
local ARROW_R    = "\226\134\146"      -- U+2192 →
local ELLIPSIS   = "\226\128\166"      -- U+2026 … (его дописывает utrunc)

-- ---- Sanitize ----
check("Sanitize: nil -> nil",        A.Sanitize(nil),                  nil)
check("Sanitize: plain unchanged",   A.Sanitize("plain text"),         "plain text")
check("Sanitize: middle dot -> -",   A.Sanitize("a" .. MIDDLE_DOT .. "b"), "a-b")
check("Sanitize: left arrow -> <-",  A.Sanitize("x" .. ARROW_L .. "y"),    "x<-y")
check("Sanitize: right arrow -> ->", A.Sanitize("x" .. ARROW_R .. "y"),    "x->y")

-- ---- ulen (число UTF-8 символов) ----
check("ulen: ascii",        A.ulen("abc"),                        3)
check("ulen: 2-byte char",  A.ulen(MIDDLE_DOT),                   1)
check("ulen: mixed",        A.ulen("a" .. ARROW_R .. "b"),        3)

-- ---- utrunc (обрезка по символам + многоточие, без разрыва байтов) ----
check("utrunc: no truncation",      A.utrunc("hello", 10),        "hello")
check("utrunc: ascii truncation",   A.utrunc("hello", 3),         "hel" .. ELLIPSIS)
check("utrunc: multibyte boundary", A.utrunc(MIDDLE_DOT:rep(5), 2), MIDDLE_DOT:rep(2) .. ELLIPSIS)

-- ---- Парсер кадров протокола через CHAT_MSG_ADDON ----
local PREFIX = A.PREFIX
mock.fire("CHAT_MSG_ADDON", PREFIX, "QBEGIN")
mock.fire("CHAT_MSG_ADDON", PREFIX, "Q|101|Title A|step1|exp1")
mock.fire("CHAT_MSG_ADDON", PREFIX, "Q|102|Title B|step2|exp2|589|32")  -- + spellId/school
mock.fire("CHAT_MSG_ADDON", PREFIX, "Q|abc|Bad id|s|e")                 -- нечисловой id — пропустить
mock.fire("CHAT_MSG_ADDON", PREFIX, "Q|103|A" .. MIDDLE_DOT .. "B|s|e") -- заголовок санитизируется
mock.fire("CHAT_MSG_ADDON", PREFIX, "QEND")

check("parser: task count (bad id skipped)", #A.tasks,        3)
check("parser: t1 id",                       A.tasks[1].id,   101)
check("parser: t1 title",                    A.tasks[1].title, "Title A")
check("parser: t1 steps",                    A.tasks[1].steps, "step1")
check("parser: t1 expected",                 A.tasks[1].expected, "exp1")
check("parser: t1 spellId nil",              A.tasks[1].spellId, nil)
check("parser: t1 school nil",               A.tasks[1].school, nil)
check("parser: t2 spellId",                  A.tasks[2].spellId, 589)
check("parser: t2 school",                   A.tasks[2].school, 32)
check("parser: t3 title sanitized",          A.tasks[3].title, "A-B")

-- QDONE удаляет тикет по id
mock.fire("CHAT_MSG_ADDON", PREFIX, "QDONE|101|passed")
check("parser: QDONE removes one",   #A.tasks,        2)
check("parser: QDONE removed right", A.tasks[1].id,   102)

-- Чужой префикс игнорируется (не наш аддон-канал)
mock.fire("CHAT_MSG_ADDON", "OtherAddon", "QBEGIN")
mock.fire("CHAT_MSG_ADDON", "OtherAddon", "QEND")
check("parser: foreign prefix ignored", #A.tasks, 2)

-- ---- итог ----
print(string.format("\n%d checks, %d failed", total, failed))
os.exit(failed > 0 and 1 or 0)
