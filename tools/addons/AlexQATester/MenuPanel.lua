--[[ AlexQATester MenuPanel (KB14): вертикальная панель из 4 кнопок выбора вкладки.

  Открывается миникнопкой / `/qa`. Клик по вкладке скрывает панель и вызывает UI.OpenWithKind(kind),
  который запрашивает у сервера список задач нужного типа и показывает главное окно AlexQATesterFrame.
]]

local A = AlexQATester
A.MenuPanel = A.MenuPanel or {}
local M = A.MenuPanel

local PANEL_W, BTN_H, GAP, PAD_TOP, PAD_BOTTOM = 200, 28, 8, 36, 14
local frame

function M.Build()
  if frame then return end
  local f = CreateFrame("Frame", "AlexQATesterMenuFrame", UIParent)
  local height = PAD_TOP + (#A.KINDS * (BTN_H + GAP)) - GAP + PAD_BOTTOM
  f:SetWidth(PANEL_W); f:SetHeight(height)
  f:SetFrameStrata("DIALOG")
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background-Dark",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:EnableMouse(true)
  f:SetMovable(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", f.StopMovingOrSizing)
  f:Hide()

  local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  title:SetPoint("TOP", 0, -16); title:SetText("Тестирование")

  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -4, -4)

  for i, item in ipairs(A.KINDS) do
    local btn = CreateFrame("Button", nil, f, "UIPanelButtonTemplate")
    btn:SetWidth(PANEL_W - 32); btn:SetHeight(BTN_H)
    btn:SetPoint("TOP", 0, -(PAD_TOP + (i - 1) * (BTN_H + GAP)))
    btn:SetText(item.label)
    local kind = item.kind
    btn:SetScript("OnClick", function() M.Hide(); if A.UI then A.UI.OpenWithKind(kind) end end)
  end

  -- Прячемся слева-сверху от миникнопки (или центр экрана, если её ещё нет).
  if AlexQATesterMinimapButton then
    f:SetPoint("TOPRIGHT", AlexQATesterMinimapButton, "BOTTOMLEFT", 0, 0)
  else
    f:SetPoint("CENTER", UIParent, "CENTER", 0, 0)
  end

  frame = f
end

function M.Show() if not frame then M.Build() end frame:Show() end
function M.Hide() if frame then frame:Hide() end end
function M.Toggle()
  if not frame then M.Build() end
  if frame:IsShown() then frame:Hide() else
    if A.UI and A.UI.IsShown() then A.UI.Hide() end
    frame:Show()
  end
end
