#pragma once
#include <filesystem>
#include <string>

namespace launcher::hasher {

// SHA-256 файла → нижний регистр hex (64 символа). Бросает std::runtime_error при ошибке I/O.
std::string Sha256File(const std::filesystem::path& path);

} // namespace launcher::hasher
