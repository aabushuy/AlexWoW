#pragma once
#include "manifest.h"
#include <atomic>
#include <filesystem>
#include <functional>
#include <string>

namespace launcher::downloader {

struct Progress {
    int files_done = 0;
    int files_total = 0;
    long long bytes_done = 0;     // только реально скопированные байты в этой сессии
    long long bytes_total = 0;    // общий объём, который пришлось перекопировать
    std::wstring current_file;    // относительный путь
};

// Колбэк для UI — вызывается из этого же потока (не главного). Передавайте PostMessage внутри.
using ProgressFn = std::function<void(const Progress&)>;

// Синхронизировать local с source согласно манифесту.
// Алгоритм per-файл:
//   1) Если локального нет → копировать.
//   2) Если есть и размер != ожидаемому → копировать.
//   3) Если размер совпал → посчитать SHA-256 локального; совпал → пропустить, нет → копировать.
void Sync(const std::wstring& source_root,
          const std::wstring& local_root,
          const manifest::Manifest& m,
          const ProgressFn& report,
          const std::atomic<bool>& cancel);

} // namespace launcher::downloader
