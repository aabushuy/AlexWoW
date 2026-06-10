--[[ Alex Dev Commands — клиентский пульт dev-команд для AlexWoW (WoW 3.3.5a).
     Команды отправляются как обычный SAY-чат (".trainer mage" и т.п.); сервер ловит сообщения на "."
     (DevCommands.TryHandleAsync) и НЕ эхоит их в чат. Гейт is_admin — на сервере, аддон ничего не обходит.
     Хардкод-каталог (дерево). Окно ввода — для свободных значений (.level / .additem / .learn / .buff). ]]

local ADDON = "AlexDevCmd"
local ROW_HEIGHT, NUM_ROWS = 20, 18

-- Каталог команд (категория → листья). Лист: cmd = готовая команда, либо prompt = {prefix,label} для ввода.
local TREE = {
  { text = "Тренеры классов", children = {
      { text = "Воин",            cmd = ".trainer warrior" },
      { text = "Паладин",         cmd = ".trainer paladin" },
      { text = "Охотник",         cmd = ".trainer hunter" },
      { text = "Разбойник",       cmd = ".trainer rogue" },
      { text = "Жрец",            cmd = ".trainer priest" },
      { text = "Рыцарь смерти",   cmd = ".trainer dk" },
      { text = "Шаман",           cmd = ".trainer shaman" },
      { text = "Маг",             cmd = ".trainer mage" },
      { text = "Чернокнижник",    cmd = ".trainer warlock" },
      { text = "Друид",           cmd = ".trainer druid" },
      { text = "Снять",           cmd = ".trainer off" },
  }},
  { text = "Тренеры профессий", children = {
      { text = "Портняжное",      cmd = ".proftrainer tailoring" },
      { text = "Кузнечное",       cmd = ".proftrainer blacksmithing" },
      { text = "Кожевничество",   cmd = ".proftrainer leatherworking" },
      { text = "Алхимия",         cmd = ".proftrainer alchemy" },
      { text = "Наложение чар",   cmd = ".proftrainer enchanting" },
      { text = "Инженерное",      cmd = ".proftrainer engineering" },
      { text = "Ювелирное",       cmd = ".proftrainer jewelcrafting" },
      { text = "Горное дело",     cmd = ".proftrainer mining" },
      { text = "Травничество",    cmd = ".proftrainer herbalism" },
      { text = "Снятие шкур",     cmd = ".proftrainer skinning" },
      { text = "Кулинария",       cmd = ".proftrainer cooking" },
      { text = "Первая помощь",   cmd = ".proftrainer firstaid" },
      { text = "Рыбная ловля",    cmd = ".proftrainer fishing" },
      { text = "Снять",           cmd = ".proftrainer off" },
  }},
  { text = "Крафт-станки", children = {
      { text = "Наковальня",      cmd = ".craft anvil" },
      { text = "Горн",            cmd = ".craft forge" },
      { text = "Костёр",          cmd = ".craft cookfire" },
      { text = "Почтовый ящик",   cmd = ".craft mailbox" },
      { text = "Убрать все",      cmd = ".craft off" },
  }},
  { text = "Вендор реагентов", children = {
      { text = "Поставить",       cmd = ".reagentvendor" },
      { text = "Снять",           cmd = ".reagentvendor off" },
  }},
  { text = "Персонаж", children = {
      { text = "Уровень…",        prompt = { prefix = ".level ",   label = "Установить уровень (1–80):" } },
      { text = "Опыт…",           prompt = { prefix = ".xp ",      label = "Добавить опыт:" } },
      { text = "Выдать предмет…", prompt = { prefix = ".additem ", label = "ID предмета [кол-во]:" } },
      { text = "Изучить спелл…",  prompt = { prefix = ".learn ",   label = "ID спелла:" } },
      { text = "Выучить всё у тренера", cmd = ".learnall" },
  }},
  { text = "Баффы", children = {
      { text = "Наложить бафф…",  prompt = { prefix = ".buff ",   label = "ID спелла [секунды]:" } },
      { text = "Снять бафф…",     prompt = { prefix = ".unbuff ", label = "ID спелла:" } },
  }},
  { text = "Прочее", children = {
      { text = "Тренировочный манекен", cmd = ".dummy" },
      { text = "Снести dev-сущности",   cmd = ".devclean" },
  }},
}

-- Editbox диалога: в 3.3.5 поле dialog.editBox не всегда задано — фолбэк на именованный глобал.
local function DialogEditBox(dialog)
  return dialog.editBox or _G[dialog:GetName() .. "EditBox"]
end

-- Окно ввода (одно на все команды со свободным аргументом). data = {prefix,label} передаётся в StaticPopup_Show.
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

-- Отправка готовой команды.
local function SendCmd(cmd)
  SendChatMessage(cmd, "SAY")
end

-- ---- Состояние дерева / рендер ----
local expanded = {}     -- [индекс категории] = true (развёрнута)
local visible = {}      -- плоский список видимых строк
local rows = {}         -- кнопки-строки
local mainFrame, scroll

local function Rebuild()
  wipe(visible)
  for ci, cat in ipairs(TREE) do
    visible[#visible + 1] = { isCategory = true, node = cat, ci = ci }
    if expanded[ci] then
      for _, child in ipairs(cat.children) do
        visible[#visible + 1] = { isCategory = false, node = child }
      end
    end
  end
end

local function Refresh()
  FauxScrollFrame_Update(scroll, #visible, NUM_ROWS, ROW_HEIGHT)
  local offset = FauxScrollFrame_GetOffset(scroll)
  for i = 1, NUM_ROWS do
    local b = rows[i]
    local entry = visible[i + offset]
    if entry then
      b.index = i + offset
      if entry.isCategory then
        b.label:SetText("|cffffd100" .. (expanded[entry.ci] and "- " or "+ ") .. entry.node.text .. "|r")
      else
        b.label:SetText("      |cffd0d0d0" .. entry.node.text .. "|r")
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
  if entry.isCategory then
    expanded[entry.ci] = not expanded[entry.ci]
    Rebuild(); Refresh()
  elseif entry.node.cmd then
    SendCmd(entry.node.cmd)
  elseif entry.node.prompt then
    StaticPopup_Show("ALEXDEVCMD_INPUT", entry.node.prompt.label, nil, entry.node.prompt)
  end
end

-- ---- Построение окна ----
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

-- ---- Загрузка ----
local loader = CreateFrame("Frame")
loader:RegisterEvent("ADDON_LOADED")
loader:SetScript("OnEvent", function(self, event, name)
  if name ~= ADDON then return end
  AlexDevCmdDB = AlexDevCmdDB or {}
  BuildUI()
  if AlexDevCmdDB.pos then
    local p, rp, x, y = unpack(AlexDevCmdDB.pos)
    mainFrame:ClearAllPoints()
    mainFrame:SetPoint(p, UIParent, rp, x, y)
  end
  Rebuild(); Refresh()
  self:UnregisterEvent("ADDON_LOADED")
end)

local function Toggle()
  if mainFrame:IsShown() then mainFrame:Hide() else mainFrame:Show(); Refresh() end
end

SLASH_ALEXDEVCMD1 = "/dev"
SLASH_ALEXDEVCMD2 = "/devcmd"
SlashCmdList["ALEXDEVCMD"] = Toggle

DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99AlexDevCmd|r загружен — /dev открывает пульт.")
