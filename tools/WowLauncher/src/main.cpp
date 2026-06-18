// AlexWoW Launcher — entry point.
//
// MVP-возможности:
//   * Читает manifest.json с UNC-источника (\\homeserver\WowProject\client\WoW335 / Z:\client\WoW335)
//   * Сверяет SHA-256 локальных файлов, докачивает отличающиеся (паттерн rsync)
//   * Прописывает realmlist.wtf на наш сервер
//   * Запускает Wow.exe
//
// Что НЕ в MVP: авто-ввод логин/пароль, античит, блокировка прямого запуска wow.exe.
// См. plan-file: tools/WowLauncher/README.md и план в C:\Users\Alex\.claude\plans\.

#include "ui.h"

#include <windows.h>
#include <string>

int APIENTRY wWinMain(HINSTANCE, HINSTANCE, LPWSTR /*cmd*/, int) {
    launcher::ui::LaunchOptions opts{};
    // Источник по умолчанию — UNC SMB-шара homeserver.
    opts.sourcePath   = L"\\\\192.168.2.210\\WowProject\\client\\WoW335";
    // Локальное место по умолчанию — корень C-диска. Меняется в UI.
    opts.localPath    = L"C:\\Games\\WoW335";
    // Адрес нашего auth-сервера.
    opts.realmAddress = L"192.168.2.210";

    return launcher::ui::RunMainDialog(opts);
}
