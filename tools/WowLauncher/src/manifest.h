#pragma once
#include <filesystem>
#include <string>
#include <vector>

namespace launcher::manifest {

struct Entry {
    std::string path;       // относительный путь (POSIX-style, нормализуется при copy)
    std::string sha256;     // hex lowercase, 64 символа
    long long size = 0;
};

struct Manifest {
    std::string version;
    std::string source;
    long long total_size = 0;
    std::vector<Entry> files;
};

// Прочитать manifest.json. Бросает std::runtime_error при ошибке.
Manifest Read(const std::filesystem::path& manifest_path);

} // namespace launcher::manifest
