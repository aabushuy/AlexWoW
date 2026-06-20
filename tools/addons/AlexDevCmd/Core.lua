--[[ Alex Dev Commands — ядро (переработка в единое окно 3 колонки на AlexUI).

  Окно: одна панель из 3 колонок (1:2:4): корни / ветви / рабочее пространство. Навигацию и панели
  держит клиент (UI.lua); действия уходят как dev-команды (SAY, сервер ловит «.» и не эхоит).
  Динамика с сервера — тонкие addon-кадры (префикс «AlexDev»):
    devteleports → TBEGIN / T|id|faction|name / TEND       (список городов)
    itemsearch|… → IBEGIN / I|id|quality|ilvl|reqlvl|name / IEND  (рынок)
  Зависит от библиотеки стиля AlexUI (см. .toc: Dependencies AlexUI).
]]

local PREFIX = "AlexDev"
AlexDevCmd = AlexDevCmd or {}
local A = AlexDevCmd
A.PREFIX = PREFIX

-- ─── Состояние ───
A.teleports = {}        -- [{ id, faction, name }] из devteleports
A.tpBuilding = nil
A.marketItems = {}      -- [{ id, quality, ilvl, reqlvl, name }] из itemsearch
A.marketBuilding = nil
A.creatureQueue = {}    -- накопленные для «Существа»: [{ key, label, level, count }]
A.auras = {}            -- [{ spellId }] активные ауры (имя/иконку резолвит клиент)
A.auraBuilding = nil

-- ─── Справочники (стабильные, маппинг на dev-команды) ───
A.CREATURE_TYPES = {
  { key = "humanoid", label = "Гуманоид" }, { key = "beast", label = "Животное" },
  { key = "demon", label = "Демон" }, { key = "undead", label = "Нежить" },
  { key = "dragonkin", label = "Дракон" }, { key = "elemental", label = "Элементаль" },
  { key = "giant", label = "Великан" }, { key = "mechanical", label = "Механизм" },
}
A.PROFS = {
  { key = "tailoring", label = "Портняжное" }, { key = "blacksmithing", label = "Кузнечное" },
  { key = "leatherworking", label = "Кожевничество" }, { key = "alchemy", label = "Алхимия" },
  { key = "enchanting", label = "Наложение чар" }, { key = "engineering", label = "Инженерное" },
  { key = "jewelcrafting", label = "Ювелирное" }, { key = "mining", label = "Горное дело" },
  { key = "herbalism", label = "Травничество" }, { key = "skinning", label = "Снятие шкур" },
  { key = "cooking", label = "Кулинария" }, { key = "firstaid", label = "Первая помощь" },
  { key = "fishing", label = "Рыбная ловля" },
}
A.STATIONS = {
  { key = "anvil", label = "Наковальня" }, { key = "forge", label = "Горн" },
  { key = "cookfire", label = "Костёр" }, { key = "mailbox", label = "Почтовый ящик" },
}
-- Манекены (Ф2 #14): 5 вариантов `.dummy [..]`. Маг = кастующий + баффы Int/Sta/метка; Лечебный = 70%+самослив.
A.DUMMIES = {
  { arg = "", label = "Бездействующий" },
  { arg = "attack", label = "Воин" },
  { arg = "hunter", label = "Охотник" },
  { arg = "caster", label = "Маг" },
  { arg = "healer", label = "Лечебный" },
}
-- UnitClass('player') второй результат (токен) → ключ для `.trainer <class>`.
A.CLASS_KEYWORD = {
  WARRIOR = "warrior", PALADIN = "paladin", HUNTER = "hunter", ROGUE = "rogue", PRIEST = "priest",
  DEATHKNIGHT = "dk", SHAMAN = "shaman", MAGE = "mage", WARLOCK = "warlock", DRUID = "druid",
}

-- Навигация: корни (col1) и ветви (col2). Корень без branches → панель = key корня.
A.ROOTS = {
  { key = "char", label = "Персонаж", branches = {
    { key = "char.basic", label = "Основное" },
    { key = "char.stats", label = "Характеристики" },
    { key = "char.buff", label = "Бафф" },
  } },
  { key = "trainer", label = "Тренер" },
  { key = "enemies", label = "Враги", branches = {
    { key = "enemies.dummies", label = "Манекены" },
    { key = "enemies.creatures", label = "Существа" },
  } },
  { key = "prof", label = "Профессия", branches = {
    { key = "prof.trainers", label = "Тренер" },
    { key = "prof.stations", label = "Станки" },
  } },
  { key = "reagents", label = "Реагенты" },
  { key = "market", label = "Рынок" },
  { key = "teleport", label = "Телепорт" },
  { key = "qa", label = "QA", branches = {
    { key = "qa.tester", label = "Тестировщик" },
    { key = "qa.spell", label = "Spell QA" },
  } },
}

-- ─── Чистые хелперы (тестируемые) ───
function A.FactionLabel(f)
  return (f == 1 and "Альянс") or (f == 2 and "Орда") or "Нейтральные"
end

-- Разобрать строку «T|id|faction|name» → таблица или nil (нечисловой id/фракция пропускается).
function A.ParseTeleport(line)
  local _, id, faction, name = strsplit("|", line)
  id = tonumber(id)
  if not id then return nil end
  return { id = id, faction = tonumber(faction) or 0, name = name or ("#" .. id) }
end

-- Класс игрока → ключ для .trainer (и локализованное имя для заголовка). nil, если не определён.
function A.PlayerTrainer()
  local localized, token = UnitClass("player")
  local key = token and A.CLASS_KEYWORD[token]
  if not key then return nil end
  return key, localized or token
end

-- Добавить запись в очередь существ.
function A.AddCreature(key, label, level, count)
  level = tonumber(level); count = tonumber(count)
  if not key or not level or level < 1 then return false end
  if not count or count < 1 then count = 1 end
  A.creatureQueue[#A.creatureQueue + 1] = { key = key, label = label or key, level = level, count = count }
  return true
end

-- ─── Команды/запросы серверу ───
function A.Cmd(command) SendChatMessage(command, "SAY") end
function A.RequestTeleports() SendAddonMessage(PREFIX, "devteleports", "WHISPER", UnitName("player")) end
function A.RequestMarket(body) SendAddonMessage(PREFIX, body, "WHISPER", UnitName("player")) end
function A.RequestAuras() SendAddonMessage(PREFIX, "auras", "WHISPER", UnitName("player")) end

-- ─── Разбор кадров от сервера ───
local function HandleLine(line)
  if line == "TBEGIN" then
    A.tpBuilding = {}
  elseif line == "TEND" then
    if A.tpBuilding then
      A.teleports = A.tpBuilding; A.tpBuilding = nil
      if A.UI then A.UI.OnTeleports() end
    end
  elseif A.tpBuilding and string.sub(line, 1, 2) == "T|" then
    local t = A.ParseTeleport(line)
    if t then A.tpBuilding[#A.tpBuilding + 1] = t end
  elseif line == "IBEGIN" then
    A.marketBuilding = {}
  elseif line == "IEND" then
    if A.marketBuilding then
      A.marketItems = A.marketBuilding; A.marketBuilding = nil
      if A.UI then A.UI.OnMarket() end
    end
  elseif A.marketBuilding and string.sub(line, 1, 2) == "I|" then
    local _, id, q, il, rl, nm = strsplit("|", line)
    id = tonumber(id)
    if id then
      A.marketBuilding[#A.marketBuilding + 1] = { id = id, quality = tonumber(q) or 1,
        ilvl = tonumber(il) or 0, reqlvl = tonumber(rl) or 0, name = nm or ("#" .. id) }
    end
  elseif line == "ABEGIN" then
    A.auraBuilding = {}
  elseif line == "AEND" then
    if A.auraBuilding then
      A.auras = A.auraBuilding; A.auraBuilding = nil
      if A.UI then A.UI.OnAuras() end
    end
  elseif A.auraBuilding and string.sub(line, 1, 2) == "A|" then
    local id = tonumber(string.sub(line, 3))
    if id then A.auraBuilding[#A.auraBuilding + 1] = { spellId = id } end
  end
end

-- ─── Загрузка ───
local loader = CreateFrame("Frame")
loader:RegisterEvent("ADDON_LOADED")
loader:RegisterEvent("PLAYER_ENTERING_WORLD")
loader:RegisterEvent("CHAT_MSG_ADDON")
loader:SetScript("OnEvent", function(_, event, ...)
  if event == "ADDON_LOADED" then
    if ... ~= "AlexDevCmd" then return end
    AlexDevCmdDB = AlexDevCmdDB or {}
    if A.UI then A.UI.Build() end
  elseif event == "PLAYER_ENTERING_WORLD" then
    A.RequestTeleports()  -- список городов безопасно перезапросить после входа/телепорта
  elseif event == "CHAT_MSG_ADDON" then
    local prefix, message = ...
    if prefix == PREFIX and message then HandleLine(message) end
  end
end)

SLASH_ALEXDEVCMD1 = "/dev"
SLASH_ALEXDEVCMD2 = "/devcmd"
SlashCmdList["ALEXDEVCMD"] = function() if A.UI then A.UI.Toggle() end end

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexDevCmd|r загружен — кнопка у миникарты или /dev.")
