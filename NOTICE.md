# NOTICE

AlexWoW — World of Warcraft 3.3.5a server, C# port of CMaNGOS-WoTLK.

Copyright (C) 2026 Alexey Bushuev and contributors.

This program is free software; you can redistribute it and/or modify it under
the terms of the GNU General Public License **version 2** as published by the
Free Software Foundation. See [`LICENSE`](LICENSE) for the full text.

This program is distributed in the hope that it will be useful, but **WITHOUT
ANY WARRANTY**; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE.

---

## Upstream / третьесторонние компоненты

| Компонент | Использование | Лицензия | Источник |
|---|---|---|---|
| **CMaNGOS-WoTLK** | Эталон-источник для портирования (системы боя, спеллов, ИИ, мира) на C#. Каждый портированный файл содержит header-комментарий со ссылкой на исходный `.cpp/.h`. | **GPL-2.0** | https://github.com/cmangos/mangos-wotlk |
| **TrinityCore (3.3.5)** | Альтернативный эталон для cross-check механик. | GPL-2.0 | https://github.com/TrinityCore/TrinityCore/tree/3.3.5 |
| **WCell** | Архивный C#-сервер 3.3.5, источник C#-идиом для серверной части. | GPL-2.0 / GPL-3.0 | https://github.com/WCell/WCell-WoW |
| **DotRecast** | NuGet-зависимость: навмеш (.NET-порт Recast/Detour) для `MmapGen` и runtime-pathfinding. | MIT / Apache-2.0 | https://github.com/ikpil/DotRecast |
| **Foole.Mpq** | Встроенный исходный код парсера MPQ в `tools/MapExtractor/Foole.Mpq/`. Используется для чтения клиентских `.MPQ` при извлечении DBC/maps/иконок. | MIT (см. header-комментарии в `.cs`-файлах) | http://github.com/Foole/MpqReader |
| **gtker/wow_messages** | Внешний справочник `.wowm` для протокола 3.3.5a. Используется только локально (`reference/`, в репозиторий не коммитится). | MIT / Apache-2.0 | https://github.com/gtker/wow_messages |

## Совместимость лицензий

- **CMaNGOS / TrinityCore / WCell — GPL-2.0+.** Порт на другой язык программирования
  считается derivative work; репо AlexWoW поэтому распространяется под **GPL-2.0**.
- **DotRecast — MIT/Apache.** Совместимы с GPL-2.0 при динамическом линковании
  (NuGet-зависимость).
- **Foole.Mpq — MIT.** Текст разрешения остаётся в header-комментариях каждого `.cs`-файла
  в `tools/MapExtractor/Foole.Mpq/`. MIT совместим с GPL-2.0.

## Bliz/Blizzard content

Этот репозиторий **не содержит** никаких данных клиента World of Warcraft (DBC, MPQ,
maps, vmaps, mmaps, exe-файлов, иконок, моделей). Игровые ассеты являются собственностью
**Blizzard Entertainment**; пользователь генерирует/копирует их **из своей легальной
копии клиента 3.3.5a (build 12340)** при помощи `tools/MapExtractor` и `tools/MmapGen`.

Запуск сервера без обладания клиентом не предполагается. AlexWoW — это серверный
эмулятор протокола 3.3.5a, не предоставляющий доступа к ассетам Blizzard.

## Header в портированных файлах

Файлы, портированные из CMaNGOS-WoTLK, должны начинаться с атрибуции (см.
[CONTRIBUTING.md](CONTRIBUTING.md)):

```csharp
// Порт CMaNGOS-WoTLK: src/game/Spells/SpellMgr.cpp
// (https://github.com/cmangos/mangos-wotlk). GPL-2.0.
```

Это сохраняет цепочку attribution и облегчает будущие диффы против upstream.
