--[[ Alex Dev Commands — клиентский пульт dev-команд для AlexWoW (WoW 3.3.5a).
     Каталог меню теперь ОТДАЁТ СЕРВЕР (#79): аддон шлёт addon-сообщение "AlexDev menu" и строит дерево
     из присланных узлов (ничего не хардкодит). Команды-листья отправляются как обычный SAY-чат (".trainer
     mage" и т.п.); сервер ловит "." (DevCommands.TryHandleAsync) и НЕ эхоит. Телепорт-листья открывают
     поп-ап подтверждения и шлют ".tp <id>". Гейт is_admin — на сервере. Открыть/закрыть — кнопка у
     миникарты или /dev. ]]

local ADDON = "AlexDevCmd"
local PREFIX = "AlexDev"
local ROW_HEIGHT, NUM_ROWS = 20, 18

-- ---- Каталог, присланный сервером ----
-- byId[id] = { id, parent, kind, label, payload = {..}, children = {ids..} }
-- kind: "cat" (узел с детьми), "cmd", "prompt", "tp", "info".
local catalog = { byId = {}, order = {}, roots = {} }
local building = nil            -- временный каталог между BEGIN и END
local expanded = {}            -- [id] = true (узел развёрнут)
local visible = {}            -- плоский список видимых строк (DFS по развёрнутым)
local rows = {}
local mainFrame, scroll

-- §179 Редактор вторичных характеристик (Доработка А): отдельный кадр SBEGIN|S|key|label|value|SEND.
local statsFrame
local statsRows = {}
local statsList = {}      -- [{ key, label, value }] в порядке прихода
local statsBuilding = nil  -- временный список между SBEGIN и SEND
local StatsRefresh         -- forward

-- §182 Окно «Добавить вещь»: отдельный кадр IBEGIN|I|id|quality|reqlvl|name|IEND.
local itemsFrame
local itemsRows = {}
local itemsList = {}       -- [{ id, quality, reqlvl, name }] результат поиска
local itemsBuilding = nil   -- временный список между IBEGIN и IEND
local ItemsRefresh          -- forward

-- ---- Диалог ввода (свободный аргумент: .level / .additem / .learn / .buff) ----
local function DialogEditBox(dialog)
  return dialog.editBox or _G[dialog:GetName() .. "EditBox"]
end

StaticPopupDialogs["ALEXDEVCMD_INPUT"] = {
  text = "%s",
  button1 = ACCEPT, button2 = CANCEL,
  hasEditBox = true, maxLetters = 64,
  OnShow = function(self)
    local eb = DialogEditBox(self)
    eb:SetText(""); eb:SetFocus()
  end,
  OnAccept = function(self) AlexDevCmd_SendPrompt(self) end,
  EditBoxOnEnterPressed = function(editBox)
    AlexDevCmd_SendPrompt(editBox:GetParent())
    editBox:GetParent():Hide()
  end,
  EditBoxOnEscapePressed = function(editBox) editBox:GetParent():Hide() end,
  timeout = 0, whileDead = true, hideOnEscape = true,
}

function AlexDevCmd_SendPrompt(dialog)
  local data = dialog.data
  local text = DialogEditBox(dialog):GetText()
  if data and data.prefix and text and text ~= "" then
    SendChatMessage(data.prefix .. text, "SAY")
  end
end

-- ---- Поп-ап подтверждения телепорта (#79) ----
StaticPopupDialogs["ALEXDEVCMD_TELEPORT"] = {
  text = "Телепортироваться в %s?",
  button1 = YES, button2 = NO,
  OnAccept = function(self)
    local data = self.data
    if data and data.cityId then
      SendChatMessage(".tp " .. data.cityId, "SAY")
    end
  end,
  timeout = 0, whileDead = true, hideOnEscape = true,
}

-- ---- Поп-ап подтверждения выдачи предмета (§182) ----
StaticPopupDialogs["ALEXDEVCMD_ADDITEM"] = {
  text = "Добавить «%s» в сумку?",
  button1 = YES, button2 = NO,
  OnAccept = function(self)
    local data = self.data
    if data and data.id then
      SendChatMessage(".additem " .. data.id, "SAY")
    end
  end,
  timeout = 0, whileDead = true, hideOnEscape = true,
}

-- ---- Отправка ----
local function SendCmd(cmd)
  SendChatMessage(cmd, "SAY")
end

local function RequestMenu()
  SendAddonMessage(PREFIX, "menu", "WHISPER", UnitName("player"))
end

-- ---- Разбор каталога от сервера ----
local function CommitCatalog()
  local byId, order = building.byId, building.order
  for _, id in ipairs(order) do byId[id].children = {} end
  local roots = {}
  for _, id in ipairs(order) do
    local n = byId[id]
    if n.parent == 0 then
      table.insert(roots, id)
    elseif byId[n.parent] then
      table.insert(byId[n.parent].children, id)
    end
  end
  catalog = { byId = byId, order = order, roots = roots }
  building = nil
end

-- Строка узла: "N|id|parent|kind|label|payload1|payload2..."
local function ParseNode(line)
  local parts = { strsplit("|", line) }
  if parts[1] ~= "N" then return end
  local id = tonumber(parts[2])
  if not id then return end
  local node = {
    id = id,
    parent = tonumber(parts[3]) or 0,
    kind = parts[4] or "info",
    label = parts[5] or "?",
    payload = {},
  }
  for i = 6, #parts do node.payload[#node.payload + 1] = parts[i] end
  building.byId[id] = node
  table.insert(building.order, id)
end

local Rebuild, Refresh  -- forward

local function HandleAddonLine(line)
  if line == "BEGIN" then
    building = { byId = {}, order = {} }
  elseif line == "END" then
    if building then
      CommitCatalog()
      wipe(expanded)
      if mainFrame then Rebuild(); Refresh() end
    end
  elseif line == "SBEGIN" then          -- §179: начало кадра вторичных характеристик
    statsBuilding = {}
  elseif line == "SEND" then            -- §179: конец кадра — применить
    if statsBuilding then
      statsList = statsBuilding
      statsBuilding = nil
      if statsFrame and StatsRefresh then StatsRefresh() end
    end
  elseif statsBuilding and string.sub(line, 1, 2) == "S|" then
    local _, key, label, value = strsplit("|", line)
    if key then statsBuilding[#statsBuilding + 1] = { key = key, label = label or key, value = value or "" } end
  elseif line == "IBEGIN" then          -- §182: начало кадра результатов поиска предметов
    itemsBuilding = {}
  elseif line == "IEND" then            -- §182: конец кадра — применить
    if itemsBuilding then
      itemsList = itemsBuilding
      itemsBuilding = nil
      if itemsFrame and ItemsRefresh then ItemsRefresh() end
    end
  elseif itemsBuilding and string.sub(line, 1, 2) == "I|" then
    local _, id, q, rl, nm = strsplit("|", line)
    id = tonumber(id)
    if id then
      itemsBuilding[#itemsBuilding + 1] =
        { id = id, quality = tonumber(q) or 1, reqlvl = tonumber(rl) or 0, name = nm or ("#" .. id) }
    end
  elseif building then
    ParseNode(line)
  end
end

-- ---- Состояние дерева / рендер (обобщённая глубина) ----
local function HasKids(node) return node.children and #node.children > 0 end

Rebuild = function()
  wipe(visible)
  local byId = catalog.byId
  local function walk(id, depth)
    local n = byId[id]
    if not n then return end
    visible[#visible + 1] = { id = id, depth = depth, hasKids = HasKids(n) }
    if expanded[id] and HasKids(n) then
      for _, cid in ipairs(n.children) do walk(cid, depth + 1) end
    end
  end
  for _, id in ipairs(catalog.roots) do walk(id, 0) end
end

Refresh = function()
  FauxScrollFrame_Update(scroll, #visible, NUM_ROWS, ROW_HEIGHT)
  local offset = FauxScrollFrame_GetOffset(scroll)
  for i = 1, NUM_ROWS do
    local b = rows[i]
    local entry = visible[i + offset]
    if entry then
      local node = catalog.byId[entry.id]
      b.index = i + offset
      local indent = string.rep("  ", entry.depth)
      if entry.hasKids then
        b.label:SetText(indent .. "|cffffd100" .. (expanded[entry.id] and "- " or "+ ") .. node.label .. "|r")
      else
        b.label:SetText(indent .. "  |cffd0d0d0" .. node.label .. "|r")
      end
      b:Show()
    else
      b:Hide()
    end
  end
end

local function OnRowClick(index)
  local entry = visible[index]
  if not entry then return end
  local node = catalog.byId[entry.id]
  if not node then return end
  if entry.hasKids then
    expanded[entry.id] = not expanded[entry.id]
    Rebuild(); Refresh()
  elseif node.kind == "cmd" then
    SendCmd(node.payload[1])
  elseif node.kind == "prompt" then
    local prefix, hint = node.payload[1], node.payload[2]
    StaticPopup_Show("ALEXDEVCMD_INPUT", hint, nil, { prefix = prefix, label = hint })
  elseif node.kind == "tp" then
    StaticPopup_Show("ALEXDEVCMD_TELEPORT", node.label, nil, { cityId = node.payload[1], cityName = node.label })
  elseif node.kind == "stats" then
    AlexDevCmd_ToggleStats() -- §179: окно редактора вторичных характеристик (Доработка А)
  elseif node.kind == "items" then
    AlexDevCmd_ToggleItems() -- §182: окно «Добавить вещь» (поиск по БД)
  end
end

-- ---- Главное окно ----
local function BuildUI()
  local f = CreateFrame("Frame", "AlexDevCmdFrame", UIParent)
  f:SetWidth(300); f:SetHeight(460)
  -- Дефолт при первом запуске — правый верх (под миникартой); сохранённая позиция (AlexDevCmdDB.pos)
  -- переопределяет это в ADDON_LOADED.
  f:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -16, -200)
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local p, _, rp, x, y = self:GetPoint()
    AlexDevCmdDB.pos = { p, rp, x, y }
  end)
  f:SetClampedToScreen(true)
  f:Hide()

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  title:SetPoint("TOP", 0, -16)
  title:SetText("Dev-команды")

  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)

  scroll = CreateFrame("ScrollFrame", "AlexDevCmdScrollFrame", f, "FauxScrollFrameTemplate")
  scroll:SetPoint("TOPLEFT", 16, -44)
  scroll:SetPoint("BOTTOMRIGHT", -34, 16)
  scroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, ROW_HEIGHT, Refresh)
  end)

  for i = 1, NUM_ROWS do
    local b = CreateFrame("Button", nil, f)
    b:SetHeight(ROW_HEIGHT)
    if i == 1 then
      b:SetPoint("TOPLEFT", scroll, "TOPLEFT", 0, 0)
    else
      b:SetPoint("TOPLEFT", rows[i - 1], "BOTTOMLEFT", 0, 0)
    end
    b:SetPoint("RIGHT", scroll, "RIGHT", 0, 0)
    local fs = b:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", 4, 0); fs:SetJustifyH("LEFT")
    b.label = fs
    local hl = b:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints()
    hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
    hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    b:SetScript("OnClick", function(self) OnRowClick(self.index) end)
    rows[i] = b
  end

  mainFrame = f
end

-- ---- §179 Окно редактора вторичных характеристик (Доработка А) ----
local STATS_ROW_H, STATS_MAX = 26, 16

local function RequestStats()
  SendAddonMessage(PREFIX, "stats", "WHISPER", UnitName("player"))
end

local function StatsApplyRow(row)
  local stat = statsList[row.index]
  if not stat then return end
  local v = row.edit:GetText()
  if v and v ~= "" then
    SendChatMessage(".setstat " .. stat.key .. " " .. v, "SAY") -- сервер пришлёт обновлённый кадр (StatsRefresh)
  end
  row.edit:ClearFocus()
end

StatsRefresh = function()
  for i = 1, STATS_MAX do
    local row, stat = statsRows[i], statsList[i]
    if row and stat then
      row.index = i
      row.label:SetText(stat.label)
      if not row.edit:HasFocus() then row.edit:SetText(stat.value or "") end -- не затирать ввод
      row:Show()
    elseif row then
      row:Hide()
    end
  end
end

local function BuildStatsUI()
  local f = CreateFrame("Frame", "AlexDevStatsFrame", UIParent)
  f:SetWidth(330); f:SetHeight(86 + STATS_MAX * STATS_ROW_H)
  f:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -330, -200)
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local p, _, rp, x, y = self:GetPoint()
    AlexDevCmdDB.statsPos = { p, rp, x, y }
  end)
  f:SetClampedToScreen(true)
  f:Hide()

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  title:SetPoint("TOP", 0, -16)
  title:SetText("Вторичные характеристики")

  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)

  local hint = f:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
  hint:SetPoint("TOP", 0, -40)
  hint:SetText("Enter в поле — применить значение")

  for i = 1, STATS_MAX do
    local row = CreateFrame("Frame", nil, f)
    row:SetHeight(STATS_ROW_H)
    row:SetPoint("TOPLEFT", 18, -(58 + (i - 1) * STATS_ROW_H))
    row:SetPoint("RIGHT", f, "RIGHT", -18, 0)

    local label = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    label:SetPoint("LEFT", 2, 0); label:SetJustifyH("LEFT")
    row.label = label

    local edit = CreateFrame("EditBox", nil, row, "InputBoxTemplate")
    edit:SetWidth(80); edit:SetHeight(20); edit:SetAutoFocus(false)
    edit:SetPoint("RIGHT", -6, 0)
    edit:SetScript("OnEnterPressed", function(self) StatsApplyRow(self:GetParent()) end)
    edit:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
    row.edit = edit

    row:Hide()
    statsRows[i] = row
  end

  if AlexDevCmdDB.statsPos then
    local p, rp, x, y = unpack(AlexDevCmdDB.statsPos)
    f:ClearAllPoints(); f:SetPoint(p, UIParent, rp, x, y)
  end

  statsFrame = f
end

function AlexDevCmd_ToggleStats()
  if not statsFrame then BuildStatsUI() end
  if statsFrame:IsShown() then
    statsFrame:Hide()
  else
    RequestStats()      -- всегда тянем актуальные значения при открытии
    statsFrame:Show()
    StatsRefresh()      -- показать кэш сразу, до прихода свежего кадра
  end
end

-- ---- §182 Окно «Добавить вещь» (поиск по БД, иконки, AH-фильтры) ----
local ITEMS_ROW_H, ITEMS_NUM_ROWS = 30, 10
local itemsScroll, itemsScanTip
local itemsFilter = { kind = "gear", quality = nil, lvlMin = "", lvlMax = "", name = "", showAll = false }
local itemsKindIdx, itemsQualIdx = 1, 1

local ITEM_KINDS = {
  { v = "gear", t = "Экипировка" }, { v = "weapon", t = "Оружие" }, { v = "armor", t = "Доспех" },
  { v = "consumable", t = "Расходники" }, { v = "all", t = "Всё" },
}
local ITEM_QUALITIES = {
  { v = nil, t = "Качество: любое" }, { v = 1, t = "Качество: обычное+" }, { v = 2, t = "Качество: необычное+" },
  { v = 3, t = "Качество: редкое+" }, { v = 4, t = "Качество: эпическое+" },
}
local QUALITY_HEX = { [0]="ff9d9d9d",[1]="ffffffff",[2]="ff1eff00",[3]="ff0070dd",[4]="ffa335ee",[5]="ffff8000",[6]="ffe6cc80",[7]="ffe6cc80" }

local function ItemsSendQuery()
  local f = itemsFilter
  if itemsFrame then itemsFrame.tries = 0 end
  local body = table.concat({
    "itemsearch", f.kind, f.lvlMin or "", f.lvlMax or "", f.quality or "", f.showAll and "1" or "", f.name or "",
  }, "|")
  SendAddonMessage(PREFIX, body, "WHISPER", UnitName("player"))
end

ItemsRefresh = function()
  FauxScrollFrame_Update(itemsScroll, #itemsList, ITEMS_NUM_ROWS, ITEMS_ROW_H)
  local offset = FauxScrollFrame_GetOffset(itemsScroll)
  local pending = false
  for i = 1, ITEMS_NUM_ROWS do
    local row = itemsRows[i]
    local it = itemsList[i + offset]
    if it then
      row.item = it
      local tex = GetItemIcon(it.id)
      if tex then
        row.icon:SetTexture(tex)
      else
        row.icon:SetTexture("Interface\\Icons\\INV_Misc_QuestionMark")
        if itemsScanTip then itemsScanTip:SetHyperlink("item:" .. it.id) end -- прайм кэша → ITEM_QUERY
        pending = true
      end
      row.name:SetText("|c" .. (QUALITY_HEX[it.quality] or "ffffffff") .. it.name .. "|r")
      row.lvl:SetText(it.reqlvl > 0 and ("ур. " .. it.reqlvl) or "")
      row:Show()
    else
      row.item = nil
      row:Hide()
    end
  end
  if itemsFrame then itemsFrame.pendingIcons = pending end
end

local function ItemsDoSearch(box1, box2, nameBox, showAll)
  itemsFilter.lvlMin = box1:GetText()
  itemsFilter.lvlMax = box2:GetText()
  itemsFilter.name = nameBox:GetText()
  itemsFilter.showAll = showAll:GetChecked() and true or false
  ItemsSendQuery()
end

local function BuildItemsUI()
  local f = CreateFrame("Frame", "AlexDevItemsFrame", UIParent)
  f:SetWidth(420); f:SetHeight(470)
  f:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -360, -200)
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local p, _, rp, x, y = self:GetPoint()
    AlexDevCmdDB.itemsPos = { p, rp, x, y }
  end)
  f:SetClampedToScreen(true)
  f:Hide()

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  title:SetPoint("TOP", 0, -14); title:SetText("Добавить вещь")
  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)

  -- Фильтр-кнопки: тип и качество (циклические — клик переключает значение).
  local kindBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  kindBtn:SetWidth(120); kindBtn:SetHeight(22); kindBtn:SetPoint("TOPLEFT", 16, -38)
  kindBtn:SetText(ITEM_KINDS[itemsKindIdx].t)
  kindBtn:SetScript("OnClick", function(self)
    itemsKindIdx = itemsKindIdx % #ITEM_KINDS + 1
    itemsFilter.kind = ITEM_KINDS[itemsKindIdx].v
    self:SetText(ITEM_KINDS[itemsKindIdx].t)
  end)
  local qualBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  qualBtn:SetWidth(170); qualBtn:SetHeight(22); qualBtn:SetPoint("LEFT", kindBtn, "RIGHT", 8, 0)
  qualBtn:SetText(ITEM_QUALITIES[itemsQualIdx].t)
  qualBtn:SetScript("OnClick", function(self)
    itemsQualIdx = itemsQualIdx % #ITEM_QUALITIES + 1
    itemsFilter.quality = ITEM_QUALITIES[itemsQualIdx].v
    self:SetText(ITEM_QUALITIES[itemsQualIdx].t)
  end)

  -- Уровень (мин–макс) и имя.
  local lvlLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  lvlLabel:SetPoint("TOPLEFT", 18, -70); lvlLabel:SetText("Ур.")
  local lvlMin = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  lvlMin:SetWidth(32); lvlMin:SetHeight(18); lvlMin:SetAutoFocus(false); lvlMin:SetNumeric(true)
  lvlMin:SetPoint("LEFT", lvlLabel, "RIGHT", 10, 0)
  local dash = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  dash:SetPoint("LEFT", lvlMin, "RIGHT", 4, 0); dash:SetText("–")
  local lvlMax = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  lvlMax:SetWidth(32); lvlMax:SetHeight(18); lvlMax:SetAutoFocus(false); lvlMax:SetNumeric(true)
  lvlMax:SetPoint("LEFT", dash, "RIGHT", 8, 0)
  local nameLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  nameLabel:SetPoint("LEFT", lvlMax, "RIGHT", 12, 0); nameLabel:SetText("Имя")
  local nameBox = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  nameBox:SetWidth(150); nameBox:SetHeight(18); nameBox:SetAutoFocus(false)
  nameBox:SetPoint("LEFT", nameLabel, "RIGHT", 10, 0)

  -- «Показать всё» + «Искать».
  local showAll = CreateFrame("CheckButton", nil, f, "UICheckButtonTemplate")
  showAll:SetWidth(24); showAll:SetHeight(24); showAll:SetPoint("TOPLEFT", 16, -94)
  local showAllLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  showAllLabel:SetPoint("LEFT", showAll, "RIGHT", 2, 0); showAllLabel:SetText("Показать всё (без фильтра класса/уровня)")
  local searchBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  searchBtn:SetWidth(90); searchBtn:SetHeight(22); searchBtn:SetPoint("TOPRIGHT", -16, -92); searchBtn:SetText("Искать")

  local function doSearch() ItemsDoSearch(lvlMin, lvlMax, nameBox, showAll) end
  searchBtn:SetScript("OnClick", doSearch)
  local function enterSearch(self) self:ClearFocus(); doSearch() end
  lvlMin:SetScript("OnEnterPressed", enterSearch)
  lvlMax:SetScript("OnEnterPressed", enterSearch)
  nameBox:SetScript("OnEnterPressed", enterSearch)

  -- Список результатов.
  itemsScroll = CreateFrame("ScrollFrame", "AlexDevItemsScrollFrame", f, "FauxScrollFrameTemplate")
  itemsScroll:SetPoint("TOPLEFT", 16, -124)
  itemsScroll:SetPoint("BOTTOMRIGHT", -34, 16)
  itemsScroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, ITEMS_ROW_H, ItemsRefresh)
  end)

  for i = 1, ITEMS_NUM_ROWS do
    local row = CreateFrame("Button", nil, f)
    row:SetHeight(ITEMS_ROW_H)
    if i == 1 then row:SetPoint("TOPLEFT", itemsScroll, "TOPLEFT", 0, 0)
    else row:SetPoint("TOPLEFT", itemsRows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", itemsScroll, "RIGHT", 0, 0)
    local icon = row:CreateTexture(nil, "ARTWORK")
    icon:SetWidth(26); icon:SetHeight(26); icon:SetPoint("LEFT", 2, 0); icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)
    row.icon = icon
    local nm = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    nm:SetPoint("LEFT", icon, "RIGHT", 6, 0); nm:SetJustifyH("LEFT"); nm:SetWidth(250); row.name = nm
    local lvl = row:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
    lvl:SetPoint("RIGHT", -6, 0); lvl:SetJustifyH("RIGHT"); row.lvl = lvl
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnClick", function(self)
      if self.item then StaticPopup_Show("ALEXDEVCMD_ADDITEM", self.item.name, nil, { id = self.item.id }) end
    end)
    itemsRows[i] = row
  end

  -- Скрытый тултип для прайминга кэша предметов (сервер ответит ITEM_QUERY → иконки станут доступны).
  itemsScanTip = CreateFrame("GameTooltip", "AlexDevItemScanTooltip", nil, "GameTooltipTemplate")
  itemsScanTip:SetOwner(WorldFrame, "ANCHOR_NONE")

  -- Иконки/имена приходят с сервера асинхронно — дорисовываем, пока не подгрузятся (с лимитом попыток).
  f:SetScript("OnUpdate", function(self, elapsed)
    self.acc = (self.acc or 0) + elapsed
    if self.acc < 0.3 then return end
    self.acc = 0
    if self.pendingIcons then
      self.tries = (self.tries or 0) + 1
      if self.tries > 30 then self.pendingIcons = false else ItemsRefresh() end
    end
  end)

  if AlexDevCmdDB.itemsPos then
    local p, rp, x, y = unpack(AlexDevCmdDB.itemsPos)
    f:ClearAllPoints(); f:SetPoint(p, UIParent, rp, x, y)
  end

  itemsFrame = f
end

function AlexDevCmd_ToggleItems()
  if not itemsFrame then BuildItemsUI() end
  if itemsFrame:IsShown() then
    itemsFrame:Hide()
  else
    itemsFrame:Show()
    ItemsSendQuery() -- сразу искать с текущими фильтрами (по умолчанию — экипировка под класс/уровень)
  end
end

local function Toggle()
  if mainFrame:IsShown() then
    mainFrame:Hide()
  else
    RequestMenu() -- всегда обновляем меню от сервера при открытии (подхватываем новые команды без релога)
    mainFrame:Show(); Rebuild(); Refresh()
  end
end

-- ---- Кнопка у миникарты ----
local minimapButton

local function MinimapUpdatePosition(self)
  local angle = math.rad(AlexDevCmdDB.minimapAngle or 200)
  self:SetPoint("CENTER", Minimap, "CENTER", 80 * math.cos(angle), 80 * math.sin(angle))
end

local function MinimapDragUpdate(self)
  local mx, my = Minimap:GetCenter()
  local scale = Minimap:GetEffectiveScale()
  local px, py = GetCursorPosition()
  px, py = px / scale, py / scale
  AlexDevCmdDB.minimapAngle = math.deg(math.atan2(py - my, px - mx))
  MinimapUpdatePosition(self)
end

local function BuildMinimapButton()
  local btn = CreateFrame("Button", "AlexDevCmdMinimapButton", Minimap)
  btn:SetWidth(31); btn:SetHeight(31)
  btn:SetFrameStrata("MEDIUM"); btn:SetFrameLevel(8)
  btn:RegisterForClicks("LeftButtonUp", "RightButtonUp")
  btn:RegisterForDrag("LeftButton")
  btn:SetMovable(true)

  local icon = btn:CreateTexture(nil, "BACKGROUND")
  icon:SetTexture("Interface\\Icons\\Spell_Arcane_Blink")
  icon:SetWidth(20); icon:SetHeight(20)
  icon:SetPoint("CENTER", 0, 1)
  icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)

  local border = btn:CreateTexture(nil, "OVERLAY")
  border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder")
  border:SetWidth(53); border:SetHeight(53)
  border:SetPoint("TOPLEFT")

  btn:SetScript("OnClick", function() Toggle() end)
  btn:SetScript("OnDragStart", function(self) self:SetScript("OnUpdate", MinimapDragUpdate) end)
  btn:SetScript("OnDragStop", function(self) self:SetScript("OnUpdate", nil) end)
  btn:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_LEFT")
    GameTooltip:AddLine("Alex Dev")
    GameTooltip:AddLine("Клик — пульт dev-команд", 1, 1, 1)
    GameTooltip:AddLine("Перетащить — вокруг миникарты", 0.7, 0.7, 0.7)
    GameTooltip:Show()
  end)
  btn:SetScript("OnLeave", function() GameTooltip:Hide() end)

  minimapButton = btn
  MinimapUpdatePosition(btn)
end

-- ---- Загрузка ----
local loader = CreateFrame("Frame")
loader:RegisterEvent("ADDON_LOADED")
loader:RegisterEvent("PLAYER_ENTERING_WORLD")
loader:RegisterEvent("CHAT_MSG_ADDON")
loader:SetScript("OnEvent", function(self, event, ...)
  if event == "ADDON_LOADED" then
    local name = ...
    if name ~= ADDON then return end
    AlexDevCmdDB = AlexDevCmdDB or {}
    BuildUI()
    BuildMinimapButton()
    if AlexDevCmdDB.pos then
      local p, rp, x, y = unpack(AlexDevCmdDB.pos)
      mainFrame:ClearAllPoints()
      mainFrame:SetPoint(p, UIParent, rp, x, y)
    end
    Rebuild(); Refresh()
  elseif event == "PLAYER_ENTERING_WORLD" then
    RequestMenu() -- каталог отдаёт сервер (#79); пере-запрос после загрузки/телепорта безвреден
  elseif event == "CHAT_MSG_ADDON" then
    local prefix, message = ...
    if prefix == PREFIX and message then HandleAddonLine(message) end
  end
end)

local function ToggleSlash() Toggle() end
SLASH_ALEXDEVCMD1 = "/dev"
SLASH_ALEXDEVCMD2 = "/devcmd"
SlashCmdList["ALEXDEVCMD"] = ToggleSlash

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexDevCmd|r загружен — кнопка у миникарты или /dev открывает пульт.")
