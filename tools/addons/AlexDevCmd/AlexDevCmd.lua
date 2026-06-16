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

-- §182/§183 Окно «Добавить вещь»: отдельный кадр IBEGIN|I|id|quality|itemlevel|reqlvl|name|IEND.
local itemsFrame
local itemsRows = {}
local itemsList = {}       -- [{ id, quality, ilvl, reqlvl, name }] результат поиска
local itemsBuilding = nil   -- временный список между IBEGIN и IEND
local itemsPage = 1         -- §183: текущая страница выдачи (пагинация Назад/Далее)
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
      itemsPage = 1 -- §183: новые результаты — на первую страницу
      if itemsFrame and ItemsRefresh then ItemsRefresh() end
    end
  elseif itemsBuilding and string.sub(line, 1, 2) == "I|" then
    local _, id, q, il, rl, nm = strsplit("|", line)
    id = tonumber(id)
    if id then
      itemsBuilding[#itemsBuilding + 1] = { id = id, quality = tonumber(q) or 1,
        ilvl = tonumber(il) or 0, reqlvl = tonumber(rl) or 0, name = nm or ("#" .. id) }
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

-- ---- §183 Окно «Добавить вещь» (стиль аукциона: верхняя панель фильтров, слева категории, справа таблица) ----
local ITEMS_ROW_H, ITEMS_NUM_ROWS = 24, 14   -- ITEMS_NUM_ROWS = размер страницы выдачи (пагинация)
local TREE_ROW_H, TREE_NUM_ROWS = 20, 16
local itemsScanTip, itemsTreeScroll
local itemsNameBox, itemsLvlMin, itemsLvlMax, itemsSuitable, itemsQualityDD  -- контролы верхней панели
local itemsTypeFS, itemsClassFS, itemsPageFS                                  -- лейблы
local itemsFilter = { classId = nil, sub = nil, quality = -1, typeLabel = nil }

local itemTreeRows = {}
local itemTreeExpanded = {}   -- [node] = true (категория развёрнута)
local itemTreeVisible = {}    -- плоский DFS-список { node, depth, parent }
local ItemTreeRefresh         -- forward

local QUALITY_HEX = { [0]="ff9d9d9d",[1]="ffffffff",[2]="ff1eff00",[3]="ff0070dd",[4]="ffa335ee",[5]="ffff8000",[6]="ffe6cc80",[7]="ffe6cc80" }

-- Качество для дропдауна (как «Качество» на аукционе): значение = минимальное качество (-1 = любое).
local QUALITY_OPTS = {
  { q = -1, t = "Все" }, { q = 1, t = "Обычное+" }, { q = 2, t = "Необычное+" },
  { q = 3, t = "Редкое+" }, { q = 4, t = "Эпическое+" }, { q = 5, t = "Легендарное" },
}

-- Категории слева (как столбец классов на аукционе). Топ-уровень = item-класс; у оружия/доспеха/расходников
-- есть подклассы (kind="type", class[+sub]); «Мечи»=подклассы 7,8 (1H/2H одним пунктом). Прочие классы —
-- сразу листья (выбирают весь класс). Качество — отдельный дропдаун сверху, не в дереве.
local ITEM_TREE = {
  { label = "Оружие", class = 2, children = {
    { label = "Всё", kind = "type", class = 2 },
    { label = "Мечи", kind = "type", class = 2, sub = {7, 8} },
    { label = "Топоры", kind = "type", class = 2, sub = {0, 1} },
    { label = "Дробящее", kind = "type", class = 2, sub = {4, 5} },
    { label = "Кинжалы", kind = "type", class = 2, sub = {15} },
    { label = "Древковое", kind = "type", class = 2, sub = {6} },
    { label = "Посохи", kind = "type", class = 2, sub = {10} },
    { label = "Кистевое", kind = "type", class = 2, sub = {13} },
    { label = "Луки", kind = "type", class = 2, sub = {2} },
    { label = "Арбалеты", kind = "type", class = 2, sub = {18} },
    { label = "Ружья", kind = "type", class = 2, sub = {3} },
    { label = "Жезлы", kind = "type", class = 2, sub = {19} },
    { label = "Метательное", kind = "type", class = 2, sub = {16} },
  } },
  { label = "Доспехи", class = 4, children = {
    { label = "Всё", kind = "type", class = 4 },
    { label = "Тканевая", kind = "type", class = 4, sub = {1} },
    { label = "Кожаная", kind = "type", class = 4, sub = {2} },
    { label = "Кольчуга", kind = "type", class = 4, sub = {3} },
    { label = "Латная", kind = "type", class = 4, sub = {4} },
    { label = "Щиты", kind = "type", class = 4, sub = {6} },
    { label = "Реликвии", kind = "type", class = 4, sub = {7, 8, 9, 10} },
  } },
  { label = "Сумки", kind = "type", class = 1 },
  { label = "Расходные", class = 0, children = {
    { label = "Всё", kind = "type", class = 0 },
    { label = "Зелья", kind = "type", class = 0, sub = {1} },
    { label = "Эликсиры", kind = "type", class = 0, sub = {2} },
    { label = "Фляги", kind = "type", class = 0, sub = {3} },
    { label = "Свитки", kind = "type", class = 0, sub = {4} },
    { label = "Еда и напитки", kind = "type", class = 0, sub = {5} },
    { label = "Бинты", kind = "type", class = 0, sub = {7} },
  } },
  { label = "Символы", kind = "type", class = 16 },
  { label = "Хозяйственные товары", kind = "type", class = 7 },
  { label = "Рецепты", kind = "type", class = 9 },
  { label = "Самоцветы", kind = "type", class = 3 },
  { label = "Разное", kind = "type", class = 15 },
  { label = "Задания", kind = "type", class = 12 },
}

local function ItemsSendQuery()
  if itemsFrame then itemsFrame.tries = 0 end
  local f = itemsFilter
  -- «Подходящее» (как на аукционе) = фильтровать под класс+уровень персонажа; снято — показать всё.
  local suitable = (itemsSuitable and itemsSuitable:GetChecked()) and true or false
  local showAll = not suitable
  local cls = f.classId ~= nil and tostring(f.classId) or ""
  local sub = (f.sub and #f.sub > 0) and table.concat(f.sub, ",") or ""
  local qual = (f.quality and f.quality >= 0) and tostring(f.quality) or ""
  local lvlMin = itemsLvlMin and itemsLvlMin:GetText() or ""
  local lvlMax = itemsLvlMax and itemsLvlMax:GetText() or ""
  local name = itemsNameBox and itemsNameBox:GetText() or ""
  local body = table.concat({ "itemsearch", cls, sub, lvlMin, lvlMax, qual, showAll and "1" or "", name }, "|")
  SendAddonMessage(PREFIX, body, "WHISPER", UnitName("player"))
end

ItemsRefresh = function()
  local total = #itemsList
  local maxPage = math.max(1, math.ceil(total / ITEMS_NUM_ROWS))
  if itemsPage > maxPage then itemsPage = maxPage end
  if itemsPage < 1 then itemsPage = 1 end
  local offset = (itemsPage - 1) * ITEMS_NUM_ROWS
  if itemsPageFS then itemsPageFS:SetText("Стр. " .. itemsPage .. "/" .. maxPage .. "  (" .. total .. ")") end
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
      row.ilvl:SetText((it.ilvl and it.ilvl > 0) and tostring(it.ilvl) or "—")
      row.lvl:SetText(it.reqlvl > 0 and tostring(it.reqlvl) or "—")
      row:Show()
    else
      row.item = nil
      row:Hide()
    end
  end
  if itemsFrame then itemsFrame.pendingIcons = pending end
end

-- Плоский DFS-список видимых узлов дерева (по развёрнутым).
local function ItemTreeRebuild()
  wipe(itemTreeVisible)
  local function walk(node, depth, parent)
    itemTreeVisible[#itemTreeVisible + 1] = { node = node, depth = depth, parent = parent }
    if node.children and itemTreeExpanded[node] then
      for _, ch in ipairs(node.children) do walk(ch, depth + 1, node) end
    end
  end
  for _, root in ipairs(ITEM_TREE) do walk(root, 0, nil) end
end

local function UpdateFilterLabels()
  if itemsTypeFS then itemsTypeFS:SetText("Тип: " .. (itemsFilter.typeLabel or "любой")) end
end

-- Выбор листа дерева категорий: подставляет тип (класс+подкласс) в верхнюю панель.
local function ItemTreeSelect(entry)
  local node = entry.node
  if node.kind ~= "type" then return end
  itemsFilter.classId = node.class -- nil = любой тип (объём 0,2,4 на сервере)
  itemsFilter.sub = node.sub
  -- Крошки: для листа-подкласса показываем «Категория — Подкласс», для прямого листа — его label.
  itemsFilter.typeLabel = (entry.parent and entry.parent.label)
    and (entry.parent.label .. " — " .. node.label) or node.label
  UpdateFilterLabels()
end

ItemTreeRefresh = function()
  FauxScrollFrame_Update(itemsTreeScroll, #itemTreeVisible, TREE_NUM_ROWS, TREE_ROW_H)
  local offset = FauxScrollFrame_GetOffset(itemsTreeScroll)
  for i = 1, TREE_NUM_ROWS do
    local row = itemTreeRows[i]
    local entry = itemTreeVisible[i + offset]
    if entry then
      row.entry = entry
      local node, indent = entry.node, string.rep("  ", entry.depth)
      if node.children then
        row.text:SetText(indent .. "|cffffd100" .. (itemTreeExpanded[node] and "- " or "+ ") .. node.label .. "|r")
      else
        row.text:SetText(indent .. "  |cffd0d0d0" .. node.label .. "|r")
      end
      row:Show()
    else
      row.entry = nil
      row:Hide()
    end
  end
end

local function ItemTreeRowClick(row)
  local entry = row.entry
  if not entry then return end
  if entry.node.children then
    itemTreeExpanded[entry.node] = not itemTreeExpanded[entry.node]
    ItemTreeRebuild(); ItemTreeRefresh()
  else
    ItemTreeSelect(entry)
  end
end

local function BuildItemsUI()
  local f = CreateFrame("Frame", "AlexDevItemsFrame", UIParent)
  f:SetWidth(720); f:SetHeight(500)
  f:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
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
  title:SetPoint("TOP", 0, -12); title:SetText("Просмотр товаров")
  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)

  local function enterSearch(self) self:ClearFocus(); ItemsSendQuery() end

  -- ===== ВЕРХНЯЯ ПАНЕЛЬ ФИЛЬТРОВ (как на аукционе) =====
  local nameLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  nameLabel:SetPoint("TOPLEFT", 18, -40); nameLabel:SetText("Имя")
  itemsNameBox = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  itemsNameBox:SetWidth(150); itemsNameBox:SetHeight(18); itemsNameBox:SetAutoFocus(false)
  itemsNameBox:SetPoint("LEFT", nameLabel, "RIGHT", 12, 0)
  itemsNameBox:SetScript("OnEnterPressed", enterSearch)

  local lvlLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  lvlLabel:SetPoint("LEFT", itemsNameBox, "RIGHT", 22, 0); lvlLabel:SetText("Уровни")
  itemsLvlMin = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  itemsLvlMin:SetWidth(30); itemsLvlMin:SetHeight(18); itemsLvlMin:SetAutoFocus(false); itemsLvlMin:SetNumeric(true)
  itemsLvlMin:SetPoint("LEFT", lvlLabel, "RIGHT", 12, 0)
  itemsLvlMin:SetScript("OnEnterPressed", enterSearch)
  local dash = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  dash:SetPoint("LEFT", itemsLvlMin, "RIGHT", 4, 0); dash:SetText("–")
  itemsLvlMax = CreateFrame("EditBox", nil, f, "InputBoxTemplate")
  itemsLvlMax:SetWidth(30); itemsLvlMax:SetHeight(18); itemsLvlMax:SetAutoFocus(false); itemsLvlMax:SetNumeric(true)
  itemsLvlMax:SetPoint("LEFT", dash, "RIGHT", 8, 0)
  itemsLvlMax:SetScript("OnEnterPressed", enterSearch)

  local qualLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  qualLabel:SetPoint("LEFT", itemsLvlMax, "RIGHT", 18, 0); qualLabel:SetText("Качество")
  itemsQualityDD = CreateFrame("Frame", "AlexDevItemsQualityDD", f, "UIDropDownMenuTemplate")
  itemsQualityDD:SetPoint("LEFT", qualLabel, "RIGHT", -6, -2)
  UIDropDownMenu_SetWidth(itemsQualityDD, 96)
  local function qualSet(self)
    itemsFilter.quality = self.value
    UIDropDownMenu_SetSelectedValue(itemsQualityDD, self.value)
  end
  UIDropDownMenu_Initialize(itemsQualityDD, function(self, level)
    for _, o in ipairs(QUALITY_OPTS) do
      local info = UIDropDownMenu_CreateInfo()
      info.text = o.t; info.value = o.q; info.func = qualSet; info.checked = (itemsFilter.quality == o.q)
      UIDropDownMenu_AddButton(info, level)
    end
  end)
  UIDropDownMenu_SetSelectedValue(itemsQualityDD, itemsFilter.quality)

  local searchBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  searchBtn:SetWidth(90); searchBtn:SetHeight(22); searchBtn:SetPoint("TOPRIGHT", -16, -38); searchBtn:SetText("Поиск")
  searchBtn:SetScript("OnClick", ItemsSendQuery)

  -- Вторая строка: «Подходящее», выбранный тип, страница и пагинация.
  itemsSuitable = CreateFrame("CheckButton", nil, f, "UICheckButtonTemplate")
  itemsSuitable:SetWidth(22); itemsSuitable:SetHeight(22); itemsSuitable:SetPoint("TOPLEFT", 16, -66)
  itemsSuitable:SetChecked(true) -- по умолчанию показываем подходящее персонажу (класс+уровень)
  local suitLabel = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  suitLabel:SetPoint("LEFT", itemsSuitable, "RIGHT", 2, 0); suitLabel:SetText("Подходящее")
  itemsClassFS = f:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
  itemsClassFS:SetPoint("LEFT", suitLabel, "RIGHT", 16, 0); itemsClassFS:SetText("Класс: ?")
  itemsTypeFS = f:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
  itemsTypeFS:SetPoint("LEFT", itemsClassFS, "RIGHT", 16, 0); itemsTypeFS:SetText("Тип: любой")

  local nextBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  nextBtn:SetWidth(64); nextBtn:SetHeight(20); nextBtn:SetPoint("TOPRIGHT", -16, -64); nextBtn:SetText("Далее")
  nextBtn:SetScript("OnClick", function() itemsPage = itemsPage + 1; ItemsRefresh() end)
  local prevBtn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
  prevBtn:SetWidth(64); prevBtn:SetHeight(20); prevBtn:SetPoint("RIGHT", nextBtn, "LEFT", -6, 0); prevBtn:SetText("Назад")
  prevBtn:SetScript("OnClick", function() itemsPage = itemsPage - 1; ItemsRefresh() end)
  itemsPageFS = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  itemsPageFS:SetPoint("RIGHT", prevBtn, "LEFT", -10, 0); itemsPageFS:SetText("")

  -- Разделитель колонок.
  local sep = f:CreateTexture(nil, "ARTWORK")
  sep:SetTexture(0.4, 0.4, 0.4, 0.6); sep:SetWidth(1)
  sep:SetPoint("TOPLEFT", 174, -90); sep:SetPoint("BOTTOMLEFT", 174, 16)

  -- ===== ЛЕВАЯ КОЛОНКА: категории (классы предметов) =====
  itemsTreeScroll = CreateFrame("ScrollFrame", "AlexDevItemsTreeScroll", f, "FauxScrollFrameTemplate")
  itemsTreeScroll:SetPoint("TOPLEFT", 16, -94)
  itemsTreeScroll:SetWidth(150); itemsTreeScroll:SetHeight(TREE_NUM_ROWS * TREE_ROW_H)
  itemsTreeScroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, TREE_ROW_H, ItemTreeRefresh)
  end)
  for i = 1, TREE_NUM_ROWS do
    local row = CreateFrame("Button", nil, f)
    row:SetHeight(TREE_ROW_H)
    if i == 1 then row:SetPoint("TOPLEFT", itemsTreeScroll, "TOPLEFT", 0, 0)
    else row:SetPoint("TOPLEFT", itemTreeRows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", itemsTreeScroll, "RIGHT", 0, 0)
    local fs = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", 4, 0); fs:SetJustifyH("LEFT"); row.text = fs
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnClick", function(self) ItemTreeRowClick(self) end)
    itemTreeRows[i] = row
  end

  -- ===== ПРАВАЯ КОЛОНКА: таблица результатов (заголовки + строки с пагинацией) =====
  local rx = 184
  local hName = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  hName:SetPoint("TOPLEFT", rx + 32, -94); hName:SetText("Предмет")
  local hIlvl = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  hIlvl:SetPoint("TOPLEFT", rx + 356, -94); hIlvl:SetText("Ур.пред")
  local hReq = f:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  hReq:SetPoint("TOPRIGHT", -20, -94); hReq:SetText("Ур.перс")

  for i = 1, ITEMS_NUM_ROWS do
    local row = CreateFrame("Button", nil, f)
    row:SetHeight(ITEMS_ROW_H)
    if i == 1 then row:SetPoint("TOPLEFT", f, "TOPLEFT", rx, -112)
    else row:SetPoint("TOPLEFT", itemsRows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", f, "RIGHT", -16, 0)
    local icon = row:CreateTexture(nil, "ARTWORK")
    icon:SetWidth(22); icon:SetHeight(22); icon:SetPoint("LEFT", 2, 0); icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)
    row.icon = icon
    local nm = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    nm:SetPoint("LEFT", icon, "RIGHT", 6, 0); nm:SetJustifyH("LEFT"); nm:SetWidth(300); row.name = nm
    local il = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    il:SetPoint("LEFT", row, "LEFT", 356, 0); il:SetWidth(50); il:SetJustifyH("LEFT"); row.ilvl = il
    local lvl = row:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
    lvl:SetPoint("RIGHT", -12, 0); lvl:SetJustifyH("RIGHT"); row.lvl = lvl
    local hl = row:CreateTexture(nil, "HIGHLIGHT")
    hl:SetAllPoints(); hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight"); hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
    row:SetScript("OnEnter", function(self)
      if self.item then
        GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
        GameTooltip:SetHyperlink("item:" .. self.item.id) -- нативный тултип предмета (как в вебе)
        GameTooltip:Show()
      end
    end)
    row:SetScript("OnLeave", function() GameTooltip:Hide() end)
    row:SetScript("OnClick", function(self)
      if self.item then StaticPopup_Show("ALEXDEVCMD_ADDITEM", self.item.name, nil, { id = self.item.id }) end
    end)
    itemsRows[i] = row
  end

  -- Скрытый тултип для прайминга кэша предметов (сервер ответит ITEM_QUERY → иконки станут доступны).
  itemsScanTip = CreateFrame("GameTooltip", "AlexDevItemScanTooltip", nil, "GameTooltipTemplate")
  itemsScanTip:SetOwner(WorldFrame, "ANCHOR_NONE")

  -- Иконки приходят с сервера асинхронно — дорисовываем, пока не подгрузятся (с лимитом попыток).
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

  -- Предзаполнение: класс персонажа (инфо) и верхняя граница уровня = уровень персонажа.
  itemsClassFS:SetText("Класс: " .. (UnitClass("player") or "?"))
  itemsLvlMax:SetText(tostring(UnitLevel("player") or ""))
  UpdateFilterLabels()
  ItemTreeRebuild(); ItemTreeRefresh() -- категории по умолчанию свёрнуты

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
