#pragma once
#include <string>

namespace launcher::config {

// Прописать `SET realmlist "<address>"` в WTF/Config.wtf и Data/<locale>/realmlist.wtf,
// если он есть. Создаёт файлы если их нет. Идемпотентно.
void WriteRealmlist(const std::wstring& wow_root, const std::wstring& realm_address);

} // namespace launcher::config
