#include "config.h"

#include <windows.h>
#include <filesystem>
#include <fstream>
#include <regex>
#include <sstream>
#include <string>

namespace launcher::config {

namespace {

namespace fs = std::filesystem;

std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}

void EnsureRealmlistInFile(const fs::path& file, const std::string& realmAddr) {
    std::string text;
    if (fs::exists(file)) {
        std::ifstream in(file, std::ios::binary);
        std::ostringstream ss; ss << in.rdbuf();
        text = ss.str();
    }

    // Удаляем все существующие `SET realmlist "..."` (любой регистр SET).
    std::regex re(R"(^[ \t]*[Ss][Ee][Tt][ \t]+realmlist[ \t]+\"[^\"]*\"[^\r\n]*\r?\n?)",
                  std::regex::multiline);
    text = std::regex_replace(text, re, "");

    // Дописываем актуальную строку в конце.
    if (!text.empty() && text.back() != '\n')
        text.push_back('\n');
    text += "SET realmlist \"" + realmAddr + "\"\n";

    fs::create_directories(file.parent_path());
    std::ofstream out(file, std::ios::binary | std::ios::trunc);
    out.write(text.data(), (std::streamsize)text.size());
}

} // anonymous namespace

void WriteRealmlist(const std::wstring& wow_root, const std::wstring& realm_address) {
    std::string addr = WideToUtf8(realm_address);

    // Основной файл — WTF/Config.wtf.
    EnsureRealmlistInFile(fs::path(wow_root) / L"WTF" / L"Config.wtf", addr);

    // Локализованный — Data/<locale>/realmlist.wtf (ruRU, enUS, ...). Берём всё что нашли.
    fs::path dataDir = fs::path(wow_root) / L"Data";
    std::error_code ec;
    if (fs::is_directory(dataDir, ec)) {
        for (const auto& entry : fs::directory_iterator(dataDir, ec)) {
            if (!entry.is_directory(ec)) continue;
            fs::path candidate = entry.path() / L"realmlist.wtf";
            // Создаём/перезаписываем только если файл уже был — не плодим папки на пустом месте.
            if (fs::exists(candidate, ec))
                EnsureRealmlistInFile(candidate, addr);
        }
    }
}

} // namespace launcher::config
