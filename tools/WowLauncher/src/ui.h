#pragma once
#include <string>
#include <functional>

namespace launcher::ui {

// Колбэки и параметры запуска (передаются из main → диалог).
struct LaunchOptions {
    std::wstring sourcePath;        // \\server\share\client или Z:\client\WoW335
    std::wstring localPath;         // C:\Games\WoW335
    std::wstring realmAddress;      // 192.168.2.210
};

// Прогресс-репорт из background-потока в UI (через PostMessage).
struct Progress {
    std::wstring status;            // строка статуса
    long long bytesDone = 0;
    long long bytesTotal = 0;
    int filesDone = 0;
    int filesTotal = 0;
    bool finished = false;
    bool failed = false;
};

// Точка входа: показать главный диалог, заблокировать поток до закрытия.
// Возвращает 0 при успехе, !=0 — при ошибке.
int RunMainDialog(const LaunchOptions& defaults);

} // namespace launcher::ui
