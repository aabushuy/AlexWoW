#pragma once
#include <string>

namespace launcher::process {

// Запустить Wow.exe из заданного корня клиента. Бросает std::runtime_error при ошибке.
void LaunchWow(const std::wstring& wow_root);

} // namespace launcher::process
