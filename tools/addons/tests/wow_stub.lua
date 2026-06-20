--[[ Мок клиентского WoW API 3.3.5a для headless-прогона аддонов вне игры.

  Назначение: загрузить .lua аддона обычным Lua-интерпретатором (LuaJIT / Lua 5.1),
  не запуская клиент, и дёргать чистые функции / гонять событийный шов с ассертами.

  Покрывает ровно тот минимум API, что нужен для загрузки и логических тестов
  (CreateFrame + RegisterEvent/SetScript, strsplit, wipe, чат-фрейм, слэш-таблица).
  Любой неизвестный метод фрейма — безопасный no-op (виджет-вызовы во время сборки UI
  не падают). Расширяется по мере роста тестов.

  Возвращает таблицу mock для интроспекции из тестов:
    mock.frames      — все созданные через CreateFrame фреймы
    mock.sent        — перехваченные SendAddonMessage { prefix, body, channel, target }
    mock.printed     — строки, ушедшие в DEFAULT_CHAT_FRAME:AddMessage
    mock.fire(ev,..) — разослать игровое событие зарегистрировавшим его фреймам (OnEvent)
]]

local mock = { frames = {}, sent = {}, printed = {} }

local function noop() end

-- Фабрика фрейма: реальные RegisterEvent/SetScript (нужны для событийного шва),
-- остальное — no-op через метатаблицу, чтобы вызовы виджет-методов не падали.
local function makeFrame()
  local f = { _events = {}, _scripts = {} }
  function f:RegisterEvent(ev) self._events[ev] = true end
  function f:UnregisterEvent(ev) self._events[ev] = nil end
  function f:SetScript(handler, fn) self._scripts[handler] = fn end
  function f:GetScript(handler) return self._scripts[handler] end
  setmetatable(f, { __index = function() return noop end })
  mock.frames[#mock.frames + 1] = f
  return f
end

-- ---- Глобалы клиента ----
function CreateFrame() return makeFrame() end

DEFAULT_CHAT_FRAME = { AddMessage = function(_, msg) mock.printed[#mock.printed + 1] = msg end }

SlashCmdList = {}
StaticPopupDialogs = {}

Minimap = makeFrame()
UIParent = makeFrame()
WorldFrame = makeFrame()
GameTooltip = makeFrame()

function UnitName() return "Tester" end
function UnitClass() return "Воин", "WARRIOR" end  -- (локализованное имя, токен) как клиент 3.3.5a
function UnitLevel() return 80 end
function GetSpellInfo() return nil end
function GetItemIcon() return nil end
function GetCursorPosition() return 0, 0 end

function SendAddonMessage(prefix, body, channel, target)
  mock.sent[#mock.sent + 1] = { prefix = prefix, body = body, channel = channel, target = target }
end
function SendChatMessage() end

function wipe(t)
  for k in pairs(t) do t[k] = nil end
  return t
end

-- Точная семантика WoW strsplit: каждый символ sep — разделитель; возвращает все поля.
function strsplit(sep, str)
  local class = sep:gsub("[%(%)%.%%%+%-%*%?%[%]%^%$]", "%%%1")
  local fields, from = {}, 1
  while true do
    local s, e = str:find("[" .. class .. "]", from)
    if not s then
      fields[#fields + 1] = str:sub(from)
      break
    end
    fields[#fields + 1] = str:sub(from, s - 1)
    from = e + 1
  end
  return unpack(fields)
end

-- Разослать игровое событие фреймам, которые его зарегистрировали (как клиентский диспетчер).
function mock.fire(event, ...)
  for _, f in ipairs(mock.frames) do
    if f._events[event] and f._scripts.OnEvent then
      f._scripts.OnEvent(f, event, ...)
    end
  end
end

return mock
