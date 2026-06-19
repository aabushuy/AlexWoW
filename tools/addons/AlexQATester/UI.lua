--[[ AlexQATester UI (KB14): главное окно AlexQATesterFrame.

  Двухпанельное «как Журнал заданий»: слева FauxScrollFrame со списком, справа UIPanelScrollFrame
  с деталями и контролами сабмита (чекбокс / комментарий / Готово). Окно скрыто после загрузки;
  открывается через AlexQATester.UI.OpenWithKind(kind) — вызывается из MenuPanel по клику на вкладку.
]]

local A = AlexQATester
A.UI = A.UI or {}
local U = A.UI

local LIST_ROW_H, LIST_NUM_ROWS = 22, 14
local mainFrame, listScroll, listRows, titleFs = nil, nil, {}, nil
local D = {}  -- виджеты правой панели (detail)

local function SetMsg(text, isErr)
  if not D.msg then return end
  D.msg:SetText(text or "")
  if isErr then D.msg:SetTextColor(0.6, 0.1, 0.05) else D.msg:SetTextColor(0.1, 0.4, 0.1) end
end

function U.SetMsg(text, isErr) SetMsg(text, isErr) end
function U.IsChecked() return D.check and D.check:GetChecked() end
function U.GetComment() return D.comment and D.comment:GetText() end

-- ---- Список (левая панель) ----
-- Имя/иконку/ранг берём из клиентских DBC через GetSpellInfo — данные тех же spell.dbc, что и у сервера.
local function ResolveName(t)
  if t.spellId then
    local n, rank = GetSpellInfo(t.spellId)
    if n then return (rank and rank ~= "" and (n .. " (" .. rank .. ")")) or n end
  end
  return t.title
end

local function ListRefresh()
  FauxScrollFrame_Update(listScroll, #A.tasks, LIST_NUM_ROWS, LIST_ROW_H, nil, nil, nil, nil, nil, nil, 1)
  local offset = FauxScrollFrame_GetOffset(listScroll)
  for i = 1, LIST_NUM_ROWS do
    local row = listRows[i]
    local t = A.tasks[i + offset]
    if t then
      row.index = i + offset
      if t.spellId then
        local _, _, icon = GetSpellInfo(t.spellId)
        row.icon:SetTexture(icon or "Interface\\Icons\\INV_Misc_QuestionMark"); row.icon:Show()
      else
        row.icon:SetTexture(nil); row.icon:Hide()
      end
      row.text:SetText(ResolveName(t))
      if row.index == A.selected then row.sel:Show() else row.sel:Hide() end
      row:Show()
    else
      row.index = nil
      row:Hide()
    end
  end
end

local function OnTaskClick(index)
  if not A.tasks[index] then return end
  A.selected = index
  ListRefresh(); U.ShowDetail()
end

-- Пояснение для пустых вкладок: что должно тут лежать и почему сейчас пусто.
-- ASCII-only (фонты 3.3.5a не имеют глифов для «ёлочек», тире и стрелок — рисуют '?').
local EMPTY_HINT = {
  general     = "Список пуст. Сюда попадают нерегрессионные задачи на тестирование,\nназначенные на вашего персонажа.",
  abilities   = "Список пуст. Здесь регрессии классовых, расовых и стартовых абилок\n(проект 'Регрессия абилок'), назначенные на вашего персонажа.",
  talents     = "Список пуст. Регрессии талантов пока не созданы\n(будет отдельный проект - план KB14).",
  professions = "Список пуст. Здесь регрессии профессий (проект 'Регрессия профессий'),\nназначенные на вашего персонажа.",
}

-- Названия школ магии по битовой маске spell_template.SchoolMask (синхронно с SpellPreviewService.SchoolName).
local SCHOOL_NAME = {
  [1] = "Физическая", [2] = "Священная", [4] = "Огонь", [8] = "Природа",
  [16] = "Лёд", [32] = "Тень", [64] = "Тайная магия",
}

local function ApplyCard(t)
  if not t.spellId then
    D.card:Hide(); D.card:SetHeight(0)
    return 0
  end
  local name, rank, icon = GetSpellInfo(t.spellId)
  D.cardIcon.spellId = t.spellId
  D.cardIcon.tex:SetTexture(icon or "Interface\\Icons\\INV_Misc_QuestionMark")
  D.cardName:SetText((name or t.title) .. (rank and rank ~= "" and ("  |cff999999" .. rank .. "|r") or ""))
  local sub = "id " .. t.spellId
  if t.school and SCHOOL_NAME[t.school] then sub = sub .. "   " .. SCHOOL_NAME[t.school] end
  D.cardSub:SetText(sub)
  D.wowhead:SetText("https://wotlkdb.com/?spell=" .. t.spellId)
  D.wowhead:SetCursorPosition(0)
  local h = 48 + 6 + 16  -- иконка + отступ + EditBox
  D.card:SetHeight(h); D.card:Show()
  return h + 10  -- + отступ к body
end

-- ---- Детализация (правая панель) ----
function U.ShowDetail()
  local t = A.tasks[A.selected]
  if not t then
    D.title:SetText("")
    D.card:Hide(); D.card:SetHeight(0)
    D.body:SetText(#A.tasks == 0 and (EMPTY_HINT[A.currentKind] or "Список пуст.") or "Выберите задачу слева.")
    D.child:SetHeight(D.body:GetStringHeight() + 4)
    D.check:Hide(); D.checkLabel:Hide(); D.commentBg:Hide(); D.comLabel:Hide(); D.doneBtn:Hide()
    SetMsg("", false)
    return
  end
  D.title:SetText(A.utrunc(t.title, A.TITLE_MAX))
  local cardH = ApplyCard(t)
  local header = A.ulen(t.title) > A.TITLE_MAX and (t.title .. "\n\n") or ""
  D.body:SetText(header .. "|cff8a5a00Шаги тестирования:|r\n" .. (t.steps ~= "" and t.steps or "—")
    .. "\n\n|cff8a5a00Ожидаемый результат:|r\n" .. (t.expected ~= "" and t.expected or "—"))
  -- title + (card если есть) + body + контролы (24 чекбокс + 12 отступ + 16 лейбл + 4 + 80 поле + 10 низ).
  D.child:SetHeight(D.title:GetStringHeight() + 12 + cardH + D.body:GetStringHeight() + 18 + 24 + 12 + 16 + 4 + 80 + 10)
  D.check:SetChecked(false)
  D.comment:SetText("")
  D.check:Show(); D.checkLabel:Show(); D.commentBg:Show(); D.comLabel:Show(); D.doneBtn:Show()
  SetMsg("", false)
end

-- Колбэк протокола: сервер прислал новый список (или удалил тикет после сабмита).
-- Сортировка: для regression-вкладок (есть SchoolMask) — по школе, затем по имени из GetSpellInfo;
-- для general (school = nil) — по title (фолбэк, выглядит стабильнее серверного «по дате»).
function U.OnTasksLoaded()
  table.sort(A.tasks, function(a, b)
    local sa, sb = a.school or 999, b.school or 999
    if sa ~= sb then return sa < sb end
    return ResolveName(a) < ResolveName(b)
  end)
  if A.selected then A.selected = 1 end
  if mainFrame then ListRefresh(); U.ShowDetail() end
end

local function ApplyTitle()
  if not titleFs then return end
  local label = A.KIND_LABEL[A.currentKind] or "Общее"
  titleFs:SetText("Тестирование: " .. label)
end

function U.OpenWithKind(kind)
  A.currentKind = kind or "general"
  A.selected = nil
  if not mainFrame then U.Build() end
  if D.check then D.check:SetChecked(false) end
  if D.comment then D.comment:SetText("") end
  ApplyTitle()
  mainFrame:Show()
  A.RequestTasks(A.currentKind)
end

-- ---- Окно ----
function U.Build()
  if mainFrame then return end
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

  -- Иконка-книга в круглом медальоне сверху-слева (декор).
  local emblem = f:CreateTexture(nil, "BACKGROUND")
  emblem:SetTexture("Interface\\MailFrame\\Mail-Icon")
  emblem:SetWidth(54); emblem:SetHeight(54)
  emblem:SetPoint("TOPLEFT", 10, -8)
  emblem:SetTexCoord(0.07, 0.93, 0.07, 0.93)

  -- Двухпанельные текстуры рамки квест-лога (лево 512 + право 256 = 768).
  local texL = f:CreateTexture(nil, "BORDER")
  texL:SetTexture("Interface\\QuestFrame\\UI-QuestLogDualPane-Left")
  texL:SetWidth(512); texL:SetHeight(512); texL:SetPoint("TOPLEFT", 0, 0)
  local texR = f:CreateTexture(nil, "BORDER")
  texR:SetTexture("Interface\\QuestFrame\\UI-QuestLogDualPane-Right")
  texR:SetWidth(256); texR:SetHeight(512); texR:SetPoint("TOPLEFT", texL, "TOPRIGHT", 0, 0)

  titleFs = f:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  titleFs:SetPoint("TOP", -32, -18); titleFs:SetText("Тестирование")
  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPLEFT", 652, -8)

  -- Кнопка «Меню» — закрыть основное окно и вернуться к панели вкладок. Уехала в нижнюю рамку,
  -- чтобы не накрывать левый список (фикс ручной подгонки координат пользователем).
  local menuBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  menuBtn:SetWidth(100); menuBtn:SetHeight(24); menuBtn:SetPoint("BOTTOMLEFT", 222, 78)
  menuBtn:SetText("Меню")
  menuBtn:SetScript("OnClick", function() f:Hide(); if A.MenuPanel then A.MenuPanel.Show() end end)

  -- Кнопка «Обновить» — перезапрашивает список задач у сервера.
  local refreshBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  refreshBtn:SetWidth(110); refreshBtn:SetHeight(24); refreshBtn:SetPoint("BOTTOMLEFT", 18, 78)
  refreshBtn:SetText("Обновить")
  refreshBtn:SetScript("OnClick", function() A.RequestTasks(A.currentKind) end)

  -- ЛЕВО: список задач (FauxScrollFrame).
  listScroll = CreateFrame("ScrollFrame", "AlexQAListScroll", f, "FauxScrollFrameTemplate")
  listScroll:SetPoint("TOPLEFT", 26, -74)
  listScroll:SetWidth(294); listScroll:SetHeight(LIST_NUM_ROWS * LIST_ROW_H + 28)
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
    -- Иконка спелла слева (только для regression-задач, где сервер прислал spellId).
    local icon = row:CreateTexture(nil, "ARTWORK")
    icon:SetWidth(18); icon:SetHeight(18); icon:SetPoint("LEFT", 2, 0)
    icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)
    row.icon = icon
    local fs = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", icon, "RIGHT", 4, 0); fs:SetPoint("RIGHT", -4, 0); fs:SetJustifyH("LEFT")
    fs:SetTextColor(1, 0.82, 0)
    row.text = fs
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnClick", function(self) OnTaskClick(self.index) end)
    listRows[i] = row
  end

  -- ПРАВО: детализация.
  local rx = 360
  local QFONTHEADER = "Fonts\\MORPHEUS.TTF"
  local QFONT = "Fonts\\FRIZQT__.TTF"

  D.scroll = CreateFrame("ScrollFrame", "AlexQADetailScroll", f, "UIPanelScrollFrameTemplate")
  D.scroll:SetPoint("TOPLEFT", rx, -74); D.scroll:SetWidth(290); D.scroll:SetHeight(336)
  D.child = CreateFrame("Frame", nil, D.scroll); D.child:SetWidth(280); D.child:SetHeight(10)
  D.scroll:SetScrollChild(D.child)

  D.title = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  D.title:SetPoint("TOPLEFT", 0, -10); D.title:SetWidth(280); D.title:SetJustifyH("LEFT")
  D.title:SetFont(QFONTHEADER, 16); D.title:SetTextColor(0.30, 0.20, 0.10)

  -- Spell-карточка: иконка 48×48, имя (ранг), подзаголовок (id · уровень). Hover на иконку →
  -- родной GameTooltip:SetSpellByID — это тот же тултип, что игрок видит в spellbook (скрин 2 ТЗ).
  D.card = CreateFrame("Frame", nil, D.child)
  D.card:SetPoint("TOPLEFT", D.title, "BOTTOMLEFT", 0, -10)
  D.card:SetWidth(280); D.card:SetHeight(0); D.card:Hide()
  D.cardIcon = CreateFrame("Frame", nil, D.card)
  D.cardIcon:SetWidth(48); D.cardIcon:SetHeight(48); D.cardIcon:SetPoint("TOPLEFT", 0, 0)
  D.cardIcon:EnableMouse(true)
  local iconTex = D.cardIcon:CreateTexture(nil, "ARTWORK")
  iconTex:SetAllPoints(); iconTex:SetTexCoord(0.07, 0.93, 0.07, 0.93)
  D.cardIcon.tex = iconTex
  D.cardIcon:SetScript("OnEnter", function(self)
    if not self.spellId then return end
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetSpellByID(self.spellId)
    GameTooltip:Show()
  end)
  D.cardIcon:SetScript("OnLeave", function() GameTooltip:Hide() end)
  D.cardName = D.card:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  D.cardName:SetPoint("TOPLEFT", D.cardIcon, "TOPRIGHT", 8, -2)
  D.cardName:SetPoint("RIGHT", D.card, "RIGHT", 0, 0); D.cardName:SetJustifyH("LEFT")
  D.cardName:SetFont(QFONTHEADER, 14); D.cardName:SetTextColor(0.30, 0.20, 0.10)
  D.cardSub = D.card:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.cardSub:SetPoint("TOPLEFT", D.cardName, "BOTTOMLEFT", 0, -4)
  D.cardSub:SetPoint("RIGHT", D.card, "RIGHT", 0, 0); D.cardSub:SetJustifyH("LEFT")
  D.cardSub:SetFont(QFONT, 11); D.cardSub:SetTextColor(0.30, 0.20, 0.10)
  -- Wowhead-URL: readonly EditBox для копирования (в клиенте 3.3.5 нет браузера).
  D.wowhead = CreateFrame("EditBox", nil, D.card)
  D.wowhead:SetPoint("TOPLEFT", D.cardIcon, "BOTTOMLEFT", 0, -6)
  D.wowhead:SetPoint("RIGHT", D.card, "RIGHT", 0, 0); D.wowhead:SetHeight(16)
  D.wowhead:SetAutoFocus(false); D.wowhead:SetFontObject(GameFontNormalSmall)
  D.wowhead:SetTextColor(0.10, 0.30, 0.70)
  D.wowhead:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  D.wowhead:SetScript("OnEnterPressed", function(self) self:ClearFocus() end)

  D.body = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.body:SetPoint("TOPLEFT", D.card, "BOTTOMLEFT", 0, -10); D.body:SetWidth(280); D.body:SetJustifyH("LEFT")
  D.body:SetFont(QFONT, 12); D.body:SetTextColor(0.30, 0.20, 0.10)

  D.check = CreateFrame("CheckButton", nil, D.child, "UICheckButtonTemplate")
  D.check:SetWidth(24); D.check:SetHeight(24)
  D.check:SetPoint("TOPLEFT", D.body, "BOTTOMLEFT", -2, -18)
  D.checkLabel = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.checkLabel:SetPoint("TOPLEFT", D.check, "TOPRIGHT", 4, -4); D.checkLabel:SetWidth(252); D.checkLabel:SetJustifyH("LEFT")
  D.checkLabel:SetText("Соответствует ожидаемому результату")
  D.checkLabel:SetFont(QFONT, 13); D.checkLabel:SetTextColor(0.30, 0.20, 0.10)

  D.comLabel = D.child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  D.comLabel:SetPoint("TOPLEFT", D.check, "BOTTOMLEFT", 2, -12); D.comLabel:SetText("Комментарий:")
  D.comLabel:SetFont(QFONT, 14); D.comLabel:SetTextColor(0.30, 0.20, 0.10)
  D.commentBg = CreateFrame("Frame", nil, D.child)
  D.commentBg:SetPoint("TOPLEFT", D.comLabel, "BOTTOMLEFT", 0, -4); D.commentBg:SetWidth(280); D.commentBg:SetHeight(120)
  D.commentBg:SetBackdrop({
    bgFile = "Interface\\ChatFrame\\ChatFrameBackground",
    edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
    tile = true, tileSize = 16, edgeSize = 12, insets = { left = 3, right = 3, top = 3, bottom = 3 },
  })
  D.commentBg:SetBackdropColor(0, 0, 0, 0.4)
  D.commentScroll = CreateFrame("ScrollFrame", "AlexQACommentScroll", D.commentBg, "UIPanelScrollFrameTemplate")
  D.commentScroll:SetPoint("TOPLEFT", 8, -8); D.commentScroll:SetPoint("BOTTOMRIGHT", -28, 8)
  D.comment = CreateFrame("EditBox", nil, D.commentScroll)
  D.comment:SetMultiLine(true); D.comment:SetAutoFocus(false); D.comment:SetFontObject(ChatFontNormal)
  D.comment:SetWidth(238); D.comment:SetHeight(1)
  D.comment:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  D.comment:SetScript("OnCursorChanged", function(self, x, y, w, h)
    local sf = self:GetParent()
    local height, offset = sf:GetHeight(), sf:GetVerticalScroll()
    if -y < offset then sf:SetVerticalScroll(-y)
    elseif -y + h > offset + height then sf:SetVerticalScroll(-y + h - height) end
  end)
  D.commentScroll:SetScrollChild(D.comment)

  D.doneBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  D.doneBtn:SetWidth(80); D.doneBtn:SetHeight(26)
  D.doneBtn:SetPoint("TOPLEFT", rx + 236, -410)
  D.doneBtn:SetText("Готово")
  D.doneBtn:SetScript("OnClick", A.Submit)

  D.msg = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  D.msg:SetPoint("RIGHT", D.doneBtn, "LEFT", -10, 0); D.msg:SetWidth(250); D.msg:SetJustifyH("RIGHT")

  if AlexQATesterDB.pos then
    local p, rp, x, y = unpack(AlexQATesterDB.pos)
    f:ClearAllPoints(); f:SetPoint(p, UIParent, rp, x, y)
  end

  mainFrame = f
  ApplyTitle()
end

function U.IsShown() return mainFrame and mainFrame:IsShown() end
function U.Hide() if mainFrame then mainFrame:Hide() end end
