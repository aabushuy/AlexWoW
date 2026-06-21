--[[ AlexUI — общий стиль и виджеты аддонов AlexWoW (WoW 3.3.5a, Lua 5.1).

  Единый «конструктор» для окон наших аддонов, чтобы вид был согласованным:
    окно с тёмным бэкдропом, расчёт колонок по пропорции, списки на FauxScrollFrame,
    миникнопка вокруг миникарты, скрытый скан-тултип, базовые виджеты (кнопка/поле ввода).

  Подключение: в .toc аддона добавить `## Dependencies: AlexUI` (грузится первым), затем
  использовать глобал AlexUI. Стиль извлечён из AlexQATester; AlexQATester будет переведён на
  AlexUI отдельно.
]]

AlexUI = AlexUI or {}
local U = AlexUI
U.VERSION = 1

-- ─── Константы стиля ───
U.ICON_TEXCOORD = { 0.07, 0.93, 0.07, 0.93 }   -- обрезка рамки иконок
U.COLOR = {
  gold = { 1, 0.82, 0 },        -- текст-акцент (заголовки строк)
  sel  = { 0.9, 0.75, 0.1 },    -- подсветка выбранной строки (альфа задаётся отдельно)
}
U.BACKDROP_DARK = {
  bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background-Dark",
  edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
  tile = true, tileSize = 32, edgeSize = 32,
  insets = { left = 11, right = 12, top = 12, bottom = 11 },
}

function U.CropIcon(tex) tex:SetTexCoord(U.ICON_TEXCOORD[1], U.ICON_TEXCOORD[2], U.ICON_TEXCOORD[3], U.ICON_TEXCOORD[4]) end

-- Подсветка строки при наведении (ADD-блендинг квестового хайлайта).
function U.AddHighlight(btn)
  local hl = btn:CreateTexture(nil, "HIGHLIGHT")
  hl:SetAllPoints()
  hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
  hl:SetBlendMode("ADD"); hl:SetAlpha(0.4)
  return hl
end

-- Фон выделения выбранной строки (золотой, по умолчанию скрыт).
function U.AddSelection(btn, alpha)
  local sel = btn:CreateTexture(nil, "BACKGROUND")
  sel:SetAllPoints()
  sel:SetTexture(U.COLOR.sel[1], U.COLOR.sel[2], U.COLOR.sel[3], alpha or 0.22)
  sel:Hide()
  return sel
end

-- ─── Окно ───
-- Тёмный бэкдроп, заголовок, кнопка закрытия, перетаскивание. Возвращает фрейм (f.titleFs — заголовок).
function U.CreateWindow(name, title, w, h)
  local f = CreateFrame("Frame", name, UIParent)
  f:SetWidth(w); f:SetHeight(h)
  f:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
  f:SetBackdrop(U.BACKDROP_DARK)
  f:SetMovable(true); f:EnableMouse(true); f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", f.StopMovingOrSizing)
  f:SetClampedToScreen(true)
  f:Hide()

  -- Плотная чёрная подложка: DialogBox-фон полупрозрачный, сквозь него просвечивает мир. Кроем непрозрачным.
  local bg = f:CreateTexture(nil, "BACKGROUND")
  bg:SetTexture(0, 0, 0, 1)
  bg:SetPoint("TOPLEFT", 10, -10); bg:SetPoint("BOTTOMRIGHT", -10, 10)

  local t = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  t:SetPoint("TOP", 0, -16); t:SetText(title or "")
  f.titleFs = t

  local close = CreateFrame("Button", nil, f, "UIPanelCloseButton")
  close:SetPoint("TOPRIGHT", -6, -6)
  return f
end

-- ─── Раскладка колонок ───
-- Columns(w, h, {1,2,4}) → { margin, gap, colTop, height, x={..}, w={..} } по пропорции ширины.
function U.Columns(w, h, ratios, opts)
  opts = opts or {}
  local margin = opts.margin or 16
  local top = opts.top or 52
  local bottom = opts.bottom or 14
  local gap = opts.gap or 10
  local inner = w - 2 * margin
  local units = 0
  for _, r in ipairs(ratios) do units = units + r end
  local unit = (inner - (#ratios - 1) * gap) / units
  local x, cw, cx = {}, {}, margin
  for i, r in ipairs(ratios) do
    x[i] = cx; cw[i] = unit * r; cx = cx + cw[i] + gap
  end
  return { margin = margin, gap = gap, colTop = -top, height = h - top - bottom, x = x, w = cw }
end

-- ─── Список на FauxScrollFrame ───
-- opts: parent, name?, x, y(<0), w, h, rowH, numRows, onClick(index), rightPad?, textInset?
-- Возвращает list: задать list.data (массив), list.render(row,item,index), list.selected; вызвать list:Refresh().
-- ВАЖНО: FauxScrollFrame_* обращаются к глобалу "<имя>ScrollBar", поэтому фрейму ОБЯЗАТЕЛЬНО нужно имя —
-- если не передали, генерируем уникальное (иначе FauxScrollFrame_Update падает на конкатенации nil).
local listCounter = 0
function U.CreateFauxList(opts)
  listCounter = listCounter + 1
  local name = opts.name or ("AlexUIFauxList" .. listCounter)
  local list = { rows = {}, rowH = opts.rowH, num = opts.numRows, onClick = opts.onClick }
  local scroll = CreateFrame("ScrollFrame", name, opts.parent, "FauxScrollFrameTemplate")
  scroll:SetPoint("TOPLEFT", opts.x, opts.y); scroll:SetWidth(opts.w); scroll:SetHeight(opts.h)
  list.scroll = scroll

  for i = 1, list.num do
    local row = CreateFrame("Button", nil, opts.parent)
    row:SetHeight(list.rowH)
    if i == 1 then row:SetPoint("TOPLEFT", scroll, "TOPLEFT", 0, 0)
    else row:SetPoint("TOPLEFT", list.rows[i - 1], "BOTTOMLEFT", 0, 0) end
    row:SetPoint("RIGHT", scroll, "RIGHT", opts.rightPad or -2, 0)
    row.sel = U.AddSelection(row)
    local fs = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    fs:SetPoint("LEFT", opts.textInset or 6, 0); fs:SetPoint("RIGHT", -4, 0); fs:SetJustifyH("LEFT")
    fs:SetTextColor(U.COLOR.gold[1], U.COLOR.gold[2], U.COLOR.gold[3])
    row.text = fs
    U.AddHighlight(row)
    row:SetScript("OnClick", function(self) if list.onClick then list.onClick(self.index) end end)
    list.rows[i] = row
  end

  scroll:SetScript("OnVerticalScroll", function(self, offset)
    FauxScrollFrame_OnVerticalScroll(self, offset, list.rowH, function() list:Refresh() end)
  end)

  function list:Refresh()
    local data = self.data or {}
    FauxScrollFrame_Update(self.scroll, #data, self.num, self.rowH)
    local offset = FauxScrollFrame_GetOffset(self.scroll)
    for i = 1, self.num do
      local row = self.rows[i]
      local item = data[i + offset]
      if item then
        row.index = i + offset
        if self.render then self.render(row, item, row.index) else row.text:SetText(tostring(item)) end
        if row.sel then if row.index == self.selected then row.sel:Show() else row.sel:Hide() end end
        row:Show()
      else
        row.index = nil; row:Hide()
      end
    end
  end

  return list
end

-- ─── Колонка-контент (скролл + дочерний фрейм) для рабочего пространства ───
-- opts: parent, name?, x, y(<0), w, h, childW. Возвращает { scroll, child }.
-- ВАЖНО: UIPanelScrollFrameTemplate вешает скроллбар СНАРУЖИ справа (relativePoint TOPRIGHT, x>0). При
-- колонке шириной почти во всю панель он уезжает за край окна — недоступен. Переякориваем бар ВНУТРЬ
-- правого края + вешаем колесо мыши (шаблон сам по контент-области колесо не вешает).
local contentColCounter = 0
function U.CreateContentColumn(opts)
  contentColCounter = contentColCounter + 1
  local name = opts.name or ("AlexUIContentCol" .. contentColCounter)
  local scroll = CreateFrame("ScrollFrame", name, opts.parent, "UIPanelScrollFrameTemplate")
  scroll:SetPoint("TOPLEFT", opts.x, opts.y); scroll:SetWidth(opts.w); scroll:SetHeight(opts.h)
  local bar = _G[name .. "ScrollBar"]
  if bar then
    bar:ClearAllPoints()
    bar:SetPoint("TOPRIGHT", scroll, "TOPRIGHT", -2, -18)
    bar:SetPoint("BOTTOMRIGHT", scroll, "BOTTOMRIGHT", -2, 18)
  end
  local child = CreateFrame("Frame", nil, scroll)
  child:SetWidth(opts.childW or (opts.w - 22)); child:SetHeight(10)
  scroll:SetScrollChild(child)
  scroll:EnableMouseWheel(true)
  scroll:SetScript("OnMouseWheel", function(self, delta)
    local maxScroll = self:GetVerticalScrollRange()
    local new = self:GetVerticalScroll() - delta * 24
    if new < 0 then new = 0 elseif new > maxScroll then new = maxScroll end
    self:SetVerticalScroll(new)
  end)
  return { scroll = scroll, child = child }
end

-- ─── Базовые виджеты ───
function U.Button(parent, text, w, h, onClick)
  local b = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
  b:SetWidth(w); b:SetHeight(h); b:SetText(text)
  if onClick then b:SetScript("OnClick", onClick) end
  return b
end

-- ВАЖНО: InputBoxTemplate рисует рамку дочерними текстурами $parentLeft/$parentMiddle/$parentRight —
-- без имени фрейма $parent не резолвится и поле «разрывает». Поэтому имя ОБЯЗАТЕЛЬНО (генерируем).
local editCounter = 0
function U.EditBox(parent, w, h, numeric)
  editCounter = editCounter + 1
  local e = CreateFrame("EditBox", "AlexUIEditBox" .. editCounter, parent, "InputBoxTemplate")
  e:SetWidth(w); e:SetHeight(h); e:SetAutoFocus(false)
  if numeric then e:SetNumeric(true) end
  e:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  return e
end

-- ─── Скрытый скан-тултип (описание спелла / прайминг кэша предметов) ───
function U.CreateScanTooltip(name)
  local t = CreateFrame("GameTooltip", name, nil, "GameTooltipTemplate")
  t:SetOwner(WorldFrame, "ANCHOR_NONE")
  return t
end

-- ─── Кнопка у миникарты ───
-- opts: icon, db (saved-vars таблица), angleKey?, angle?, onClick, tooltip (массив строк).
function U.CreateMinimapButton(name, opts)
  local btn = CreateFrame("Button", name, Minimap)
  btn:SetWidth(31); btn:SetHeight(31); btn:SetFrameStrata("MEDIUM"); btn:SetFrameLevel(8)
  btn:RegisterForClicks("LeftButtonUp"); btn:RegisterForDrag("LeftButton"); btn:SetMovable(true)

  local icon = btn:CreateTexture(nil, "BACKGROUND")
  icon:SetTexture(opts.icon); icon:SetWidth(20); icon:SetHeight(20); icon:SetPoint("CENTER", 0, 1)
  U.CropIcon(icon)
  local border = btn:CreateTexture(nil, "OVERLAY")
  border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder"); border:SetWidth(53); border:SetHeight(53)
  border:SetPoint("TOPLEFT")

  local db, key = opts.db, opts.angleKey or "minimapAngle"
  local function pos(self)
    local a = math.rad((db and db[key]) or opts.angle or 200)
    self:SetPoint("CENTER", Minimap, "CENTER", 80 * math.cos(a), 80 * math.sin(a))
  end
  btn:SetScript("OnClick", function() if opts.onClick then opts.onClick() end end)
  btn:SetScript("OnDragStart", function(self) self:SetScript("OnUpdate", function(s)
    local mx, my = Minimap:GetCenter()
    local px, py = GetCursorPosition()
    local scale = Minimap:GetEffectiveScale()
    if db then db[key] = math.deg(math.atan2(py / scale - my, px / scale - mx)) end
    pos(s)
  end) end)
  btn:SetScript("OnDragStop", function(self) self:SetScript("OnUpdate", nil) end)
  if opts.tooltip then
    btn:SetScript("OnEnter", function(self)
      GameTooltip:SetOwner(self, "ANCHOR_LEFT")
      for i, l in ipairs(opts.tooltip) do
        if i == 1 then GameTooltip:AddLine(l) else GameTooltip:AddLine(l, 1, 1, 1) end
      end
      GameTooltip:Show()
    end)
    btn:SetScript("OnLeave", function() GameTooltip:Hide() end)
  end
  pos(btn)
  return btn
end
