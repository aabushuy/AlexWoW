--[[ Alex QA Tester — ядро аддона тестировщика (KB14, переработка в одно окно).

  Архитектура: один неймспейс AlexQATester, два файла:
    Core.lua — состояние, addon-протокол с сервером, чистые хелперы, миникнопка, слэш, события
    UI.lua   — единое окно из 3 колонок (нав по типу / список / детализация)

  Поток: миникнопка / `/qa` → UI.Toggle() → клик по группе (Общие/Абилки/Таланты/Профессии) →
  RequestTasks(kind) → список тикетов → клик по тикету → детализация (+ qaspell для спелл-тикетов).

  Протокол (addon-сообщения, префикс «AlexDev», общий с dev-пультом — маршрутизация по телу кадра):
    qatasks|<kind>            → QBEGIN / Q|id|title|steps|expected[|spellId|school] / QEND
    qaspell|<spellId>         → DBEGIN|id / DD|label|value / DR|itemId|count|name / DEND
    qasubmit|id|pass|comment  → QDONE|id|status | QERR|id|msg
  title приходит как «#<id> - <Title>» (сервер уже подставил номер и почистил спецсимволы).
]]

AlexQATester = AlexQATester or {}
local A = AlexQATester

A.PREFIX = "AlexDev"
A.tasks = {}             -- [{ id, title, name, steps, expected, spellId?, school? }]
A.building = nil          -- временный список между QBEGIN и QEND
A.selected = nil          -- индекс выбранного тикета
A.currentKind = "general"
A.details = {}            -- кэш деталей спелла: [spellId] = { meta = {{label,value}}, reagents = {{itemId,count,name}} }
A.detailBuilding = nil    -- временные детали между DBEGIN и DEND
A.detailSpellId = nil

A.KINDS = {
  { kind = "general",     label = "Общие" },
  { kind = "abilities",   label = "Абилки" },
  { kind = "talents",     label = "Таланты" },
  { kind = "professions", label = "Профессия" },
}
A.KIND_LABEL = {}
for _, it in ipairs(A.KINDS) do A.KIND_LABEL[it.kind] = it.label end

-- Срезать символы без глифа в шрифтах клиента 3.3.5a (рисуются «?»): U+00B7 middle dot,
-- ←/→ (U+2190/U+2192). Применяется к любому пришедшему с сервера тексту перед показом.
function A.Sanitize(s)
  if not s then return s end
  return (s:gsub("\194\183", "-"):gsub("\226\134\144", "<-"):gsub("\226\134\146", "->"))
end

-- Число UTF-8 символов.
function A.ulen(s)
  local n = 0
  for i = 1, #s do local b = string.byte(s, i); if b < 128 or b >= 192 then n = n + 1 end end
  return n
end
A.TITLE_MAX = 70

-- Обрезать строку до maxChars UTF-8 символов (+ «…»), без разрыва многобайтовых символов.
function A.utrunc(s, maxChars)
  if A.ulen(s) <= maxChars then return s end
  local i, n = 1, 0
  while i <= #s and n < maxChars do
    local b = string.byte(s, i)
    local clen = (b < 128 and 1) or (b < 224 and 2) or (b < 240 and 3) or 4
    i = i + clen; n = n + 1
  end
  return string.sub(s, 1, i - 1) .. "…"
end

-- Разобрать пришедший заголовок «#<id> - <Title>» → номер тикета и текст Title (для показа и сортировки).
-- Если формат иной — номер nil, title = вся строка.
function A.ParseTitle(combined)
  combined = combined or ""
  local num, title = combined:match("^#(%d+)%s*%-%s*(.*)$")
  if num then return tonumber(num), title end
  return nil, combined
end

-- ---- Запрос/отправка ----
function A.RequestTasks(kind)
  A.currentKind = kind or A.currentKind or "general"
  SendAddonMessage(A.PREFIX, "qatasks|" .. A.currentKind, "WHISPER", UnitName("player"))
end

-- Запросить детали спелла, если их ещё нет в кэше (UI рисует кэш сразу, обновится по DEND).
function A.RequestSpellDetail(spellId)
  if not spellId or A.details[spellId] then return end
  SendAddonMessage(A.PREFIX, "qaspell|" .. spellId, "WHISPER", UnitName("player"))
end

function A.Submit()
  local t = A.tasks[A.selected]
  if not t or not A.UI then return end
  local pass = A.UI.IsChecked() and "1" or "0"
  local comment = A.UI.GetComment() or ""
  if pass == "0" and comment:gsub("%s", "") == "" then
    A.UI.SetMsg("Без галки комментарий обязателен", true)
    return
  end
  SendAddonMessage(A.PREFIX, "qasubmit|" .. t.id .. "|" .. pass .. "|" .. comment, "WHISPER", UnitName("player"))
  A.UI.SetMsg("Отправка…", false)
end

-- ---- Разбор кадров от сервера ----
local function RemoveTaskById(id)
  for i, t in ipairs(A.tasks) do
    if t.id == id then table.remove(A.tasks, i); break end
  end
  if A.selected and not A.tasks[A.selected] then A.selected = nil end
end

-- Сортировка списка по тексту Title (ASC, без учёта регистра).
local function SortTasks()
  table.sort(A.tasks, function(a, b) return (a.name or ""):lower() < (b.name or ""):lower() end)
end

local function HandleLine(line)
  -- ── список тикетов ──
  if line == "QBEGIN" then
    A.building = {}
  elseif line == "QEND" then
    if A.building then
      A.tasks = A.building; A.building = nil
      SortTasks()
      A.selected = nil
      if A.UI then A.UI.OnTasksLoaded() end
    end
  elseif A.building and string.sub(line, 1, 2) == "Q|" then
    -- Поля 6..7 (spellId, school) необязательны: пустые → nil.
    local _, id, title, steps, expected, spellId, school = strsplit("|", line)
    id = tonumber(id)
    if id then
      local display = A.Sanitize(title) or ("#" .. id)
      local _, nameText = A.ParseTitle(display)
      A.building[#A.building + 1] = {
        id = id,
        title = display,
        name = nameText,
        steps = A.Sanitize(steps) or "",
        expected = A.Sanitize(expected) or "",
        spellId = tonumber(spellId), school = tonumber(school),
      }
    end
  -- ── детали спелла ──
  elseif string.sub(line, 1, 7) == "DBEGIN|" then
    A.detailSpellId = tonumber(string.sub(line, 8))
    A.detailBuilding = { meta = {}, reagents = {} }
  elseif line == "DEND" then
    if A.detailBuilding and A.detailSpellId then
      A.details[A.detailSpellId] = A.detailBuilding
      local sid = A.detailSpellId
      A.detailBuilding = nil; A.detailSpellId = nil
      if A.UI then A.UI.OnDetailLoaded(sid) end
    end
  elseif A.detailBuilding and string.sub(line, 1, 3) == "DD|" then
    local _, label, value = strsplit("|", line)
    A.detailBuilding.meta[#A.detailBuilding.meta + 1] = { label = A.Sanitize(label) or "", value = A.Sanitize(value) or "" }
  elseif A.detailBuilding and string.sub(line, 1, 3) == "DR|" then
    local _, itemId, count, name = strsplit("|", line)
    itemId = tonumber(itemId)
    if itemId then
      A.detailBuilding.reagents[#A.detailBuilding.reagents + 1] =
        { itemId = itemId, count = tonumber(count) or 1, name = A.Sanitize(name) or ("#" .. itemId) }
    end
  -- ── сабмит ──
  elseif string.sub(line, 1, 6) == "QDONE|" then
    local _, id, status = strsplit("|", line)
    id = tonumber(id)
    if id then RemoveTaskById(id) end
    if A.UI then
      A.UI.OnTasksLoaded()
      A.UI.SetMsg("Готово: задача → " .. (status or "?"), false)
    end
  elseif string.sub(line, 1, 5) == "QERR|" then
    local _, _, msg = strsplit("|", line)
    if A.UI then A.UI.SetMsg(msg or "Ошибка", true) end
  end
end

-- ---- Кнопка у миникарты ----
local function BuildMinimapButton()
  local btn = CreateFrame("Button", "AlexQATesterMinimapButton", Minimap)
  btn:SetWidth(31); btn:SetHeight(31)
  btn:SetFrameStrata("MEDIUM"); btn:SetFrameLevel(8)
  btn:RegisterForClicks("LeftButtonUp")
  btn:RegisterForDrag("LeftButton")
  btn:SetMovable(true)

  local icon = btn:CreateTexture(nil, "BACKGROUND")
  icon:SetTexture("Interface\\Icons\\INV_Misc_Note_01")
  icon:SetWidth(20); icon:SetHeight(20); icon:SetPoint("CENTER", 0, 1)
  icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)
  local border = btn:CreateTexture(nil, "OVERLAY")
  border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder")
  border:SetWidth(53); border:SetHeight(53); border:SetPoint("TOPLEFT")

  local function UpdatePos(self)
    local angle = math.rad(AlexQATesterDB.minimapAngle or 150)
    self:SetPoint("CENTER", Minimap, "CENTER", 80 * math.cos(angle), 80 * math.sin(angle))
  end
  btn:SetScript("OnClick", function() if A.UI then A.UI.Toggle() end end)
  btn:SetScript("OnDragStart", function(self) self:SetScript("OnUpdate", function(s)
    local mx, my = Minimap:GetCenter()
    local px, py = GetCursorPosition()
    local scale = Minimap:GetEffectiveScale()
    AlexQATesterDB.minimapAngle = math.deg(math.atan2(py / scale - my, px / scale - mx))
    UpdatePos(s)
  end) end)
  btn:SetScript("OnDragStop", function(self) self:SetScript("OnUpdate", nil) end)
  btn:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_LEFT")
    GameTooltip:AddLine("QA Tester")
    GameTooltip:AddLine("Клик — окно тестирования", 1, 1, 1)
    GameTooltip:Show()
  end)
  btn:SetScript("OnLeave", function() GameTooltip:Hide() end)
  UpdatePos(btn)
end

-- ---- Загрузка ----
local loader = CreateFrame("Frame")
loader:RegisterEvent("ADDON_LOADED")
loader:RegisterEvent("CHAT_MSG_ADDON")
loader:SetScript("OnEvent", function(_, event, ...)
  if event == "ADDON_LOADED" then
    if ... ~= "AlexQATester" then return end
    AlexQATesterDB = AlexQATesterDB or {}
    if A.UI then A.UI.Build() end
    BuildMinimapButton()
  elseif event == "CHAT_MSG_ADDON" then
    local prefix, message = ...
    if prefix == A.PREFIX and message then HandleLine(message) end
  end
end)

SLASH_ALEXQATESTER1 = "/qa"
SLASH_ALEXQATESTER2 = "/qatest"
SlashCmdList["ALEXQATESTER"] = function() if A.UI then A.UI.Toggle() end end

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexQATester|r загружен — кнопка у миникарты или /qa.")
