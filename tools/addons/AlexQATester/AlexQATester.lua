--[[ Alex QA Tester (KB9) — клиентский аддон тестировщика. Показывает задачи на тестирование, назначенные
     персонажу на канбан-доске (сервер: addon-команда "qatasks", кадр QBEGIN|Q|id|title|steps|expected|QEND),
     и отправляет результат ("qasubmit|id|pass|comment"). Транспорт — addon-сообщения с префиксом "AlexDev"
     (тот же, что у dev-пульта; сервер маршрутизирует по телу, оба аддона игнорируют чужие кадры).
     Воркфлоу: галка «Соответствует ожидаемому» → Done; без галки → обязателен комментарий → Ready to
     Implementation. Окно из 2 половин: слева список задач, справа детализация + контролы. ]]

local PREFIX = "AlexDev"
local LIST_ROW_H, LIST_NUM_ROWS = 22, 14

local tasks = {}          -- [{ id, title, steps, expected }]
local building = nil       -- временный список между QBEGIN и QEND
local selected = nil       -- индекс выбранной задачи
local mainFrame, listScroll, listRows = nil, nil, {}
local D = {}               -- виджеты правой панели (detail)
local ListRefresh, ShowDetail  -- forward

-- Число UTF-8 символов (решаем, обрезан ли заголовок при показе ≤2 строк).
local function ulen(s)
  local n = 0
  for i = 1, #s do local b = string.byte(s, i); if b < 128 or b >= 192 then n = n + 1 end end
  return n
end
local TITLE_MAX = 84 -- порог «двух строк» по символам для ширины заголовка

-- Обрезать строку до maxChars UTF-8 символов (+ «…»), без разрыва многобайтовых символов.
local function utrunc(s, maxChars)
  if ulen(s) <= maxChars then return s end
  local i, n = 1, 0
  while i <= #s and n < maxChars do
    local b = string.byte(s, i)
    local clen = (b < 128 and 1) or (b < 224 and 2) or (b < 240 and 3) or 4
    i = i + clen; n = n + 1
  end
  return string.sub(s, 1, i - 1) .. "…"
end

-- ---- Запрос/отправка ----
local function RequestTasks()
  SendAddonMessage(PREFIX, "qatasks", "WHISPER", UnitName("player"))
end

local function SetMsg(text, isErr)
  if not D.msg then return end
  D.msg:SetText(text or "")
  -- Тёмные тона — читаются на пергаменте.
  if isErr then D.msg:SetTextColor(0.6, 0.1, 0.05) else D.msg:SetTextColor(0.1, 0.4, 0.1) end
end

local function Submit()
  local t = tasks[selected]
  if not t then return end
  local pass = D.check:GetChecked() and "1" or "0"
  local comment = D.comment:GetText() or ""
  if pass == "0" and comment:gsub("%s", "") == "" then
    SetMsg("Без галки комментарий обязателен", true)
    return
  end
  SendAddonMessage(PREFIX, "qasubmit|" .. t.id .. "|" .. pass .. "|" .. comment, "WHISPER", UnitName("player"))
  SetMsg("Отправка…", false)
end

-- ---- Разбор кадров от сервера ----
local function RemoveTaskById(id)
  for i, t in ipairs(tasks) do
    if t.id == id then table.remove(tasks, i); break end
  end
  if selected and not tasks[selected] then selected = nil end
end

local function HandleLine(line)
  if line == "QBEGIN" then
    building = {}
  elseif line == "QEND" then
    if building then
      tasks = building; building = nil
      selected = (#tasks > 0) and 1 or nil -- по умолчанию выбран первый таск
      if mainFrame then ListRefresh(); ShowDetail() end
    end
  elseif building and string.sub(line, 1, 2) == "Q|" then
    local _, id, title, steps, expected = strsplit("|", line)
    id = tonumber(id)
    if id then
      building[#building + 1] = { id = id, title = title or ("#" .. id), steps = steps or "", expected = expected or "" }
    end
  elseif string.sub(line, 1, 6) == "QDONE|" then
    local _, id, status = strsplit("|", line)
    id = tonumber(id)
    if id then RemoveTaskById(id) end
    if mainFrame then ListRefresh(); ShowDetail() end
    SetMsg("Готово: задача → " .. (status or "?"), false)
  elseif string.sub(line, 1, 5) == "QERR|" then
    local _, _, msg = strsplit("|", line)
    SetMsg(msg or "Ошибка", true)
  end
end

-- ---- Список (левая панель) ----
ListRefresh = function()
  FauxScrollFrame_Update(listScroll, #tasks, LIST_NUM_ROWS, LIST_ROW_H)
  local offset = FauxScrollFrame_GetOffset(listScroll)
  for i = 1, LIST_NUM_ROWS do
    local row = listRows[i]
    local t = tasks[i + offset]
    if t then
      row.index = i + offset
      row.text:SetText(t.title)
      if row.index == selected then
        row.sel:Show()
      else
        row.sel:Hide()
      end
      row:Show()
    else
      row.index = nil
      row:Hide()
    end
  end
end

local function OnTaskClick(index)
  if not tasks[index] then return end
  selected = index
  ListRefresh(); ShowDetail()
end

-- ---- Детализация (правая панель) ----
ShowDetail = function()
  local t = tasks[selected]
  if not t then
    D.title:SetText("")
    D.body:SetText("Выберите задачу слева.")
    D.child:SetHeight(D.body:GetStringHeight() + 4)
    D.check:Hide(); D.checkLabel:Hide(); D.commentBg:Hide(); D.comLabel:Hide(); D.doneBtn:Hide()
    SetMsg("", false)
    return
  end
  D.title:SetText(utrunc(t.title, TITLE_MAX)) -- #2: заголовок ≤2 строк (обрезка строкой)
  -- #3: если заголовок обрезан, дублируем его полностью в теле обычным шрифтом.
  local header = ulen(t.title) > TITLE_MAX and (t.title .. "\n\n") or ""
  D.body:SetText(header .. "|cff8a5a00Шаги тестирования:|r\n" .. (t.steps ~= "" and t.steps or "—")
    .. "\n\n|cff8a5a00Ожидаемый результат:|r\n" .. (t.expected ~= "" and t.expected or "—"))
  D.child:SetHeight(D.body:GetStringHeight() + 4)
  D.check:SetChecked(false)
  D.comment:SetText("")
  D.check:Show(); D.checkLabel:Show(); D.commentBg:Show(); D.comLabel:Show(); D.doneBtn:Show()
  SetMsg("", false)
end

-- ---- Окно ----
local function Build()
  local f = CreateFrame("Frame", "AlexQATesterFrame", UIParent)
  f:SetWidth(768); f:SetHeight(512)
  f:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local p, _, rp, x, y = self:GetPoint()
    AlexQATesterDB.pos = { p, rp, x, y }
  end)
  f:SetClampedToScreen(true)
  f:Hide()

  -- Оформление как «Журнал заданий»: двухпанельные текстуры рамки квест-лога (лево 512 + право 256 = 768).
  local texL = f:CreateTexture(nil, "BORDER")
  texL:SetTexture("Interface\\QuestFrame\\UI-QuestLogDualPane-Left")
  texL:SetWidth(512); texL:SetHeight(512); texL:SetPoint("TOPLEFT", 0, 0)
  local texR = f:CreateTexture(nil, "BORDER")
  texR:SetTexture("Interface\\QuestFrame\\UI-QuestLogDualPane-Right")
  texR:SetWidth(256); texR:SetHeight(512); texR:SetPoint("TOPLEFT", texL, "TOPRIGHT", 0, 0)

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  title:SetPoint("TOP", 0, -18); title:SetText("Задачи на тестирование")
  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -36, -14)

  -- ЛЕВО: список задач (FauxScrollFrame).
  listScroll = CreateFrame("ScrollFrame", "AlexQAListScroll", f, "FauxScrollFrameTemplate")
  listScroll:SetPoint("TOPLEFT", 26, -84)
  listScroll:SetWidth(250); listScroll:SetHeight(LIST_NUM_ROWS * LIST_ROW_H)
  listScroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, LIST_ROW_H, ListRefresh)
  end)
  for i = 1, LIST_NUM_ROWS do
    local row = CreateFrame("Button", nil, f)
    row:SetHeight(LIST_ROW_H)
    if i == 1 then row:SetPoint("TOPLEFT", listScroll, "TOPLEFT", 0, 0)
    else row:SetPoint("TOPLEFT", listRows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", listScroll, "RIGHT", 0, 0)
    local sel = row:CreateTexture(nil, "BACKGROUND")
    sel:SetAllPoints(); sel:SetTexture(0.9, 0.75, 0.1, 0.20); sel:Hide()
    row.sel = sel
    local fs = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", 4, 0); fs:SetPoint("RIGHT", -4, 0); fs:SetJustifyH("LEFT")
    fs:SetTextColor(1, 0.82, 0) -- левая панель тёмная (как список квест-лога) → светлый текст
    row.text = fs
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnClick", function(self) OnTaskClick(self.index) end)
    listRows[i] = row
  end

  -- ПРАВО: детализация.
  local rx = 310
  local QFONT = "Fonts\\FRIZQT__.TTF" -- шрифт как в квест-окне; тёмные тона на пергаменте

  D.title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  D.title:SetPoint("TOPLEFT", rx, -82); D.title:SetWidth(415); D.title:SetJustifyH("LEFT")
  D.title:SetFont(QFONT, 17); D.title:SetTextColor(0.55, 0.40, 0.13) -- #2: обрезаем строкой (SetMaxLines нет в 3.3.5)

  D.scroll = CreateFrame("ScrollFrame", "AlexQADetailScroll", f, "UIPanelScrollFrameTemplate")
  D.scroll:SetPoint("TOPLEFT", rx, -126); D.scroll:SetWidth(412); D.scroll:SetHeight(108)
  D.child = CreateFrame("Frame", nil, D.scroll); D.child:SetWidth(412); D.child:SetHeight(10)
  D.scroll:SetScrollChild(D.child)
  D.body = D.child:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
  D.body:SetPoint("TOPLEFT", 0, 0); D.body:SetWidth(406); D.body:SetJustifyH("LEFT")
  D.body:SetFont(QFONT, 14); D.body:SetTextColor(0.13, 0.10, 0.06)

  D.check = CreateFrame("CheckButton", nil, f, "UICheckButtonTemplate")
  D.check:SetWidth(24); D.check:SetHeight(24); D.check:SetPoint("TOPLEFT", rx - 2, -246)
  D.checkLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.checkLabel:SetPoint("LEFT", D.check, "RIGHT", 2, 0); D.checkLabel:SetText("Соответствует ожидаемому результату")
  D.checkLabel:SetFont(QFONT, 14); D.checkLabel:SetTextColor(0.13, 0.10, 0.06)

  D.comLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  D.comLabel:SetPoint("TOPLEFT", rx, -278); D.comLabel:SetText("Комментарий:")
  D.comLabel:SetFont(QFONT, 14); D.comLabel:SetTextColor(0.13, 0.10, 0.06)
  D.commentBg = CreateFrame("Frame", nil, f)
  D.commentBg:SetPoint("TOPLEFT", rx, -294); D.commentBg:SetWidth(412); D.commentBg:SetHeight(54)
  D.commentBg:SetBackdrop({
    bgFile = "Interface\\ChatFrame\\ChatFrameBackground",
    edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
    tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 },
  })
  D.commentBg:SetBackdropColor(0, 0, 0, 0.6)
  D.comment = CreateFrame("EditBox", nil, D.commentBg)
  D.comment:SetMultiLine(true); D.comment:SetAutoFocus(false); D.comment:SetFontObject(ChatFontNormal)
  D.comment:SetPoint("TOPLEFT", 6, -6); D.comment:SetPoint("BOTTOMRIGHT", -6, 6)
  D.comment:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  D.doneBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  D.doneBtn:SetWidth(120); D.doneBtn:SetHeight(26)
  D.doneBtn:SetPoint("TOPLEFT", rx + 286, -402) -- правый нижний угол пергамента (фикс. позиция в рамках арта)
  D.doneBtn:SetText("Готово")
  D.doneBtn:SetScript("OnClick", Submit)

  D.msg = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  D.msg:SetPoint("RIGHT", D.doneBtn, "LEFT", -10, 0); D.msg:SetWidth(250); D.msg:SetJustifyH("RIGHT")

  if AlexQATesterDB.pos then
    local p, rp, x, y = unpack(AlexQATesterDB.pos)
    f:ClearAllPoints(); f:SetPoint(p, UIParent, rp, x, y)
  end

  mainFrame = f
end

local function Toggle()
  if not mainFrame then Build() end
  if mainFrame:IsShown() then
    mainFrame:Hide()
  else
    mainFrame:Show()
    ListRefresh(); ShowDetail()
    RequestTasks() -- свежий список при открытии
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
  btn:SetScript("OnClick", Toggle)
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
    GameTooltip:AddLine("Клик — задачи на тестирование", 1, 1, 1)
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
    Build()
    BuildMinimapButton()
  elseif event == "CHAT_MSG_ADDON" then
    local prefix, message = ...
    if prefix == PREFIX and message then HandleLine(message) end
  end
end)

SLASH_ALEXQATESTER1 = "/qa"
SLASH_ALEXQATESTER2 = "/qatest"
SlashCmdList["ALEXQATESTER"] = Toggle

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexQATester|r загружен — кнопка у миникарты или /qa.")
