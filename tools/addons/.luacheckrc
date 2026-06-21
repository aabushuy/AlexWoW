-- Luacheck-конфиг для клиентских аддонов AlexWoW (WoW 3.3.5a, Lua 5.1).
-- Запуск:  luacheck tools/addons   (из корня репо)
-- Бинарь luacheck.exe ставится отдельно, в репозиторий не коммитится (см. tools/addons/README.md).

std = "lua51"

-- Поверхность клиентского API 3.3.5a, которую реально используют наши аддоны (read-only глобалы).
-- Дополняется по мере роста аддонов: новый используемый API → новая строка здесь, не отключение проверки.
stds.wow = {
  read_globals = {
    -- Базовый API
    "CreateFrame", "SendAddonMessage", "SendChatMessage",
    "UnitName", "UnitClass", "UnitLevel",
    "UnitHealth", "UnitPower", "UnitPowerMax", "UnitStat", "UnitAttackPower", "UnitRangedAttackPower",
    "GetCritChance", "GetDodgeChance", "GetParryChance", "GetBlockChance",
    "GetCombatRating", "CR_HIT_MELEE", "CR_CRIT_TAKEN_SPELL", "GetExpertise", "GetSpellBonusDamage",
    "GetCursorPosition", "GetItemIcon", "GetSpellInfo",
    "strsplit", "wipe",
    -- Глобальные таблицы / фреймы клиента
    "UIParent", "WorldFrame", "Minimap", "DEFAULT_CHAT_FRAME", "GameTooltip",
    "StaticPopup_Show",
    -- FauxScrollFrame-хелперы
    "FauxScrollFrame_Update", "FauxScrollFrame_GetOffset", "FauxScrollFrame_OnVerticalScroll",
    -- Dropdown-хелперы
    "UIDropDownMenu_SetWidth", "UIDropDownMenu_Initialize", "UIDropDownMenu_SetText",
    "UIDropDownMenu_CreateInfo", "UIDropDownMenu_AddButton", "UIDropDownMenu_SetSelectedValue",
    -- FrameXML-константы и шрифт-объекты
    "ACCEPT", "CANCEL", "YES", "NO",
    "GameFontNormalSmall", "ChatFontNormal",
    -- Фреймы, создаваемые по имени в другом файле (читаются здесь)
    "AlexQATesterMinimapButton",
  },
}

std = "lua51+wow"

-- Глобалы, которые наши аддоны намеренно определяют или модифицируют (saved vars, неймспейсы,
-- слэши, попап-колбэки, штатная регистрация в SlashCmdList/StaticPopupDialogs).
globals = {
  "SlashCmdList", "StaticPopupDialogs",
  "AlexUI",  -- общая библиотека стиля (tools/addons/AlexUI), читается/пишется аддонами
  "AlexDevCmd", "AlexDevCmdDB",
  "AlexQATester", "AlexQATesterDB",
  "AlexDevCmdDB",
  "AlexDevCmd_SendPrompt", "AlexDevCmd_ToggleStats", "AlexDevCmd_ToggleItems",
  "SLASH_ALEXQATESTER1", "SLASH_ALEXQATESTER2",
  "SLASH_ALEXDEVCMD1", "SLASH_ALEXDEVCMD2",
}

-- Плотные табличные литералы (категории предметов, hex-цвета качества) длиннее 120 — это не «грязь».
max_line_length = false

-- Папка tests/ — headless-харнес, который сам РЕАЛИЗУЕТ клиентский API (мок) и определяет
-- эти глобалы намеренно. Линтить аддоны, не харнес: его корректность проверяет прогон spec'ов.
exclude_files = { "**/tests/**" }
