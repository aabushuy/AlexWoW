#include "process.h"

#include <windows.h>
#include <filesystem>
#include <stdexcept>
#include <string>

namespace launcher::process {

void LaunchWow(const std::wstring& wow_root) {
    namespace fs = std::filesystem;
    fs::path exe = fs::path(wow_root) / L"Wow.exe";
    if (!fs::exists(exe))
        throw std::runtime_error("Wow.exe не найден: " + exe.string());

    std::wstring cmd = L"\"" + exe.wstring() + L"\"";
    std::wstring cwd = wow_root;

    STARTUPINFOW si{}; si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};

    // CreateProcessW требует **mutable** буфер для lpCommandLine.
    std::vector<wchar_t> cmdline(cmd.begin(), cmd.end());
    cmdline.push_back(L'\0');

    if (!CreateProcessW(exe.wstring().c_str(),
                        cmdline.data(),
                        nullptr, nullptr, FALSE,
                        CREATE_NEW_PROCESS_GROUP,
                        nullptr, cwd.c_str(), &si, &pi))
    {
        throw std::runtime_error("CreateProcessW failed: " + std::to_string(GetLastError()));
    }
    // Закрываем handles, процесс продолжает работать.
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
}

} // namespace launcher::process
