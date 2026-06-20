--[[ Headless-тесты логики AlexDevCmd (Core.lua) вне клиента.
  Запуск: luajit alexdevcmd_spec.lua (из tools/addons/tests). Код 1 при провале.
  Проверяет чистые хелперы и парсер addon-кадров (телепорты/рынок) через CHAT_MSG_ADDON.
]]

local here = (arg[0] or ""):match("^(.*[/\\])") or "./"
local mock = dofile(here .. "wow_stub.lua")
dofile(here .. "../AlexDevCmd/Core.lua")
local A = AlexDevCmd
assert(A, "Core.lua должен определить глобал AlexDevCmd")

local total, failed = 0, 0
local function check(name, got, want)
  total = total + 1
  if got ~= want then
    failed = failed + 1
    print(string.format("FAIL  %-42s got %s  want %s", name, tostring(got), tostring(want)))
  else
    print("ok    " .. name)
  end
end
local function send(line) mock.fire("CHAT_MSG_ADDON", A.PREFIX, line) end

-- ─── FactionLabel ───
check("Faction: 1=Альянс",     A.FactionLabel(1), "Альянс")
check("Faction: 2=Орда",       A.FactionLabel(2), "Орда")
check("Faction: 0=Нейтральные", A.FactionLabel(0), "Нейтральные")

-- ─── ParseTeleport ───
local t = A.ParseTeleport("T|5|2|Оргриммар")
check("ParseTeleport: id",      t and t.id, 5)
check("ParseTeleport: faction", t and t.faction, 2)
check("ParseTeleport: name",    t and t.name, "Оргриммар")
check("ParseTeleport: bad id → nil", A.ParseTeleport("T|x|1|Bad"), nil)

-- ─── PlayerTrainer (stub: Воин/WARRIOR) ───
local key, name = A.PlayerTrainer()
check("PlayerTrainer: key",  key, "warrior")
check("PlayerTrainer: name", name, "Воин")

-- ─── AddCreature ───
check("AddCreature: ok", A.AddCreature("humanoid", "Гуманоид", "80", "3"), true)
check("AddCreature: queue len", #A.creatureQueue, 1)
check("AddCreature: count", A.creatureQueue[1].count, 3)
check("AddCreature: bad level → false", A.AddCreature("beast", "Животное", "0", "1"), false)
check("AddCreature: count<1 → 1", (A.AddCreature("demon", "Демон", "10", "0") and A.creatureQueue[#A.creatureQueue].count), 1)

-- ─── Парсер телепортов (кадр) + сортировка приходит уже с сервера ───
send("TBEGIN")
send("T|1|1|Штормград")
send("T|2|2|Оргриммар")
send("T|3|0|Даларан")
send("T|x|1|Битый")    -- нечисловой id — пропуск
send("TEND")
check("teleports: count (bad skipped)", #A.teleports, 3)
check("teleports[2] name", A.teleports[2].name, "Оргриммар")

-- ─── Парсер рынка (кадр) ───
send("IBEGIN")
send("I|6948|1|0|1|Камень возвращения")
send("I|2835|1|0|1|Грубый камень")
send("IEND")
check("market: count", #A.marketItems, 2)
check("market[1] id", A.marketItems[1].id, 6948)
check("market[2] name", A.marketItems[2].name, "Грубый камень")

print(string.format("\n%d checks, %d failed", total, failed))
os.exit(failed > 0 and 1 or 0)
