--[[ AlexDevCmd UI — единое окно 3 колонки (1:2:4) на библиотеке AlexUI.

  Колонки: 1 — корни (Персонаж/Тренер/Враги/Профессия/Реагенты/Рынок/Телепорт/QA),
           2 — ветви (или список городов для «Телепорт» / дерево категорий для «Рынок»),
           3 — рабочее пространство (бесповоротная панель под выбранную ветвь).
  Действия уходят как dev-команды (A.Cmd → SAY). Данные (города/рынок) — addon-кадры (см. Core.lua).
]]

local A = AlexDevCmd
A.UI = A.UI or {}
local U = A.UI
local L = AlexUI

local W, H = 864, 520
local layout = L.Columns(W, H, { 1, 2, 4 })
local C3W = layout.w[3]

local mainFrame, col1, col2, scanTip
local panels = {}            -- [panelKey] = frame (с .refresh)
local currentRoot, currentPanel

-- ─── Вспомогательное ───
local function NewPanel()
  local p = CreateFrame("Frame", nil, mainFrame)
  p:SetPoint("TOPLEFT", layout.x[3], layout.colTop)
  p:SetWidth(C3W); p:SetHeight(layout.height)
  p:Hide()
  return p
end

local function Header(p, text)
  local fs = p:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  fs:SetPoint("TOPLEFT", 4, -8); fs:SetText(text or "")
  return fs
end

local function ShowPanel(key)
  for k, p in pairs(panels) do if k ~= key then p:Hide() end end
  local p = panels[key]
  if p then
    currentPanel = key
    if p.refresh then p.refresh() end
    p:Show()
  end
end

-- ─── Панель: список вариантов + кнопки-действия (манекены / проф-тренеры / станки) ───
-- opts: title, items ({label,..}), actions ({ {text, fn(selectedItem)} }), rowH?
local function BuildActionList(opts)
  local p = NewPanel()
  Header(p, opts.title)
  local nAct = #opts.actions
  local list = L.CreateFauxList({
    parent = p, x = 4, y = -44, w = C3W - 8, h = layout.height - 44 - 40,
    rowH = opts.rowH or 24, numRows = math.floor((layout.height - 44 - 40) / (opts.rowH or 24)),
    onClick = function(i) p.list.selected = i; p.list:Refresh() end,
  })
  p.list = list
  list.data = opts.items
  list.render = function(row, item) row.text:SetText(item.label) end
  local bw = (C3W - 8 - (nAct - 1) * 6) / nAct
  for i, act in ipairs(opts.actions) do
    local b = L.Button(p, act.text, bw, 26, function()
      local sel = p.list.selected and opts.items[p.list.selected]
      act.fn(sel)
    end)
    b:SetPoint("BOTTOMLEFT", 4 + (i - 1) * (bw + 6), 8)
  end
  p.refresh = function() p.list.selected = p.list.selected or 1; p.list:Refresh() end
  return p
end

-- ─── Панель: Тренер (класс) ───
local function BuildTrainer()
  local p = NewPanel()
  local hdr = Header(p, "Тренер")
  local btn = L.Button(p, "Вызвать", 130, 26, function()
    local key = A.PlayerTrainer()
    if key then A.Cmd(".trainer " .. key) end
  end)
  btn:SetPoint("TOPLEFT", 4, -48)
  p.refresh = function()
    local _, name = A.PlayerTrainer()
    hdr:SetText("Тренер: " .. (name or "?"))
  end
  return p
end

-- ─── Панель: Персонаж → Основное (уровень/ресурсы/ловкость) ───
local function BuildCharBasic()
  local p = NewPanel()
  Header(p, "Основное")
  local rows = {
    { key = "level", label = "Уровень", read = function() return UnitLevel("player") end },
    { key = "hp", label = "Здоровье", read = function() return UnitHealth("player") end },
    { key = "mana", label = "Мана", read = function() return UnitPower("player", 0) end },
    { key = "rage", label = "Ярость", read = function()
      local m = UnitPowerMax("player", 1); return (m and m > 0) and math.floor(UnitPower("player", 1) / m * 100) or 0
    end },
    { key = "agi", label = "Ловкость", read = function() local _, e = UnitStat("player", 2); return e end },
  }
  local boxes = {}
  for i, r in ipairs(rows) do
    local lbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    lbl:SetPoint("TOPLEFT", 10, -44 - (i - 1) * 30); lbl:SetWidth(110); lbl:SetJustifyH("LEFT"); lbl:SetText(r.label)
    local box = L.EditBox(p, 100, 20, true); box:SetPoint("LEFT", lbl, "RIGHT", 8, 0)
    boxes[r.key] = box
  end
  local save = L.Button(p, "Сохранить", 130, 26, function()
    local lv = tonumber(boxes.level:GetText())
    if lv and lv ~= UnitLevel("player") then A.Cmd(".level " .. lv) end  -- .level сбрасывает/лечит — шлём только при смене
    for _, k in ipairs({ "hp", "mana", "rage", "agi" }) do
      local v = boxes[k]:GetText()
      if v and v ~= "" then A.Cmd(".setstat " .. k .. " " .. v) end
    end
  end)
  save:SetPoint("TOPLEFT", 10, -44 - 5 * 30 - 4)
  local note = p:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
  note:SetPoint("TOPLEFT", 10, -44 - 5 * 30 - 40); note:SetWidth(C3W - 20); note:SetJustifyH("LEFT")
  note:SetText("Оверрайды живут до смены уровня/экипировки/релога (session-only).")
  p.refresh = function() for _, r in ipairs(rows) do boxes[r.key]:SetText(tostring(r.read() or 0)) end end
  return p
end

-- ─── Панель: Персонаж → Характеристики (группы, session-оверрайды) ───
local STAT_DEFS = {
  { g = "Основные", rows = {
    { "str", "Сила", function() local _, e = UnitStat("player", 1); return e end },
    { "agi", "Ловкость", function() local _, e = UnitStat("player", 2); return e end },
    { "sta", "Выносливость", function() local _, e = UnitStat("player", 3); return e end },
    { "int", "Интеллект", function() local _, e = UnitStat("player", 4); return e end },
    { "spi", "Дух", function() local _, e = UnitStat("player", 5); return e end },
  } },
  { g = "Ближний бой", rows = {
    { "attackpower", "Сила атаки", function() local b, p, n = UnitAttackPower("player"); return (b or 0) + (p or 0) + (n or 0) end },
    { "hitmelee", "Рейт. меткости", function() return GetCombatRating and GetCombatRating(CR_HIT_MELEE or 6) end },
    { "critmelee", "Крит, %", function() return GetCritChance and math.floor(GetCritChance()) end },
    { "wpnmin", "Урон оружия (мин)" }, { "wpnmax", "Урон оружия (макс)" }, { "wpnspeed", "Скорость оружия, мс" },
  } },
  { g = "Дальний бой", rows = {
    { "rangedap", "Сила атаки", function() local b, p, n = UnitRangedAttackPower("player"); return (b or 0) + (p or 0) + (n or 0) end },
  } },
  { g = "Магия", rows = { { "critspell", "Крит заклинаний, %" } } },
  { g = "Защита", rows = {
    { "dodge", "Уклонение, %", function() return GetDodgeChance and math.floor(GetDodgeChance()) end },
    { "parry", "Парирование, %", function() return GetParryChance and math.floor(GetParryChance()) end },
    { "block", "Блок, %", function() return GetBlockChance and math.floor(GetBlockChance()) end },
    { "armor", "Броня" },
  } },
}
local function BuildCharStats()
  local p = NewPanel()
  Header(p, "Характеристики")
  local content = L.CreateContentColumn({ parent = p, x = 4, y = -40, w = C3W - 8, h = layout.height - 40 - 40, childW = C3W - 30 })
  local child, boxes, y = content.child, {}, 6
  for _, grp in ipairs(STAT_DEFS) do
    local gh = child:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    gh:SetPoint("TOPLEFT", 0, -y); gh:SetText("|cffffd100" .. grp.g .. "|r"); y = y + 22
    for _, r in ipairs(grp.rows) do
      local lbl = child:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
      lbl:SetPoint("TOPLEFT", 8, -y); lbl:SetWidth(150); lbl:SetJustifyH("LEFT"); lbl:SetText(r[2])
      local box = L.EditBox(child, 80, 18, true); box:SetPoint("LEFT", lbl, "RIGHT", 8, 0)
      box.read = r[3]; boxes[r[1]] = box; y = y + 24
    end
    y = y + 6
  end
  child:SetHeight(y + 6)
  local save = L.Button(p, "Сохранить", 130, 26, function()
    for k, box in pairs(boxes) do
      local v = box:GetText()
      if v and v ~= "" then A.Cmd(".setstat " .. k .. " " .. v) end
    end
  end)
  save:SetPoint("BOTTOMLEFT", 4, 8)
  p.refresh = function()
    for _, box in pairs(boxes) do
      if box.read then local cur = box.read(); if cur then box:SetText(tostring(cur)) end end
    end
  end
  return p
end

-- ─── Панель: Бафф (наложить по id + список активных с «Снять») ───
local function BuildBuff()
  local p = NewPanel()
  Header(p, "Баффы")
  local lbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  lbl:SetPoint("TOPLEFT", 6, -44); lbl:SetText("ID спелла:")
  local idBox = L.EditBox(p, 90, 20, true); idBox:SetPoint("LEFT", lbl, "RIGHT", 8, 0)
  local applyBtn = L.Button(p, "Наложить", 100, 24, function()
    local id = idBox:GetText()
    if id and id ~= "" then A.Cmd(".buff " .. id); idBox:SetText(""); A.RequestAuras() end
  end)
  applyBtn:SetPoint("LEFT", idBox, "RIGHT", 8, 0)
  idBox:SetScript("OnEnterPressed", function() applyBtn:Click() end)
  local allBtn = L.Button(p, "Снять все", 110, 24, function()
    A.Cmd(".unbuff all")
    for i = #A.auras, 1, -1 do A.auras[i] = nil end  -- оптимистично очищаем (мгновенный отклик)
    p.list:Refresh(); A.RequestAuras()
  end)
  allBtn:SetPoint("TOPLEFT", 6, -76)

  local listLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  listLbl:SetPoint("TOPLEFT", 6, -110); listLbl:SetText("Активные баффы:")
  local list = L.CreateFauxList({
    parent = p, x = 4, y = -130, w = C3W - 8, h = layout.height - 130 - 8, rowH = 22,
    numRows = math.floor((layout.height - 130 - 8) / 22),
  })
  p.list = list
  list.render = function(row, item)
    if not row.icon then
      row.icon = row:CreateTexture(nil, "ARTWORK")
      row.icon:SetWidth(18); row.icon:SetHeight(18); row.icon:SetPoint("LEFT", 2, 0); L.CropIcon(row.icon)
      row.rm = L.Button(row, "Снять", 56, 18)
      row.rm:SetScript("OnClick", function(self)
        A.Cmd(".unbuff " .. self.spellId)
        for i, a in ipairs(A.auras) do if a.spellId == self.spellId then table.remove(A.auras, i); break end end
        p.list:Refresh(); A.RequestAuras()
      end)
      row.rm:SetPoint("RIGHT", -2, 0)
      row.text:ClearAllPoints(); row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0); row.text:SetPoint("RIGHT", row.rm, "LEFT", -4, 0)
    end
    local name, _, icon = GetSpellInfo(item.spellId)
    row.icon:SetTexture(icon or "Interface\\Icons\\INV_Misc_QuestionMark")
    row.text:SetText(name or ("#" .. item.spellId))
    row.rm.spellId = item.spellId
  end
  p.refresh = function() list.data = A.auras; A.RequestAuras(); list:Refresh() end
  return p
end

-- ─── Панель: Враги → Существа ───
local function BuildCreatures()
  local p = NewPanel()
  Header(p, "Существа")
  local sel = { key = A.CREATURE_TYPES[1].key, label = A.CREATURE_TYPES[1].label }

  local dd = CreateFrame("Frame", "AlexDevCreatureDD", p, "UIDropDownMenuTemplate")
  dd:SetPoint("TOPLEFT", -8, -40)
  UIDropDownMenu_SetWidth(dd, 140)
  UIDropDownMenu_Initialize(dd, function()
    for _, t in ipairs(A.CREATURE_TYPES) do
      local info = UIDropDownMenu_CreateInfo()
      info.text = t.label; info.func = function()
        sel.key = t.key; sel.label = t.label; UIDropDownMenu_SetSelectedValue(dd, t.key); UIDropDownMenu_SetText(dd, t.label)
      end
      UIDropDownMenu_AddButton(info)
    end
  end)
  UIDropDownMenu_SetText(dd, sel.label)

  local lvlLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  lvlLbl:SetPoint("TOPLEFT", 6, -74); lvlLbl:SetText("Уровень")
  local lvl = L.EditBox(p, 44, 18, true); lvl:SetPoint("LEFT", lvlLbl, "RIGHT", 8, 0); lvl:SetText("80")
  local cntLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  cntLbl:SetPoint("LEFT", lvl, "RIGHT", 14, 0); cntLbl:SetText("Кол-во")
  local cnt = L.EditBox(p, 44, 18, true); cnt:SetPoint("LEFT", cntLbl, "RIGHT", 8, 0); cnt:SetText("1")

  local list = L.CreateFauxList({
    parent = p, x = 4, y = -104, w = C3W - 8, h = layout.height - 104 - 40,
    rowH = 20, numRows = math.floor((layout.height - 104 - 40) / 20),
  })
  list.data = A.creatureQueue
  list.render = function(row, item) row.text:SetText(string.format("%s — ур.%d ×%d", item.label, item.level, item.count)) end

  local addBtn = L.Button(p, "Добавить", 110, 24, function()
    if A.AddCreature(sel.key, sel.label, lvl:GetText(), cnt:GetText()) then list:Refresh() end
  end)
  addBtn:SetPoint("LEFT", cnt, "RIGHT", 14, 0)
  local runBtn = L.Button(p, "Запуск", 120, 26, function()
    for _, c in ipairs(A.creatureQueue) do A.Cmd(string.format(".spawnenemy %s %d %d", c.key, c.level, c.count)) end
    wipe(A.creatureQueue); list:Refresh()
  end)
  runBtn:SetPoint("BOTTOMLEFT", 4, 8)
  local clrBtn = L.Button(p, "Очистить", 100, 26, function() wipe(A.creatureQueue); list:Refresh() end)
  clrBtn:SetPoint("LEFT", runBtn, "RIGHT", 8, 0)

  p.refresh = function() list:Refresh() end
  return p
end

-- ─── Панель: Реагенты (поиск по id / по имени → список → выдать) ───
local function BuildReagents()
  local p = NewPanel()
  Header(p, "Реагенты")
  local box = L.EditBox(p, 160, 20); box:SetPoint("TOPLEFT", 10, -44)
  local searchBtn = L.Button(p, "Искать", 80, 22)
  searchBtn:SetPoint("LEFT", box, "RIGHT", 8, 0)
  local byName = CreateFrame("CheckButton", nil, p, "UICheckButtonTemplate")
  byName:SetWidth(22); byName:SetHeight(22); byName:SetPoint("TOPLEFT", 6, -70)
  local byNameLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  byNameLbl:SetPoint("LEFT", byName, "RIGHT", 2, 0); byNameLbl:SetText("По названию (иначе по id)")
  local function doSearch()
    local q = box:GetText() or ""
    if byName:GetChecked() then A.RequestMarket("itemsearch||||||1|" .. q)  -- showAll=1, имя
    else A.RequestMarket("itembyid|" .. q) end
  end
  searchBtn:SetScript("OnClick", doSearch)
  box:SetScript("OnEnterPressed", doSearch)

  local cntLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  cntLbl:SetPoint("TOPLEFT", 10, -100); cntLbl:SetText("Количество")
  local cnt = L.EditBox(p, 44, 18, true); cnt:SetPoint("LEFT", cntLbl, "RIGHT", 8, 0); cnt:SetText("1")
  local addBtn = L.Button(p, "Добавить", 100, 22, function()
    local item = p.list.selected and A.marketItems[p.list.selected]
    if item then A.Cmd(".additem " .. item.id .. " " .. (cnt:GetText() or "1")) end
  end)
  addBtn:SetPoint("LEFT", cnt, "RIGHT", 14, 0)

  local list = L.CreateFauxList({
    parent = p, x = 4, y = -128, w = C3W - 8, h = layout.height - 128 - 8, rowH = 22,
    numRows = math.floor((layout.height - 128 - 8) / 22),
    onClick = function(i) p.list.selected = i; p.list:Refresh() end,
  })
  p.list = list
  list.render = function(row, item)
    if not row.icon then
      row.icon = row:CreateTexture(nil, "ARTWORK")
      row.icon:SetWidth(18); row.icon:SetHeight(18); row.icon:SetPoint("LEFT", 2, 0); L.CropIcon(row.icon)
      row.text:ClearAllPoints(); row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0); row.text:SetPoint("RIGHT", -4, 0)
      row:SetScript("OnEnter", function(self)
        if self.itemId then GameTooltip:SetOwner(self, "ANCHOR_RIGHT"); GameTooltip:SetHyperlink("item:" .. self.itemId); GameTooltip:Show() end
      end)
      row:SetScript("OnLeave", function() GameTooltip:Hide() end)
    end
    row.itemId = item.id
    local tex = GetItemIcon(item.id)
    if not tex and scanTip then scanTip:SetHyperlink("item:" .. item.id) end
    row.icon:SetTexture(tex or "Interface\\Icons\\INV_Misc_QuestionMark")
    row.text:SetText(item.name)
  end
  p.refresh = function() list.data = A.marketItems; list:Refresh() end
  return p
end

-- ─── Панель: QA → Тестировщик ───
local function BuildQaTester()
  local p = NewPanel()
  Header(p, "Тестировщик")
  local on = L.Button(p, "Активировать", 150, 26, function() A.Cmd(".tester on") end)
  on:SetPoint("TOPLEFT", 4, -48)
  local off = L.Button(p, "Деактивировать", 150, 26, function() A.Cmd(".tester off") end)
  off:SetPoint("TOPLEFT", 4, -82)
  return p
end

-- ─── Панель: QA → Spell QA ───
local function BuildQaSpell()
  local p = NewPanel()
  Header(p, "Spell QA — захват")
  local defs = {
    { "Начать", ".spelltest start" }, { "Остановить", ".spelltest stop" },
    { "Автопрогон ×5", ".spelltest run" }, { "Статус", ".spelltest status" },
  }
  for i, d in ipairs(defs) do
    local b = L.Button(p, d[1], 160, 26, function() A.Cmd(d[2]) end)
    b:SetPoint("TOPLEFT", 4, -44 - (i - 1) * 32)
  end
  return p
end

-- ─── Панель: Телепорт (детализация выбранного города) ───
local function BuildTeleport()
  local p = NewPanel()
  local hdr = Header(p, "Телепорт")
  local fac = p:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  fac:SetPoint("TOPLEFT", 6, -44)
  local btn = L.Button(p, "Телепортироваться", 180, 26, function()
    local c = A.teleports[col2.selected or 0]
    if c then A.Cmd(".tp " .. c.id) end
  end)
  btn:SetPoint("TOPLEFT", 6, -80)
  p.refresh = function()
    local c = A.teleports[col2.selected or 0]
    if c then
      hdr:SetText(c.name); fac:SetText("Фракция: " .. A.FactionLabel(c.faction)); fac:Show(); btn:Show()
    else
      hdr:SetText("Телепорт"); fac:Hide(); btn:Hide()
    end
  end
  return p
end

-- ─── Панель: Рынок (фильтр + результаты + «Взять»); дерево категорий — в col2 ───
local ITEM_TREE = {
  { label = "Оружие", children = {
    { label = "Всё", class = 2 }, { label = "Мечи", class = 2, sub = { 7, 8 } },
    { label = "Топоры", class = 2, sub = { 0, 1 } }, { label = "Дробящее", class = 2, sub = { 4, 5 } },
    { label = "Кинжалы", class = 2, sub = { 15 } }, { label = "Кистевое", class = 2, sub = { 13 } },
    { label = "Посохи", class = 2, sub = { 10 } }, { label = "Луки", class = 2, sub = { 2 } },
    { label = "Ружья", class = 2, sub = { 3 } }, { label = "Жезлы", class = 2, sub = { 19 } },
  } },
  { label = "Доспехи", children = {
    { label = "Всё", class = 4 }, { label = "Тканевая", class = 4, sub = { 1 } },
    { label = "Кожаная", class = 4, sub = { 2 } }, { label = "Кольчуга", class = 4, sub = { 3 } },
    { label = "Латная", class = 4, sub = { 4 } }, { label = "Щиты", class = 4, sub = { 6 } },
  } },
  { label = "Расходные", children = {
    { label = "Всё", class = 0 }, { label = "Зелья", class = 0, sub = { 1 } },
    { label = "Эликсиры", class = 0, sub = { 2 } }, { label = "Еда", class = 0, sub = { 5 } },
  } },
  { label = "Сумки", class = 1 }, { label = "Самоцветы", class = 3 },
  { label = "Рецепты", class = 9 }, { label = "Разное", class = 15 }, { label = "Задания", class = 12 },
}
local marketExpanded, marketVisible, marketFilter = {}, {}, { class = nil, sub = nil }

local function MarketFlatten()
  wipe(marketVisible)
  for _, root in ipairs(ITEM_TREE) do
    marketVisible[#marketVisible + 1] = { node = root, depth = 0 }
    if root.children and marketExpanded[root] then
      for _, ch in ipairs(root.children) do marketVisible[#marketVisible + 1] = { node = ch, depth = 1 } end
    end
  end
end

local function BuildMarket()
  local p = NewPanel()
  local nameBox = L.EditBox(p, 150, 18); nameBox:SetPoint("TOPLEFT", 10, -8)
  local nameLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  nameLbl:SetPoint("BOTTOMLEFT", nameBox, "TOPLEFT", -2, 2); nameLbl:SetText("Имя")
  local lvlMin = L.EditBox(p, 36, 18, true); lvlMin:SetPoint("LEFT", nameBox, "RIGHT", 16, 0)
  local dash = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall"); dash:SetPoint("LEFT", lvlMin, "RIGHT", 6, 0); dash:SetText("-")
  local lvlMax = L.EditBox(p, 36, 18, true); lvlMax:SetPoint("LEFT", dash, "RIGHT", 8, 0)
  local lvlLbl = p:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  lvlLbl:SetPoint("BOTTOMLEFT", lvlMin, "TOPLEFT", -2, 2); lvlLbl:SetText("Уровни")

  local function query()
    local f = marketFilter
    local cls = f.class ~= nil and tostring(f.class) or ""
    local sub = (f.sub and #f.sub > 0) and table.concat(f.sub, ",") or ""
    A.reagentMode = false
    A.RequestMarket(table.concat({ "itemsearch", cls, sub, lvlMin:GetText() or "", lvlMax:GetText() or "",
      "", "1", nameBox:GetText() or "" }, "|"))
  end
  local searchBtn = L.Button(p, "Поиск", 80, 22, function() query() end)
  searchBtn:SetPoint("LEFT", lvlMax, "RIGHT", 16, 0)
  nameBox:SetScript("OnEnterPressed", function() query() end)
  p.query = query

  local list = L.CreateFauxList({
    parent = p, x = 4, y = -44, w = C3W - 8, h = layout.height - 44 - 36, rowH = 24,
    numRows = math.floor((layout.height - 44 - 36) / 24),
    onClick = function(i) p.list.selected = i; p.list:Refresh() end,
  })
  p.list = list
  list.render = function(row, item)
    if not row.icon then
      row.icon = row:CreateTexture(nil, "ARTWORK")
      row.icon:SetWidth(20); row.icon:SetHeight(20); row.icon:SetPoint("LEFT", 2, 0); L.CropIcon(row.icon)
      row.text:ClearAllPoints(); row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0); row.text:SetPoint("RIGHT", -4, 0)
      row:SetScript("OnEnter", function(self)
        if self.itemId then GameTooltip:SetOwner(self, "ANCHOR_RIGHT"); GameTooltip:SetHyperlink("item:" .. self.itemId); GameTooltip:Show() end
      end)
      row:SetScript("OnLeave", function() GameTooltip:Hide() end)
    end
    row.itemId = item.id
    local tex = GetItemIcon(item.id)
    if not tex and scanTip then scanTip:SetHyperlink("item:" .. item.id) end
    row.icon:SetTexture(tex or "Interface\\Icons\\INV_Misc_QuestionMark")
    row.text:SetText(item.name)
  end
  local takeBtn = L.Button(p, "Взять", 120, 26, function()
    local item = p.list.selected and A.marketItems[p.list.selected]
    if item then A.Cmd(".additem " .. item.id) end
  end)
  takeBtn:SetPoint("BOTTOMLEFT", 4, 6)

  p.refresh = function() list.data = A.marketItems; list:Refresh() end
  return p
end

-- ─── col2: переключение содержимого под корень ───
local function Col2Branches(root)
  col2.data = root.branches; col2.selected = 1
  col2.render = function(row, item) row.text:SetText(item.label) end
  col2.onClick = function(i)
    col2.selected = i; col2:Refresh()
    ShowPanel(root.branches[i].key)
  end
  col2:Refresh()
  ShowPanel(root.branches[1].key)
end

local function Col2Cities()
  col2.data = A.teleports; col2.selected = nil
  col2.render = function(row, item) row.text:SetText(item.name) end
  col2.onClick = function(i) col2.selected = i; col2:Refresh(); ShowPanel("teleport") end
  col2:Refresh()
  ShowPanel("teleport")
end

local function Col2MarketTree()
  MarketFlatten()
  col2.data = marketVisible; col2.selected = nil
  col2.render = function(row, e)
    local n, indent = e.node, string.rep("  ", e.depth)
    if n.children then row.text:SetText(indent .. "|cffffd100" .. (marketExpanded[n] and "- " or "+ ") .. n.label .. "|r")
    else row.text:SetText(indent .. "  " .. n.label) end
  end
  col2.onClick = function(i)
    local e = marketVisible[i]; if not e then return end
    if e.node.children then
      marketExpanded[e.node] = not marketExpanded[e.node]
      MarketFlatten(); col2.data = marketVisible; col2:Refresh()
    else
      marketFilter.class = e.node.class; marketFilter.sub = e.node.sub
      col2.selected = i; col2:Refresh()
      if panels.market and panels.market.query then panels.market.query() end
    end
  end
  col2:Refresh()
  ShowPanel("market")
end

local function Col2Clear() col2.data = {}; col2.selected = nil; col2.onClick = nil; col2:Refresh() end

-- ─── Выбор корня ───
local function SelectRoot(i)
  local root = A.ROOTS[i]
  if not root then return end
  currentRoot = root.key
  col1.selected = i; col1:Refresh()
  if root.branches then Col2Branches(root)
  elseif root.key == "teleport" then Col2Cities()
  elseif root.key == "market" then Col2MarketTree()
  else Col2Clear(); ShowPanel(root.key) end
end

-- ─── Колбэки протокола ───
function U.OnTeleports() if currentRoot == "teleport" then col2:Refresh() end end
function U.OnMarket()
  if currentPanel == "market" and panels.market then panels.market.refresh() end
  if currentPanel == "reagents" and panels.reagents then panels.reagents.refresh() end
end
function U.OnAuras()
  local p = panels["char.buff"]
  if currentPanel == "char.buff" and p then p.list.data = A.auras; p.list:Refresh() end  -- repoint: A.auras — новая таблица после AEND
end

-- ─── Построение ───
function U.Build()
  if mainFrame then return end
  mainFrame = L.CreateWindow("AlexDevCmdFrame", "Dev-команды", W, H)
  mainFrame:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local pnt, _, rp, x, y = self:GetPoint()
    AlexDevCmdDB.pos = { pnt, rp, x, y }
  end)

  col1 = L.CreateFauxList({
    parent = mainFrame, name = "AlexDevCol1", x = layout.x[1], y = layout.colTop,
    w = layout.w[1], h = layout.height, rowH = 26, numRows = math.floor(layout.height / 26),
    onClick = SelectRoot,
  })
  col1.data = A.ROOTS
  col1.render = function(row, item) row.text:SetText(item.label) end

  col2 = L.CreateFauxList({
    parent = mainFrame, name = "AlexDevCol2", x = layout.x[2], y = layout.colTop,
    w = layout.w[2], h = layout.height, rowH = 24, numRows = math.floor(layout.height / 24),
  })

  scanTip = L.CreateScanTooltip("AlexDevScanTooltip")

  -- col3-панели
  panels["char.basic"] = BuildCharBasic()
  panels["char.stats"] = BuildCharStats()
  panels["char.buff"] = BuildBuff()
  panels["trainer"] = BuildTrainer()
  panels["enemies.dummies"] = BuildActionList({
    title = "Манекены", items = A.DUMMIES,
    actions = { { text = "Вызвать", fn = function(s) if s then A.Cmd((".dummy " .. s.arg):gsub("%s+$", "")) end end } },
  })
  panels["enemies.creatures"] = BuildCreatures()
  panels["prof.trainers"] = BuildActionList({
    title = "Тренеры профессий", items = A.PROFS,
    actions = {
      { text = "Призвать", fn = function(s) if s then A.Cmd(".proftrainer " .. s.key) end end },
      { text = "Отпустить всех", fn = function() A.Cmd(".devclean") end },
    },
  })
  panels["prof.stations"] = BuildActionList({
    title = "Станки", items = A.STATIONS,
    actions = {
      { text = "Поставить", fn = function(s) if s then A.Cmd(".craft " .. s.key) end end },
      { text = "Убрать все", fn = function() A.Cmd(".craft off") end },
    },
  })
  panels["reagents"] = BuildReagents()
  panels["market"] = BuildMarket()
  panels["teleport"] = BuildTeleport()
  panels["qa.tester"] = BuildQaTester()
  panels["qa.spell"] = BuildQaSpell()

  L.CreateMinimapButton("AlexDevCmdMinimapButton", {
    icon = "Interface\\Icons\\Spell_Arcane_Blink", db = AlexDevCmdDB, angle = 200,
    onClick = function() U.Toggle() end,
    tooltip = { "Alex Dev", "Клик — пульт dev-команд" },
  })

  if AlexDevCmdDB.pos then
    local pnt, rp, x, y = unpack(AlexDevCmdDB.pos)
    mainFrame:ClearAllPoints(); mainFrame:SetPoint(pnt, UIParent, rp, x, y)
  end

  SelectRoot(1)
end

function U.Toggle()
  if not mainFrame then U.Build() end
  if mainFrame:IsShown() then mainFrame:Hide() else mainFrame:Show(); A.RequestTeleports() end
end
