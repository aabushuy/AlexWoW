--[[ Headless-smoke библиотеки AlexUI: загрузка под мок-API + чистая математика колонок.
  Запуск: luajit alexui_spec.lua (из tools/addons/tests). Код 1 при провале.
]]

local here = (arg[0] or ""):match("^(.*[/\\])") or "./"
dofile(here .. "wow_stub.lua")
dofile(here .. "../AlexUI/AlexUI.lua")

local total, failed = 0, 0
local function check(name, got, want)
  total = total + 1
  if got ~= want then
    failed = failed + 1
    print(string.format("FAIL  %-40s got %s  want %s", name, tostring(got), tostring(want)))
  else
    print("ok    " .. name)
  end
end

check("AlexUI загружен", type(AlexUI), "table")
check("CreateWindow есть", type(AlexUI.CreateWindow), "function")
check("Columns есть", type(AlexUI.Columns), "function")

-- Чистая математика раскладки 1:2:4 при ширине 864.
local lay = AlexUI.Columns(864, 520, { 1, 2, 4 })
check("Columns: w2 = 2*w1", lay.w[2], lay.w[1] * 2)
check("Columns: w3 = 4*w1", lay.w[3], lay.w[1] * 4)
check("Columns: правый край = W-margin", lay.x[3] + lay.w[3], 864 - 16)
check("Columns: высота", lay.height, 520 - 52 - 14)

print(string.format("\n%d checks, %d failed", total, failed))
os.exit(failed > 0 and 1 or 0)
