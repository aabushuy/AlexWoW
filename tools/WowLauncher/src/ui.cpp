#include "ui.h"
#include "downloader.h"
#include "config.h"
#include "process.h"
#include "manifest.h"

#include <windows.h>
#include <commctrl.h>
#include <string>
#include <thread>
#include <atomic>
#include <filesystem>

namespace launcher::ui {

namespace {

constexpr int IDC_PATH_EDIT       = 1001;
constexpr int IDC_PATH_BROWSE     = 1002;
constexpr int IDC_LAUNCH          = 1003;
constexpr int IDC_STATUS_LABEL    = 1004;
constexpr int IDC_PROGRESS        = 1005;
constexpr int IDC_LOG             = 1006;

constexpr UINT WM_LAUNCHER_PROGRESS = WM_USER + 1;
constexpr UINT WM_LAUNCHER_DONE     = WM_USER + 2;

struct DialogState {
    LaunchOptions opts;
    std::thread worker;
    std::atomic<bool> cancelRequested{false};
};

DialogState* StateFromHwnd(HWND hwnd) {
    return reinterpret_cast<DialogState*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
}

void AppendLog(HWND hwnd, const std::wstring& line) {
    HWND log = GetDlgItem(hwnd, IDC_LOG);
    int len = GetWindowTextLengthW(log);
    SendMessageW(log, EM_SETSEL, len, len);
    SendMessageW(log, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>((line + L"\r\n").c_str()));
}

// Чисто косметика: уведомление UI о прогрессе. Воркер-поток не трогает контролы напрямую — только PostMessage.
void PostProgress(HWND hwnd, const Progress& p) {
    auto* heap = new Progress(p);   // владение переходит к обработчику WM_LAUNCHER_PROGRESS
    PostMessageW(hwnd, WM_LAUNCHER_PROGRESS, 0, reinterpret_cast<LPARAM>(heap));
}

void RunWorker(HWND hwnd, DialogState* st) {
    auto report = [&](const std::wstring& s, long long done, long long total, int fd, int ft) {
        PostProgress(hwnd, Progress{s, done, total, fd, ft, false, false});
    };

    try {
        report(L"Чтение манифеста...", 0, 0, 0, 0);
        auto sourceManifest = std::filesystem::path(st->opts.sourcePath) / L"manifest.json";
        auto manifest = launcher::manifest::Read(sourceManifest);

        report(L"Сверка локальных файлов...", 0, manifest.total_size, 0, (int)manifest.files.size());

        launcher::downloader::Sync(
            st->opts.sourcePath, st->opts.localPath, manifest,
            [&](const launcher::downloader::Progress& dp) {
                std::wstring s = L"Файл " + std::to_wstring(dp.files_done) + L"/" +
                                 std::to_wstring(dp.files_total) + L": " + dp.current_file;
                report(s, dp.bytes_done, dp.bytes_total, dp.files_done, dp.files_total);
            },
            st->cancelRequested);

        if (st->cancelRequested) {
            PostMessageW(hwnd, WM_LAUNCHER_DONE, 1, 0);
            return;
        }

        report(L"Прописываю realmlist...", manifest.total_size, manifest.total_size,
               (int)manifest.files.size(), (int)manifest.files.size());
        launcher::config::WriteRealmlist(st->opts.localPath, st->opts.realmAddress);

        report(L"Запуск Wow.exe...", manifest.total_size, manifest.total_size,
               (int)manifest.files.size(), (int)manifest.files.size());
        launcher::process::LaunchWow(st->opts.localPath);

        PostMessageW(hwnd, WM_LAUNCHER_DONE, 0, 0);
    }
    catch (const std::exception& e) {
        // Конвертим в WideString через простой mbtowc
        std::string what = e.what();
        std::wstring wide(what.begin(), what.end());
        Progress err{};
        err.status = L"ОШИБКА: " + wide;
        err.failed = true;
        PostProgress(hwnd, err);
        PostMessageW(hwnd, WM_LAUNCHER_DONE, 2, 0);
    }
}

INT_PTR CALLBACK DlgProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_INITDIALOG: {
        auto* st = reinterpret_cast<DialogState*>(lp);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, lp);
        SetDlgItemTextW(hwnd, IDC_PATH_EDIT, st->opts.localPath.c_str());
        SendDlgItemMessageW(hwnd, IDC_PROGRESS, PBM_SETRANGE32, 0, 10000);
        AppendLog(hwnd, L"AlexWoW Launcher MVP — нажмите «Обновить и запустить».");
        AppendLog(hwnd, L"Источник: " + st->opts.sourcePath);
        AppendLog(hwnd, L"Realm:    " + st->opts.realmAddress);
        return TRUE;
    }

    case WM_COMMAND: {
        auto* st = StateFromHwnd(hwnd);
        switch (LOWORD(wp)) {
        case IDC_LAUNCH: {
            wchar_t buf[512];
            GetDlgItemTextW(hwnd, IDC_PATH_EDIT, buf, 512);
            st->opts.localPath = buf;

            EnableWindow(GetDlgItem(hwnd, IDC_LAUNCH), FALSE);
            EnableWindow(GetDlgItem(hwnd, IDC_PATH_EDIT), FALSE);
            st->worker = std::thread(RunWorker, hwnd, st);
            return TRUE;
        }
        case IDCANCEL:
            st->cancelRequested = true;
            if (st->worker.joinable()) st->worker.detach();
            EndDialog(hwnd, 1);
            return TRUE;
        }
        return FALSE;
    }

    case WM_LAUNCHER_PROGRESS: {
        std::unique_ptr<Progress> p(reinterpret_cast<Progress*>(lp));
        SetDlgItemTextW(hwnd, IDC_STATUS_LABEL, p->status.c_str());
        if (p->bytesTotal > 0) {
            int pos = (int)((p->bytesDone * 10000) / p->bytesTotal);
            SendDlgItemMessageW(hwnd, IDC_PROGRESS, PBM_SETPOS, pos, 0);
        }
        if (!p->status.empty())
            AppendLog(hwnd, p->status);
        return TRUE;
    }

    case WM_LAUNCHER_DONE: {
        auto* st = StateFromHwnd(hwnd);
        if (st->worker.joinable()) st->worker.join();
        if (wp == 0) {
            AppendLog(hwnd, L"Готово. Клиент запущен.");
            // Закрываем лаунчер автоматически через 2с.
            SetTimer(hwnd, 1, 2000, nullptr);
        } else if (wp == 1) {
            AppendLog(hwnd, L"Отменено пользователем.");
            EnableWindow(GetDlgItem(hwnd, IDC_LAUNCH), TRUE);
            EnableWindow(GetDlgItem(hwnd, IDC_PATH_EDIT), TRUE);
        } else {
            EnableWindow(GetDlgItem(hwnd, IDC_LAUNCH), TRUE);
            EnableWindow(GetDlgItem(hwnd, IDC_PATH_EDIT), TRUE);
        }
        return TRUE;
    }

    case WM_TIMER:
        KillTimer(hwnd, 1);
        EndDialog(hwnd, 0);
        return TRUE;

    case WM_CLOSE:
        EndDialog(hwnd, 1);
        return TRUE;
    }
    return FALSE;
}

} // anonymous namespace

int RunMainDialog(const LaunchOptions& defaults) {
    INITCOMMONCONTROLSEX icc{sizeof(icc), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES};
    InitCommonControlsEx(&icc);

    DialogState st;
    st.opts = defaults;

    // Создаём диалог программно (без .rc-template) — проще для одной формы.
    // Конструктор окна:
    HINSTANCE hInst = GetModuleHandleW(nullptr);

    // Регистрируем класс для главного окна
    static const wchar_t* kClassName = L"AlexWowLauncherMain";
    WNDCLASSW wc{};
    wc.lpfnWndProc = DefDlgProcW;
    wc.cbWndExtra = DLGWINDOWEXTRA;
    wc.hInstance = hInst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = kClassName;
    RegisterClassW(&wc);

    // Шаблон диалога создаём через DLGTEMPLATE-структуру runtime. Это типичный
    // паттерн «диалог без .rc»: 8-byte DLGTEMPLATE + контролы (выровненные по DWORD).
    // Для краткости — используем CreateWindowEx + ручное создание контролов.

    int W = 560, H = 400;
    RECT scr; GetWindowRect(GetDesktopWindow(), &scr);
    int x = (scr.right - W) / 2;
    int y = (scr.bottom - H) / 2;

    HWND hwnd = CreateWindowExW(WS_EX_DLGMODALFRAME, kClassName, L"AlexWoW Launcher",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        x, y, W, H, nullptr, nullptr, hInst, nullptr);
    if (!hwnd) return 1;

    SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(&st));

    auto MkLabel = [&](int id, int x, int y, int w, int h, const wchar_t* text) {
        return CreateWindowW(L"STATIC", text, WS_CHILD | WS_VISIBLE, x, y, w, h, hwnd, (HMENU)(INT_PTR)id, hInst, nullptr);
    };
    auto MkEdit = [&](int id, int x, int y, int w, int h, DWORD extra = 0) {
        return CreateWindowW(L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL | extra,
                             x, y, w, h, hwnd, (HMENU)(INT_PTR)id, hInst, nullptr);
    };
    auto MkBtn = [&](int id, int x, int y, int w, int h, const wchar_t* text, DWORD style = 0) {
        return CreateWindowW(L"BUTTON", text, WS_CHILD | WS_VISIBLE | style, x, y, w, h, hwnd, (HMENU)(INT_PTR)id, hInst, nullptr);
    };

    HFONT hFont = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    auto SetFont = [&](HWND h) { SendMessageW(h, WM_SETFONT, (WPARAM)hFont, TRUE); };

    SetFont(MkLabel(0,            12,  12, 540, 18, L"Локальный путь WoW:"));
    HWND hEdit  = MkEdit (IDC_PATH_EDIT, 12,  34, 540, 24);
    SetFont(hEdit);
    SetDlgItemTextW(hwnd, IDC_PATH_EDIT, st.opts.localPath.c_str());

    HWND hStat  = MkLabel(IDC_STATUS_LABEL, 12,  70, 540, 18, L"Готов к запуску");
    SetFont(hStat);
    HWND hProg  = CreateWindowExW(0, PROGRESS_CLASSW, nullptr,
        WS_CHILD | WS_VISIBLE | PBS_SMOOTH, 12, 92, 540, 18, hwnd, (HMENU)(INT_PTR)IDC_PROGRESS, hInst, nullptr);
    (void)hProg;

    HWND hLog = MkEdit(IDC_LOG, 12, 120, 540, 200,
        ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL);
    SetFont(hLog);

    HWND hRun = MkBtn(IDC_LAUNCH, 280, 332, 130, 28, L"Обновить и запустить", BS_DEFPUSHBUTTON);
    SetFont(hRun);
    HWND hCanc= MkBtn(IDCANCEL,   420, 332, 130, 28, L"Отмена");
    SetFont(hCanc);

    // Имитируем WM_INITDIALOG для нашей DlgProc.
    SendMessageW(hwnd, PBM_SETRANGE32, 0, 10000);
    AppendLog(hwnd, L"AlexWoW Launcher MVP — нажмите «Обновить и запустить».");
    AppendLog(hwnd, L"Источник: " + st.opts.sourcePath);
    AppendLog(hwnd, L"Realm:    " + st.opts.realmAddress);

    // Цикл сообщений + обработка через DlgProc (через subclassing). Проще — стандартный loop.
    ShowWindow(hwnd, SW_SHOW);
    UpdateWindow(hwnd);

    // Subclass окна — кладём DlgProc как WndProc:
    SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(DlgProc));

    MSG msg;
    while (IsWindow(hwnd) && GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (!IsDialogMessageW(hwnd, &msg)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    if (st.worker.joinable()) st.worker.join();
    return 0;
}

} // namespace launcher::ui
