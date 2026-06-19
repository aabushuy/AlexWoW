--[[ Alex QA Tester (KB14) — общее ядро аддона тестировщика.

  Архитектура: монолит KB8 разнесён на 3 файла под одним неймспейсом AlexQATester:
    Core.lua       — состояние, addon-протокол с сервером, миникнопка, слэш, события
    UI.lua         — главное окно AlexQATesterFrame (список + детализация + сабмит)
    MenuPanel.lua  — вертикальная панель из 4 кнопок: Общее/Абилки/Таланты/Профессия

  Поток открытия: миникнопка/`/qa` → MenuPanel.Toggle → клик по вкладке → UI.OpenWithKind(kind).

  Сервер: addon-команда `qatasks|<kind>` (kind ∈ general/abilities/talents/professions; без kind =
  general) → кадр `QBEGIN` / `Q|id|title|steps|expected[|spellId|school]` / `QEND`. Сабмит —
  `qasubmit|id|pass|comment`. Транспорт — addon-сообщения с префиксом `AlexDev` (общий с dev-пультом;
  оба аддона маршрутизируют по телу и игнорируют чужие кадры). Старая команда `qatasks` без kind
  трактуется сервером как general — обратная совместимость на случай частичного деплоя.
]]

AlexQATester = AlexQATester or {}
local A = AlexQATester

A.PREFIX = "AlexDev"
A.tasks = {}          -- [{ id, title, steps, expected, spellId?, school? }]
A.building = nil       -- временный список между QBEGIN и QEND
A.selected = nil       -- индекс выбранной задачи
A.currentKind = "general"
A.KINDS = {
  { kind = "general",     label = "Общее" },
  { kind = "abilities",   label = "Абилки" },
  { kind = "talents",     label = "Таланты" },
  { kind = "professions", label = "Профессия" },
}
A.KIND_LABEL = {}
for _, it in ipairs(A.KINDS) do A.KIND_LABEL[it.kind] = it.label end

-- Число UTF-8 символов (решаем, обрезан ли заголовок при показе ≤2 строк).
function A.ulen(s)
  local n = 0
  for i = 1, #s do local b = string.byte(s, i); if b < 128 or b >= 192 then n = n + 1 end end
  return n
end
A.TITLE_MAX = 60

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

-- ---- Запрос/отправка ----
function A.RequestTasks(kind)
  A.currentKind = kind or A.currentKind or "general"
  SendAddonMessage(A.PREFIX, "qatasks|" .. A.currentKind, "WHISPER", UnitName("player"))
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

local function HandleLine(line)
  if line == "QBEGIN" then
    A.building = {}
  elseif line == "QEND" then
    if A.building then
      A.tasks = A.building; A.building = nil
      A.selected = (#A.tasks > 0) and 1 or nil
      if A.UI then A.UI.OnTasksLoaded() end
    end
  elseif A.building and string.sub(line, 1, 2) == "Q|" then
    -- Поля 6..7 (spellId, school) необязательны: пустые → nil. На сервере шаг 3 заполняет их для regression-вкладок.
    local _, id, title, steps, expected, spellId, school = strsplit("|", line)
    id = tonumber(id)
    if id then
      A.building[#A.building + 1] = {
        id = id, title = title or ("#" .. id), steps = steps or "", expected = expected or "",
        spellId = tonumber(spellId), school = tonumber(school),
      }
    end
  elseif string.sub(line, 1, 6) == "QDONE|" then
    local _, id, status = strsplit("|", line)
    id = tonumber(id)
    if id then RemoveTaskById(id) end
    if A.UI then A.UI.OnTasksLoaded() end
    if A.UI then A.UI.SetMsg("Готово: задача → " .. (status or "?"), false) end
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
  btn:SetScript("OnClick", function() if A.MenuPanel then A.MenuPanel.Toggle() end end)
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
    GameTooltip:AddLine("Клик — меню вкладок", 1, 1, 1)
    GameTooltip:Show()
  end)
  btn:SetScript("OnLeave", function() GameTooltip:Hide() end)
  UpdatePos(btn)
end

-- ---- Загрузка ----
local loader = CreateFrame("Frame")
loader:RegisterEvent("ADDON_LOADED")
loader:RegisterEvent("PLAYER_ENTERING_WORLD")
loader:RegisterEvent("CHAT_MSG_ADDON")
loader:SetScript("OnEvent", function(self, event, ...)
  if event == "ADDON_LOADED" then
    if ... ~= "AlexQATester" then return end
    AlexQATesterDB = AlexQATesterDB or {}
    if A.UI then A.UI.Build() end
    if A.MenuPanel then A.MenuPanel.Build() end
    BuildMinimapButton()
  elseif event == "CHAT_MSG_ADDON" then
    local prefix, message = ...
    if prefix == A.PREFIX and message then HandleLine(message) end
  end
end)

SLASH_ALEXQATESTER1 = "/qa"
SLASH_ALEXQATESTER2 = "/qatest"
SlashCmdList["ALEXQATESTER"] = function() if A.MenuPanel then A.MenuPanel.Toggle() end end

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexQATester|r загружен — кнопка у миникарты или /qa.")
