--[[ AlexQATester UI (KB14, переработка): единое окно из 3 колонок.

  Пропорции ширины колонок 1:2:3:
    Колонка 1 — навигация по типу тикета (Общие / Абилки / Таланты / Профессии).
    Колонка 2 — список тикетов «#<id> - <Title>», сортировка по Title ASC.
    Колонка 3 — детализация выбранного тикета (диспетчер по типу):
      общие         — шаги воспроизведения + ожидаемый результат;
      абилки/таланты — иконка+имя, описание (клиентский тултип spell.dbc) + детали с сервера;
      профессии     — иконка+имя + таблица реагентов (с сервера).
    На всех типах снизу — комментарий + чекбокс «Соответствует ожидаемому результату» + «Готово».
]]

local A = AlexQATester
A.UI = A.UI or {}
local U = A.UI

-- Геометрия окна и колонок.
local W, H = 720, 520
local MARGIN, TOP, BOTTOM, GAP = 16, 52, 14, 10
local INNER = W - 2 * MARGIN                 -- 688
local UNIT = (INNER - 2 * GAP) / 6           -- 1:2:3 → 6 долей
local COL1, COL2, COL3 = UNIT, UNIT * 2, UNIT * 3
local COL_TOP, COL_H = -TOP, H - TOP - BOTTOM
local LIST_ROW_H, LIST_NUM_ROWS = 22, 18
local CHILD_W = COL3 - 28                     -- ширина контента детализации (минус скроллбар)

local mainFrame, listScroll, listRows, navButtons, scanTip
local D = {}                                  -- виджеты детализации

-- Пояснения для пустых вкладок (ASCII — фонты 3.3.5a без «ёлочек»/тире/стрелок рисуют '?').
local EMPTY_HINT = {
  general     = "Список пуст. Сюда попадают нерегрессионные задачи на тестирование,\nназначенные на вашего персонажа.",
  abilities   = "Список пуст. Здесь регрессии классовых, расовых и стартовых абилок,\nназначенные на вашего персонажа.",
  talents     = "Список пуст. Регрессии талантов пока не созданы.",
  professions = "Список пуст. Здесь регрессии профессий,\nназначенные на вашего персонажа.",
}

-- ─── Описание спелла через клиентский тултип (spell.dbc): скан строк скрытого GameTooltip ───
local function ScanSpellDescription(spellId)
  if not scanTip then return "" end
  scanTip:ClearLines()
  scanTip:SetSpellByID(spellId)
  local lines = {}
  for i = 2, scanTip:NumLines() do            -- строка 1 — имя (показываем отдельно)
    local fs = _G["AlexQAScanTooltipTextLeft" .. i]
    local txt = fs and fs:GetText()
    if txt and txt ~= "" then lines[#lines + 1] = txt end
  end
  return table.concat(lines, "\n")
end

-- ─── Сообщение об отправке / контролы сабмита ───
local function SetMsg(text, isErr)
  if not D.msg then return end
  D.msg:SetText(text or "")
  if isErr then D.msg:SetTextColor(1, 0.3, 0.25) else D.msg:SetTextColor(0.4, 1, 0.4) end
end
function U.SetMsg(text, isErr) SetMsg(text, isErr) end
function U.IsChecked() return D.check and D.check:GetChecked() end
function U.GetComment() return D.comment and D.comment:GetText() end

-- ─── Список (колонка 2) ───
local function ListRefresh()
  FauxScrollFrame_Update(listScroll, #A.tasks, LIST_NUM_ROWS, LIST_ROW_H)
  local offset = FauxScrollFrame_GetOffset(listScroll)
  for i = 1, LIST_NUM_ROWS do
    local row = listRows[i]
    local t = A.tasks[i + offset]
    if t then
      row.index = i + offset
      row.text:SetText(A.utrunc(t.title, 36))
      if row.index == A.selected then row.sel:Show() else row.sel:Hide() end
      row:Show()
    else
      row.index = nil
      row:Hide()
    end
  end
end

-- ─── Детализация (колонка 3) ───
local function HideDetailWidgets()
  D.cardIcon:Hide(); D.cardName:Hide(); D.desc:Hide(); D.details:Hide()
  D.reagHeader:Hide()
  for _, r in ipairs(D.reagRows) do r:Hide() end
  D.body:Hide()
  D.check:Hide(); D.checkLabel:Hide(); D.comLabel:Hide(); D.commentBg:Hide(); D.doneBtn:Hide(); D.msg:Hide()
end

local function LayoutSubmit(y)
  D.check:ClearAllPoints(); D.check:SetPoint("TOPLEFT", -2, -y); D.check:Show()
  D.checkLabel:ClearAllPoints(); D.checkLabel:SetPoint("LEFT", D.check, "RIGHT", 2, 0); D.checkLabel:Show()
  y = y + 28
  D.comLabel:ClearAllPoints(); D.comLabel:SetPoint("TOPLEFT", 0, -y); D.comLabel:Show()
  y = y + 18
  D.commentBg:ClearAllPoints(); D.commentBg:SetPoint("TOPLEFT", 0, -y); D.commentBg:Show()
  y = y + 84
  D.doneBtn:ClearAllPoints(); D.doneBtn:SetPoint("TOPLEFT", 0, -y); D.doneBtn:Show()
  D.msg:ClearAllPoints(); D.msg:SetPoint("LEFT", D.doneBtn, "RIGHT", 10, 0); D.msg:Show()
  return y + 32
end

-- Перерисовать таблицу реагентов; вернуть y под ней. Прайминг иконок (кэш предметов) — через скан-тултип.
local function LayoutReagents(reag, y)
  D.reagHeader:ClearAllPoints(); D.reagHeader:SetPoint("TOPLEFT", 0, -y)
  D.reagHeader:SetText(#reag > 0 and "Реагенты:" or "Реагенты: загрузка…"); D.reagHeader:Show()
  y = y + D.reagHeader:GetStringHeight() + 4
  local pending = false
  for i, rg in ipairs(reag) do
    local row = D.reagRows[i]
    if not row then break end
    row:ClearAllPoints(); row:SetPoint("TOPLEFT", 4, -y)
    local icon = GetItemIcon(rg.itemId)
    if not icon then
      if scanTip then scanTip:SetHyperlink("item:" .. rg.itemId) end  -- прайм кэша → SMSG_ITEM_QUERY
      icon = "Interface\\Icons\\INV_Misc_QuestionMark"; pending = true
    end
    row.icon:SetTexture(icon)
    row.text:SetText(string.format("%s  |cffffffffx%d|r  |cff808080(id %d)|r", rg.name, rg.count, rg.itemId))
    row:Show()
    y = y + 22
  end
  if mainFrame then mainFrame.pendingIcons = pending end
  return y
end

function U.ShowDetail(t)
  HideDetailWidgets()
  if not t then
    D.title:SetText("")
    D.body:ClearAllPoints(); D.body:SetPoint("TOPLEFT", 0, -8)
    D.body:SetText(#A.tasks == 0 and (EMPTY_HINT[A.currentKind] or "Список пуст.") or "Выберите задачу слева.")
    D.body:Show()
    D.child:SetHeight(D.body:GetStringHeight() + 16)
    return
  end

  local y = 8
  D.title:ClearAllPoints(); D.title:SetPoint("TOPLEFT", 0, -y)
  D.title:SetText(A.utrunc(t.name or t.title, A.TITLE_MAX)); D.title:Show()
  y = y + D.title:GetStringHeight() + 10

  local kind = A.currentKind
  local spellKind = t.spellId and (kind == "abilities" or kind == "talents" or kind == "professions")
  if spellKind then
    local name, _, icon = GetSpellInfo(t.spellId)
    D.cardIcon:ClearAllPoints(); D.cardIcon:SetPoint("TOPLEFT", 0, -y); D.cardIcon.spellId = t.spellId
    D.cardIcon.tex:SetTexture(icon or "Interface\\Icons\\INV_Misc_QuestionMark"); D.cardIcon:Show()
    D.cardName:ClearAllPoints(); D.cardName:SetPoint("TOPLEFT", D.cardIcon, "TOPRIGHT", 8, -6)
    D.cardName:SetText(name or t.name or ("#" .. t.spellId)); D.cardName:Show()
    y = y + 44

    local det = A.details[t.spellId]
    if kind == "professions" then
      y = LayoutReagents(det and det.reagents or {}, y) + 8
    else
      local desc = ScanSpellDescription(t.spellId)
      if desc ~= "" then
        D.desc:ClearAllPoints(); D.desc:SetPoint("TOPLEFT", 0, -y); D.desc:SetText(desc); D.desc:Show()
        y = y + D.desc:GetStringHeight() + 10
      end
      if det and #det.meta > 0 then
        local parts = {}
        for _, m in ipairs(det.meta) do parts[#parts + 1] = "|cffffd100" .. m.label .. ":|r " .. m.value end
        D.details:ClearAllPoints(); D.details:SetPoint("TOPLEFT", 0, -y)
        D.details:SetText(table.concat(parts, "\n")); D.details:Show()
        y = y + D.details:GetStringHeight() + 10
      end
    end
  else
    local body = "|cffffd100Шаги воспроизведения:|r\n" .. (t.steps ~= "" and t.steps or "—")
      .. "\n\n|cffffd100Ожидаемый результат:|r\n" .. (t.expected ~= "" and t.expected or "—")
    D.body:ClearAllPoints(); D.body:SetPoint("TOPLEFT", 0, -y); D.body:SetText(body); D.body:Show()
    y = y + D.body:GetStringHeight() + 12
  end

  D.check:SetChecked(false)
  D.comment:SetText("")
  y = LayoutSubmit(y)
  SetMsg("", false)
  D.child:SetHeight(y + 10)
end

-- ─── Колбэки протокола ───
function U.OnTasksLoaded()
  if not mainFrame then return end
  ListRefresh()
  U.ShowDetail(A.tasks[A.selected])
end

function U.OnDetailLoaded(spellId)
  local t = A.tasks[A.selected]
  if t and t.spellId == spellId then U.ShowDetail(t) end
end

-- ─── Выбор группы / тикета ───
local function SelectKind(kind)
  A.currentKind = kind
  A.selected = nil
  for _, b in ipairs(navButtons) do
    if b.kind == kind then b.sel:Show() else b.sel:Hide() end
  end
  A.RequestTasks(kind)
  U.ShowDetail(nil)
end

local function SelectTask(index)
  if not A.tasks[index] then return end
  A.selected = index
  local t = A.tasks[index]
  if t.spellId then A.RequestSpellDetail(t.spellId) end
  ListRefresh()
  U.ShowDetail(t)
end

-- ─── Построение окна ───
function U.Build()
  if mainFrame then return end
  listRows, navButtons = {}, {}

  local f = CreateFrame("Frame", "AlexQATesterFrame", UIParent)
  f:SetWidth(W); f:SetHeight(H)
  f:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background-Dark",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local p, _, rp, x, y = self:GetPoint()
    AlexQATesterDB.pos = { p, rp, x, y }
  end)
  f:SetClampedToScreen(true)
  f:Hide()

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  title:SetPoint("TOP", 0, -16); title:SetText("Тестирование")
  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)

  -- Колонка 1 — навигация по типам.
  for i, item in ipairs(A.KINDS) do
    local b = CreateFrame("Button", nil, f)
    b:SetWidth(COL1); b:SetHeight(26)
    b:SetPoint("TOPLEFT", MARGIN, COL_TOP - (i - 1) * 30)
    b.kind = item.kind
    local sel = b:CreateTexture(nil, "BACKGROUND")
    sel:SetAllPoints(); sel:SetTexture(0.9, 0.75, 0.1, 0.25); sel:Hide(); b.sel = sel
    local hl = b:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    local fs = b:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", 6, 0); fs:SetText(item.label)
    b:SetScript("OnClick", function(self) SelectKind(self.kind) end)
    navButtons[i] = b
  end

  -- Колонка 2 — список тикетов.
  local col2x = MARGIN + COL1 + GAP
  listScroll = CreateFrame("ScrollFrame", "AlexQAListScroll", f, "FauxScrollFrameTemplate")
  listScroll:SetPoint("TOPLEFT", col2x, COL_TOP)
  listScroll:SetWidth(COL2 - 4); listScroll:SetHeight(COL_H)
  listScroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, LIST_ROW_H, ListRefresh)
  end)
  for i = 1, LIST_NUM_ROWS do
    local row = CreateFrame("Button", nil, f)
    row:SetHeight(LIST_ROW_H)
    if i == 1 then row:SetPoint("TOPLEFT", listScroll, "TOPLEFT", 0, 0)
    else row:SetPoint("TOPLEFT", listRows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", listScroll, "RIGHT", -2, 0)
    local sel = row:CreateTexture(nil, "BACKGROUND")
    sel:SetAllPoints(); sel:SetTexture(0.9, 0.75, 0.1, 0.20); sel:Hide(); row.sel = sel
    local fs = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", 4, 0); fs:SetPoint("RIGHT", -4, 0); fs:SetJustifyH("LEFT")
    fs:SetTextColor(1, 0.82, 0); row.text = fs
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnClick", function(self) SelectTask(self.index) end)
    listRows[i] = row
  end

  -- Колонка 3 — детализация (скролл-фрейм с дочерним контентом).
  local col3x = col2x + COL2 + GAP
  D.scroll = CreateFrame("ScrollFrame", "AlexQADetailScroll", f, "UIPanelScrollFrameTemplate")
  D.scroll:SetPoint("TOPLEFT", col3x, COL_TOP); D.scroll:SetWidth(COL3 - 8); D.scroll:SetHeight(COL_H)
  D.child = CreateFrame("Frame", nil, D.scroll); D.child:SetWidth(CHILD_W); D.child:SetHeight(10)
  D.scroll:SetScrollChild(D.child)

  D.title = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  D.title:SetWidth(CHILD_W); D.title:SetJustifyH("LEFT")

  D.cardIcon = CreateFrame("Frame", nil, D.child)
  D.cardIcon:SetWidth(40); D.cardIcon:SetHeight(40); D.cardIcon:EnableMouse(true)
  local iconTex = D.cardIcon:CreateTexture(nil, "ARTWORK")
  iconTex:SetAllPoints(); iconTex:SetTexCoord(0.07, 0.93, 0.07, 0.93); D.cardIcon.tex = iconTex
  D.cardIcon:SetScript("OnEnter", function(self)
    if not self.spellId then return end
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT"); GameTooltip:SetSpellByID(self.spellId); GameTooltip:Show()
  end)
  D.cardIcon:SetScript("OnLeave", function() GameTooltip:Hide() end)
  D.cardName = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  D.cardName:SetWidth(CHILD_W - 48); D.cardName:SetJustifyH("LEFT")

  D.desc = D.child:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  D.desc:SetWidth(CHILD_W); D.desc:SetJustifyH("LEFT")
  D.details = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.details:SetWidth(CHILD_W); D.details:SetJustifyH("LEFT")

  D.reagHeader = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.reagHeader:SetWidth(CHILD_W); D.reagHeader:SetJustifyH("LEFT")
  D.reagRows = {}
  for i = 1, 8 do
    local r = CreateFrame("Frame", nil, D.child); r:SetWidth(CHILD_W); r:SetHeight(20)
    local ic = r:CreateTexture(nil, "ARTWORK")
    ic:SetWidth(18); ic:SetHeight(18); ic:SetPoint("LEFT", 0, 0); ic:SetTexCoord(0.07, 0.93, 0.07, 0.93); r.icon = ic
    local tx = r:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
    tx:SetPoint("LEFT", ic, "RIGHT", 6, 0); tx:SetJustifyH("LEFT"); r.text = tx
    r:Hide(); D.reagRows[i] = r
  end

  D.body = D.child:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  D.body:SetWidth(CHILD_W); D.body:SetJustifyH("LEFT")

  D.check = CreateFrame("CheckButton", nil, D.child, "UICheckButtonTemplate")
  D.check:SetWidth(24); D.check:SetHeight(24)
  D.checkLabel = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.checkLabel:SetWidth(CHILD_W - 28); D.checkLabel:SetJustifyH("LEFT")
  D.checkLabel:SetText("Соответствует ожидаемому результату")
  D.comLabel = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.comLabel:SetText("Комментарий:")

  D.commentBg = CreateFrame("Frame", nil, D.child)
  D.commentBg:SetWidth(CHILD_W); D.commentBg:SetHeight(78)
  D.commentBg:SetBackdrop({
    bgFile = "Interface\\ChatFrame\\ChatFrameBackground",
    edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
    tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 },
  })
  D.commentBg:SetBackdropColor(0, 0, 0, 0.5)
  D.comment = CreateFrame("EditBox", nil, D.commentBg)
  D.comment:SetMultiLine(true); D.comment:SetAutoFocus(false); D.comment:SetFontObject(ChatFontNormal)
  D.comment:SetPoint("TOPLEFT", 6, -6); D.comment:SetPoint("BOTTOMRIGHT", -6, 6)
  D.comment:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  D.doneBtn = CreateFrame("Button", nil, D.child, "UIPanelButtonTemplate")
  D.doneBtn:SetWidth(90); D.doneBtn:SetHeight(24); D.doneBtn:SetText("Готово")
  D.doneBtn:SetScript("OnClick", A.Submit)
  D.msg = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  D.msg:SetWidth(CHILD_W - 100); D.msg:SetJustifyH("LEFT")

  -- Скрытый тултип для скана описания спелла и прайминга кэша предметов (иконки реагентов).
  scanTip = CreateFrame("GameTooltip", "AlexQAScanTooltip", nil, "GameTooltipTemplate")
  scanTip:SetOwner(WorldFrame, "ANCHOR_NONE")

  -- Иконки реагентов приходят асинхронно — дорисовываем, пока не подгрузятся (с лимитом попыток).
  f:SetScript("OnUpdate", function(self, elapsed)
    self.acc = (self.acc or 0) + elapsed
    if self.acc < 0.3 then return end
    self.acc = 0
    if self.pendingIcons then
      self.tries = (self.tries or 0) + 1
      if self.tries > 20 then self.pendingIcons = false
      else U.ShowDetail(A.tasks[A.selected]) end
    end
  end)

  if AlexQATesterDB.pos then
    local p, rp, x, y = unpack(AlexQATesterDB.pos)
    f:ClearAllPoints(); f:SetPoint(p, UIParent, rp, x, y)
  end

  mainFrame = f
end

function U.IsShown() return mainFrame and mainFrame:IsShown() end
function U.Hide() if mainFrame then mainFrame:Hide() end end

function U.Toggle()
  if not mainFrame then U.Build() end
  if mainFrame:IsShown() then
    mainFrame:Hide()
  else
    mainFrame:Show()
    SelectKind(A.currentKind or "general")
  end
end
