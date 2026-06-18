#include "manifest.h"

#include <nlohmann/json.hpp>
#include <fstream>
#include <stdexcept>

namespace launcher::manifest {

Manifest Read(const std::filesystem::path& manifest_path) {
    std::ifstream in(manifest_path, std::ios::binary);
    if (!in)
        throw std::runtime_error("Не удалось открыть manifest.json: " + manifest_path.string());

    nlohmann::json j;
    in >> j;

    Manifest m;
    m.version    = j.value("version", "");
    m.source     = j.value("source", "");
    m.total_size = j.value("total_size", 0LL);
    if (!j.contains("files") || !j["files"].is_array())
        throw std::runtime_error("manifest.json: отсутствует массив files");

    m.files.reserve(j["files"].size());
    for (auto& e : j["files"]) {
        Entry ent;
        ent.path   = e.value("path", "");
        ent.sha256 = e.value("sha256", "");
        ent.size   = e.value("size", 0LL);
        if (ent.path.empty() || ent.sha256.size() != 64)
            continue; // битая запись — пропускаем
        m.files.push_back(std::move(ent));
    }
    return m;
}

} // namespace launcher::manifest
