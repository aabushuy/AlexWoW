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
  end
end

-- ---- Главное окно ----
local function BuildUI()
  local f = CreateFrame("Frame", "AlexDevCmdFrame", UIParent)
  f:SetWidth(300); f:SetHeight(460)
  f:SetPoint("CENTER")
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
