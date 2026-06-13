# Devlog — M5: Видимость и мир

**Статус:** 🟡 в работе. Веха крупная — ведём по срезам, каждый завершается
проверяемым поведением живого клиента 3.3.5a.

## Разбивка на срезы

| Срез | Содержание | Критерий приёмки (клиент) |
|---|---|---|
| **M5.1** | Видимый NPC рядом со спавном (захардкоженный) + `CMSG_CREATURE_QUERY` | Вошёл в мир — рядом стоит именованный NPC |
| **M5.2** | Каркас `AlexWoW.Game`: сущности, ObjectGuid, грид/карта, реестр сессий | Фундамент (косвенно) |
| **M5.3** | Другие игроки видны + рассылка движения | Два клиента: A видит, как B бегает |
| **M5.4** | Спавн NPC из дампа мира CMaNGOS (импорт в MySQL) | Реальные NPC в стартовой зоне |
| **M5.5** | maps/vmaps — высота рельефа и коллизии | NPC/игрок стоят на земле, LoS |

---

## M5.1 — видимый NPC (✅ проверено клиентом)

**Цель:** после входа в мир рядом с персонажем стоит NPC, которого видно и можно
навести курсор/получить имя.

### Что сделано

- **GUID существа** (`Npcs.UnitGuid`): `0xF130 << 48 | entry << 24 | counter`
  (HIGHGUID_UNIT, 3.3.5a). entry встроен в GUID.
- **`CreatureUpdate.BuildCreateObject`** — `SMSG_UPDATE_OBJECT`,
  `UPDATETYPE_CREATE_OBJECT2`, `TYPEID_UNIT (3)`, флаг `LIVING` **без** `Self`.
  Движение-блок идентичен проверенному `PlayerSpawn` (moveFlags=0, 9 скоростей).
  Values: GUID, `OBJECT_FIELD_ENTRY (0x0003)`, type-mask `Object|Unit (0x9)`, scale,
  health/maxhealth, level, faction, displayId, nativeDisplayId, bounding/combat reach.
- **`CMSG_CREATURE_QUERY (0x060)` → `SMSG_CREATURE_QUERY_RESPONSE (0x061)`** —
  имя/подпись/тип существа (layout сверен с gtker.com): 6 CString
  (name1–4, sub_name, icon) + type_flags/type/family/rank/killCredit×2 +
  displayId[4] + health/mana-множители + racial_leader + questItems[6] + movementId.
- **Тестовый NPC** (`Npcs.TestDummy`): entry 190000, «Тестовый страж / AlexWoW»,
  display **49** (модель человека — гарантированно есть в DBC клиента),
  level 80, faction **35** («дружелюбен ко всем», нейтральный). Спавнится в 4 ярдах
  «перед» игроком, на ту же высоту (рельеф пока неизвестен — это M5.5).
- **UTF-8 для `CString`** — клиент 3.3.5a (ruRU) ожидает UTF-8; ASCII — подмножество,
  для англ. строк байты не меняются. Бонус: кириллица в именах персонажей теперь корректна.

### Детали протокола (3.3.5a)

- `TYPEID_UNIT = 3`, type-mask существа = `Object|Unit = 0x9`.
- `OBJECT_FIELD_ENTRY = 0x0003` — обязателен для существ (entry шаблона).
- `CMSG_CREATURE_QUERY` тело: `u32 entry` + `u64 guid` (guid для ответа не нужен).
- «Нет данных» в ответе: вернуть `entry | 0x80000000`.

### Риски / на что смотреть на клиенте

- **displayId** должен существовать в DBC клиента — взяли 49 (человек) для надёжности
  первой проверки; реальные модели придут с дампом мира (M5.4).
- Если NPC не виден — добавить `UNIT_FIELD_BYTES_0` (race/class/gender/powertype).
- Высота Z = высота игрока: NPC может слегка парить/тонуть, пока нет maps (M5.5).

### Проверка

- ✅ Сборка чистая, без предупреждений.
- ✅ **Живой клиент (2026-06-03):** NPC появляется рядом, имя-плашка/уровень/фракционный
  цвет (зелёный) и таргет работают.

### Уроки

- **Модели рас (display 49 и т.п.) у существа рисуются БЕЛЫМИ** — их текстура собирается
  из кастомизации персонажа (`UNIT_FIELD_BYTES_0` + `PLAYER_BYTES`), которой у NPC нет.
  Для существ нужен настоящий **creature display id** (своя текстура в CreatureDisplayInfo.dbc).
- **`SMSG_CREATURE_QUERY_RESPONSE` кэшируется клиентом по entry** — при смене имени/полей
  того же entry старое имя остаётся в плашке до сброса клиентского кэша. Не баг.
- Текущий плейсхолдер: display **257** (оказался моделью человеческого ребёнка, не цыплёнком —
  но текстурирован верно). Реальные модели придут с дампом мира (M5.4).
- В логах виден `0x11C` (`CMSG_SET_ACTIVE_MOVER`?) без обработчика — не критично, учесть позже.

---

## M5.2 + M5.3 — другие игроки видны + рассылка движения (✅ проверено клиентом)

Каркас `AlexWoW.Game` отдельным проектом **отложен** до M6 (там появится бой/спеллы — выделение
оправдается). Реестр мира и видимость сделаны внутри `AlexWoW.WorldServer` (`World/`), чтобы
быстрее получить проверяемый многопользовательский результат.

### Что сделано

- **`WorldState`** (singleton) + **`WorldPlayer`** — реестр онлайн-игроков
  (`ConcurrentDictionary` по guid), видимость по дистанции (100 ярдов, та же карта).
- **Вход в мир** (`CMSG_PLAYER_LOGIN`): игрок регистрируется и **обоюдно спавнится** с соседями.
- **Чужой спавн**: `PlayerSpawn.BuildCreateObject` принимает позицию/ориентацию явно + флаг
  `isSelf` (для других — без `Self`, с живыми координатами из их сессии).
- **Рассылка движения**: `MSG_MOVE_*` ретранслируются соседям как есть (тело уже содержит
  packed guid мувера) → движение в реальном времени.
- **Выход/обрыв**: `SMSG_DESTROY_OBJECT (0x0AA)` соседям + снятие с реестра (логаут и дисконнект,
  идемпотентно через `WorldSession.LeaveWorldAsync`).

### Ключевой урок (многопоточность)

Соседние сессии шлют пакеты этому клиенту **из своих потоков**, а RC4 (`WorldHeaderCrypt`) —
потоковый шифр с общим состоянием. Без сериализации шифрование+запись расходятся и поток
рассыпается. Решение: `SemaphoreSlim` в `WorldSession.SendAsync` (шифрование и запись — под локом).
Лок берётся по одной сессии за раз (не вложенно) → дедлоков нет.

### Проверка

- ✅ Сборка чистая.
- ✅ **Живой клиент (2026-06-03):** два клиента (аккаунты TEST и TEST2, оба человека в Северозёмье) —
  видят друг друга, движение одного видно у второго в реальном времени, имена подтягиваются.

---

## M5.4 — NPC из дампа мира CMaNGOS (✅ проверено клиентом)

### Импорт дампа

- Источник: `cmangos/wotlk-db`, `Full_DB/WoTLKDB_1_9_14092.sql.gz` (~29 МБ gzip).
- На homeserver: скачан в `~`, создана БД **`mangos`** (grant `alexwow`), импорт
  `gunzip -c … | docker exec -i alexwow-mysql mysql … mangos` — **55 c**.
- Итог: **152 979** строк `creature` (спавны), **29 919** `creature_template`.
- `Updates/` поверх базы пока **не применял** (база самодостаточна для спавна; применить позже).
- ⚠️ БД `mangos` живёт в том же volume `alexwow-mysql-data`, что и `alexwow_auth`.
  При полном пересоздании volume её надо переимпортировать (бинарники деплоя её не трогают).

### Доступ и спавн

- **`WorldDatabase`** (read-only) к `mangos`. Координаты в дампе — `decimal(40,20)`,
  приводим `CAST(… AS DOUBLE)`. Подключение — отдельная строка
  `WorldServer__WorldConnectionString` (docker-compose).
- **Спавн на входе**: `creature ⨝ creature_template` по карте игрока в квадрате
  ±100 ярдов, `spawnMask & 1`, лимит 100. Каждое существо — `SMSG_UPDATE_OBJECT`.
  GUID существа = `0xF130 | entry<<24 | creature.guid`. Z — из дампа (на земле).
- **`CMSG_CREATURE_QUERY`** теперь отвечает из `creature_template` (имя/подпись/тип),
  с fallback на тестовый реестр, если БД недоступна.
- Поля существа: `OBJECT_FIELD_ENTRY`, scale, `UNIT_FIELD_BYTES_0` (class),
  health, level (MinLevel), faction, **`UNIT_NPC_FLAGS (0x52)`** (иконки квестов/вендора/госсипа),
  displayId (первый ненулевой из DisplayId1..4).
- Если БД мира недоступна — graceful fallback на один захардкоженный тестовый NPC.

### Грабли

- В дампе CMaNGOS есть **дев/QA-NPC с префиксом `[DND]`** (TAR-пьедесталы тренеров/предметов) —
  ~170 шаблонов, 29 заспавнены прямо у человеческого старта. Засоряют зону. Фильтр в запросе:
  `t.Name NOT LIKE '[DND]%'` (в MySQL `[` в LIKE — литерал).
- `UNIT_NPC_FLAGS` индекс взят из CMaNGOS `UpdateFields.h`: `OBJECT_END(0x06)+0x4C = 0x52`.

### Проверка

- ✅ Сборка чистая; world логирует «БД мира подключена: 152979 спавнов».
- ✅ **Живой клиент (2026-06-03):** в Северозёмье видны реальные NPC из дампа —
  Paladin Trainer, Deputy Willem (квестодатель с иконкой), Rabbit, Riding White Stallion,
  стражи; стоят на земле, имена/уровни из БД. Дев-`[DND]`-NPC отфильтрованы.

### Дальше (вне M5.4)

NPC не двигаются (нет ИИ) и не реагируют (нет боя) — это M6. Гейм-объекты (`gameobject`),
динамическая видимость при перемещении (грид), maps/vmaps (M5.5) — отдельные срезы.

---

## M5.6a — динамическая видимость NPC (✅ проверено клиентом)

Раньше NPC грузились только на входе и висели вечно (и пустота за стартовым квадратом).
Теперь видимый набор пересчитывается при ходьбе.

### Что сделано

- **`SpawnHandlers.RefreshVisibleNpcsAsync(session, map, x, y)`** — диф видимого набора:
  запрос существ в ±100 ярдов от текущей позиции, сравнение с `session.VisibleNpcs`:
  ушедшие → `SMSG_DESTROY_OBJECT`, новые → `SMSG_UPDATE_OBJECT`. Один метод для входа
  (набор пуст → все CREATE) и для движения.
- **Троттлинг**: в `MovementHandlers` пересчёт запускается, только если игрок отошёл
  ≥ `VisRefreshStep` (20 ярдов) от позиции прошлого пересчёта — не на каждый пакет движения.
- **`VisibleNpcs.Clear()`** при выходе из мира (клиент выгружает мир — при повторном входе
  той же сессией создаём заново).
- Лимит видимых поднят до 150.

### Проверка

- ✅ **Живой клиент (2026-06-03):** при ходьбе дальние NPC исчезают, новые впереди
  появляются; при возврате — появляются снова. Дев-`[DND]` у старта пропали.

### Заметки

- На границе ~100 ярдов возможно лёгкое мерцание (нет гистерезиса) — приемлемо, доработать позже.
- Пересчёт делает синхронный DB-запрос в обработчике движения (раз в 20 ярдов) — для нескольких
  игроков ок; при росте нагрузки вынести в грид/фон.
- **Тестовая зона CMaNGOS «TAR»** у человеческого старта: помимо `[DND]`-пьедесталов есть
  ряд тренеров классов (entries 26324–26332, generic-имена «Mage Trainer» …). См. M5.6b — отфильтрованы.

---

## M5.6b — гейм-объекты из дампа + очистка тест-контента (✅ проверено клиентом)

### Что сделано

- **`gameobject ⨝ gameobject_template`** (как у существ): спавн рядом, диф-видимость при ходьбе,
  `SMSG_DESTROY_OBJECT`. GUID GO = `0xF110 | entry<<24 | gameobject.guid`. Фильтр `displayId<>0`
  (невидимые триггеры пропускаем).
- **`GameObjectUpdate`**: `TYPEID_GAMEOBJECT (5)`, type-mask `Object|GameObject (0x21)`,
  движение-блок `STATIONARY_POSITION | ROTATION` → x,y,z,o + **упакованный кватернион (int64)**.
  Values: entry, scale(size), `GAMEOBJECT_DISPLAYID/FLAGS/PARENTROTATION(4f)/FACTION/BYTES_1`.
- **`CMSG_GAMEOBJECT_QUERY (0x5E)` → `SMSG_GAMEOBJECT_QUERY_RESPONSE (0x5F)`**: entry, type,
  displayId, имя (+3 пустых), iconName/castBarCaption/unk1, data[24]=0, size, questItems[6]=0
  (data/quest-поведение пока не нужно — объект и так рисуется и именуется).

### Ключевой урок (поворот GO)

В 3.3.5 ориентация гейм-объекта берётся из **упакованного кватерниона в movement-блоке**
(`UPDATEFLAG_ROTATION = 0x200`), а не из `GAMEOBJECT_PARENTROTATION`-float'ов (те для transport-родителя).
Без него мебель «лежала/смотрела не туда». Упаковка (CMaNGOS `QuaternionCompressed`):
`raw = Z | (Y<<21) | (X<<42)`; `X = int(qx·2^21)·sign(w) & 0x3FFFFF`, `Y/Z = int(q·2^20)·sign(w) & 0x1FFFFF`.

### Очистка тест-контента CMaNGOS (в дампе у человеческого старта)

Дамп CMaNGOS кладёт у старта тест/кастом-контент, который «парит» (нет рельефа — M5.5) и не нужен:
- **`[%`** — дев/плейсхолдеры (`[DND]`, `[PH]`, `[UNUSED]`).
- **generic-тренеры/бистмастер** без subname (`Name LIKE '% Trainer' AND SubName=''`, `Name='Beastmaster'`):
  TAR-тестовые. Настоящие тренеры — с личным именем и «X Trainer» в subname → остаются.
- **кастомные арена-NPC** (entry 26012 Arena Organizer, 26075 Paymaster, 26760 Fight Promoter) —
  список исключений `WorldDatabase.ExcludedCreatureEntries` (нет в ретейле, заспавнены в каждом городе).

### Проверка

- ✅ **Живой клиент (2026-06-03):** видны гейм-объекты (скамьи, фонари, бочки/ящики, венки на дверях),
  стоят корректно после фикса поворота; парящие TAR-тренеры и арена-NPC у старта убраны.

---

## M5.5 — maps (высота рельефа) ✅ загрузчик + .NET-экстрактор, рельеф на сервере

Изначально план был «C++ экстрактор CMaNGOS + .NET загрузчик», но на машине **нет C++ тулчейна**
(ни VS, ни MinGW, ни CMake). По выбору пользователя — **написал экстрактор на .NET** (см. ниже).

### Сделано (часть 1 — загрузчик)

- Новый проект **`AlexWoW.DataStores`**:
  - **`GridMap`** — читает один файл `maps/MMMGGgg.map` (формат CMaNGOS, magic `MAPS`/`v1.4`).
    Заголовок: offset'ы area/height/liquid/holes. Грузим только высоту: сетки **V9 129×129**
    (углы) + **V8 128×128** (центры), варианты float / int16 / int8 (int разворачиваем в float
    по `value*mult + gridHeight`). `GetHeight(x,y)` — билинейная интерполяция по 4 треугольникам
    ячейки (1:1 с `GridMap::getHeightFromFloat`). holes/area/liquid пока пропущены.
  - **`TerrainMaps`** — по (mapId, x, y): грид `gx=(int)(32 - x/533.333)`, `gy` аналогично;
    лениво грузит `maps/{map:D3}{gx:D2}{gy:D2}.map`, кэширует (`ConcurrentDictionary`), отдаёт высоту.
- WorldServer: опция `WorldServer__MapsPath` (`/data/maps` в docker), DI-singleton `TerrainMaps`,
  лог при старте, и **проверочный лог при входе**: «земля в (x;y) = H (Z персонажа …, дельта …)».
- docker-compose: том `/data/docker/alexwow-maps:/data/maps:ro` — **вне** каталога деплоя
  (deploy.ps1 делает `rm -rf` деплой-каталога). Нет каталога/файлов → graceful «без рельефа».

### Сделано (часть 2 — .NET-экстрактор `tools/MapExtractor`)

Чистый .NET, без тулчейна. Запуск: `dotnet run --project tools/MapExtractor -- <dataDir> <outDir> [mapId]`.

- **MPQ**: вендорнул `Foole.Mpq` (pure-C# ридер WoW-MPQ; War3Net не открыл WoW-архивы). ZLib —
  заменил на встроенный `System.IO.Compression.ZLibStream` (без внешних пакетов).
- **`MpqChain`** — цепочка MPQ с приоритетом патчей (patch-3 > … > common, + ruRU-локаль),
  чтение по точному имени (хеш, listfile не нужен). 18 архивов; Map.dbc в `patch-ruRU-*`,
  террейн — в `common-2`+`patch`.
- **`ClientData`** — `Map.dbc` (WDBC: id@0, directory-string@4) + WDT `MAIN` (64×64, бит0 = есть ADT).
- **`AdtHeight`** — парсинг MCNK (по `IndexX/IndexY`) + субчанк `MCVT` (скан внутри MCNK, надёжнее
  `ofsMCVT`); `V9[i*8+y][j*8+x]=ypos+mcvt[y*17+x]`, `V8[...]=ypos+mcvt[y*17+9+x]` — 1:1 с экстрактором CMaNGOS.
- **`MapWriter`** — пишет `.map` (MAPS/v1.4, height-only float) под мой загрузчик.
- **Маппинг имён** (ключевой!): ADT-тайл `(x,y)` → `.map` `{mapId:D3}{y:D2}{x:D2}` (т.е. gx=y, gy=x).
  Якорь: `Azeroth_32_48.adt` → `0004832.map`, человеческий старт (-8949.95,-132.49) → грид gx=48,gy=32.

### Проверка (✅ round-trip)

- ✅ Экстракция всех карт: **5744 тайла, 66 карт, 0.71 ГБ, ~123 c**.
- ✅ **Загрузчик `AlexWoW.DataStores` на сгенерированных файлах**: высота земли у старта
  `height(map=0, -8949.95, -132.49) = 83.53` — совпадает со стартовой Z `83.5312`. Экстрактор+загрузчик корректны.
- ✅ Развёрнуто: `maps/*` (5744) на homeserver в `/data/docker/alexwow-maps` (том `:ro` в контейнере),
  world логирует «Рельеф (maps) подключён».
- Финальное end-to-end в живом клиенте: при входе лог «Рельеф: земля = H (дельта ≈ 0)».

### Дальше

- Применения высоты: ground-snap, падения, проверки Z — по мере M6.

---

## M5.5+ vmaps (WMO LoS) — ✅ проверено round-trip'ом (всё на .NET)

Решение пользователя — писать и vmap-экстрактор на .NET (тулчейна C++ нет). Сделан срез
**WMO-коллизии → line-of-sight** (M2-дудады позже).

### Экстрактор (`tools/MapExtractor`)

- **Geometry**: `Vec3` + `Matrix3` (соглашения G3D `fromEulerAnglesZYX`).
- **Wmo**: root (`MOHD.nGroups`) + group-файлы (`MOVT` вершины, `MOVI` индексы; заголовок `MOGP`=68 байт,
  далее субчанки). → треугольники в модельном пространстве.
- **VmapExtract**: размещения из ADT (`MWMO` имена + `MODF` поз/поворот/uniqueId). Трансформа в **игровые
  координаты**: `internal = R·v + fixCoords(pos)` (R=`Rz(rot.y)·Ry(rot.x)·Rx(rot.z)`), `game=(mid−ix,mid−iy,iz)`, mid=17066.67.
- **VmapWriter** + команда `vmap`: треугольники разносятся по тайлам (AABB-overlap), дедуп инстансов по
  `uniqueId`, файл `{map:D3}{gx:D2}{gy:D2}.vmap` (AVMP: magic+version+count+9 float/треуг.).

### Серверный загрузчик (`AlexWoW.DataStores/Collision`)

- **VmapTile**: загрузка тайла + Möller–Trumbore `RayTriangle`, `SegmentBlocked`, `FloorBelow`.
- **Vmaps**: ленивый кэш тайлов, `IsInLineOfSight(map,x1,y1,z1,x2,y2,z2)`, `GetFloor`. Перебор треугольников
  (для редких LoS ок; BVH — позже).

### Проверка (✅ round-trip)

- Карта 0: **302 vmap-тайла** (~21 c). Луч сквозь Аббатство Северозёмья на Z=100 → **LoS False** (стена),
  тот же луч на Z=200 над зданием → **LoS True**. Геометрия+transform ранее сверены AABB (аббатство/мост/Стормвинд).

### Дальше (vmaps/mmaps)

- M2-дудады (деревья/объекты) в коллизию — отдельно (меньший приоритет).
- Полная экстракция всех карт + деплой + подключение `Vmaps` в WorldServer — когда появится потребитель (бой, M6).
- BVH вместо перебора — при нагрузке.
- **mmaps** (навмеш для ИИ) через DotRecast — крупнейший оставшийся срез. **Фундамент готов** (см. ниже).

---

## M5.5+ mmaps (навмеш) — 🟡 фундамент валиден на реальных данных

Решение пользователя — всё на .NET. Навмеш через **DotRecast** (C#-порт Recast/Detour, NuGet
`DotRecast.Recast.Toolset` 2026.1.3).

### Сделано (`tools/MmapGen`)

- Команда `tile <mapsDir> <map> <gx> <gy>`: сэмплирует высоты тайла (`TerrainMaps`, 129×129),
  строит входной меш Recast (Y-вверх: вершина `(worldX, height, worldY)`, намотка под нормаль +Y),
  `SoloNavMeshBuilder` → `DtNavMesh`, запрос пути `DtNavMeshQuery` (`FindNearestPoly`+`FindPath`).
- API-грабли: `RcSampleInputGeomProvider(float[] verts, int[] faces)`; `RcPartition.WATERSHED`;
  `SampleAreaModifications` в `DotRecast.Recast.Toolset.Builder`; `FindPath(...Span<long> path, out int n, int maxPath)`.
  Намотка треугольников критична (нормаль вниз → 0 полигонов); плоский bbox без перепада высот → build fail.

### Проверка (✅)

- Тайл Северозёмья 0/48,32: навмеш `success=True, polys=1646`; `FindNearestPoly` находит полигоны
  под стартом и у аббатства; `FindPath` возвращает путь.

### Полный пайплайн (✅ путь обходит здания)

- **`NavmeshBuild`**: меш тайла = рельеф (ходимая поверхность) + vmap-треугольники (WMO) как
  препятствия → `SoloNavMeshBuilder` → `DtMeshData` (Build в try/catch — редкие edge-case тайлы пропускаются).
- **`mmap`-команда**: пер-тайл сборка → сериализация `{map:D3}{gx:D2}{gy:D2}.mmtile` (`DtMeshDataWriter`).
- **Сервер `AlexWoW.DataStores/Navigation/Navmesh`**: ленивый кэш тайлов (`DtMeshDataReader`→`DtNavMesh`→
  `DtNavMeshQuery`), `FindPath` → точки пути в игровых координатах (recast↔game = (x,z,y)).
- **`mmpath`-команда** — проверка через серверный загрузчик.
- Подключено в WorldServer: опции `VmapsPath`/`MmapsPath`, singleton'ы `Vmaps`/`Navmesh`, лог при старте,
  тома `/data/vmaps`,`/data/mmaps` в docker-compose. Потребитель — ИИ существ (M6).

### Проверка (✅)

- Тайл Северозёмья: `FindPath` старт→аббатство вернул **путь из 8 точек, ОБХОДЯЩИЙ аббатство**
  (vmap-стены стали «дырами» в навмеше — поиск пути их огибает), а не прямую сквозь стены.

### Осталось (батчи данных, к M6)

- Полная генерация vmaps+mmaps **всех** карт + деплой (карта 0 — генерится/деплоится; остальное — батч).
- M2-дудады в коллизию; ститчинг тайлов навмеша (межтайловые пути); BVH в vmap (перф).
