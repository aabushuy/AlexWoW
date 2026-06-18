#include "downloader.h"
#include "hasher.h"

#include <windows.h>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <vector>

namespace launcher::downloader {

namespace {

// Переводит POSIX-стиль "Data/common.MPQ" в платформенный путь под root.
std::filesystem::path Resolve(const std::wstring& root, const std::string& rel) {
    std::filesystem::path p(root);
    std::filesystem::path r(rel);  // utf-8 → path
    return p / r;
}

std::wstring ToWide(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), w.data(), n);
    return w;
}

// Копирование с CALLBACK-прогрессом. Один файл может быть большим (Data/*.MPQ ~2-3 ГБ),
// поэтому используем CopyFileExW который дёргает PROGRESS_ROUTINE по мере прохода.
struct CopyContext {
    long long base_bytes_done;     // байт скопировано до начала этого файла
    long long total_bytes_to_copy; // общий бюджет (для процента)
    int files_done;
    int files_total;
    std::wstring current_file;
    const ProgressFn* report;
    const std::atomic<bool>* cancel;
    long long last_reported = 0;
};

DWORD CALLBACK CopyProgressCb(
    LARGE_INTEGER /*TotalFileSize*/, LARGE_INTEGER TotalBytesTransferred,
    LARGE_INTEGER /*StreamSize*/, LARGE_INTEGER /*StreamBytesTransferred*/,
    DWORD /*StreamNumber*/, DWORD /*CallbackReason*/, HANDLE /*hSrc*/, HANDLE /*hDst*/,
    LPVOID lpData)
{
    auto* ctx = reinterpret_cast<CopyContext*>(lpData);
    if (*ctx->cancel) return PROGRESS_CANCEL;

    long long abs_done = ctx->base_bytes_done + TotalBytesTransferred.QuadPart;
    // Дросселируем: отчёт раз в ~4 МБ, чтобы не флудить UI.
    if (abs_done - ctx->last_reported < (4 << 20))
        return PROGRESS_CONTINUE;
    ctx->last_reported = abs_done;

    if (*ctx->report) {
        Progress p{};
        p.files_done = ctx->files_done;
        p.files_total = ctx->files_total;
        p.bytes_done = abs_done;
        p.bytes_total = ctx->total_bytes_to_copy;
        p.current_file = ctx->current_file;
        (*ctx->report)(p);
    }
    return PROGRESS_CONTINUE;
}

void CopyOne(const std::filesystem::path& src, const std::filesystem::path& dst,
             CopyContext* ctx)
{
    std::error_code ec;
    std::filesystem::create_directories(dst.parent_path(), ec);

    BOOL cancel = FALSE;
    if (!CopyFileExW(src.wstring().c_str(), dst.wstring().c_str(),
                     CopyProgressCb, ctx, &cancel,
                     0 /* flags: разрешить перезапись */))
    {
        DWORD err = GetLastError();
        if (err == ERROR_REQUEST_ABORTED) return;  // отменено пользователем
        throw std::runtime_error("CopyFileExW failed (" + std::to_string(err) + ") для " + dst.string());
    }
}

} // anonymous namespace

void Sync(const std::wstring& source_root,
          const std::wstring& local_root,
          const manifest::Manifest& m,
          const ProgressFn& report,
          const std::atomic<bool>& cancel)
{
    namespace fs = std::filesystem;

    // 1) Сначала проходим всё дерево и набираем «to-copy» список.
    struct Job { fs::path src; fs::path dst; long long size; std::string rel; };
    std::vector<Job> jobs;
    long long total_to_copy = 0;

    int idx = 0;
    for (const auto& e : m.files) {
        if (cancel) return;
        ++idx;
        fs::path src = Resolve(source_root, e.path);
        fs::path dst = Resolve(local_root, e.path);

        std::error_code ec;
        bool exists = fs::exists(dst, ec);
        long long local_size = exists ? (long long)fs::file_size(dst, ec) : -1;
        bool need_copy = !exists || local_size != e.size;

        // Размер совпал — проверим хеш. Это медленно (SHA по большому MPQ — десятки сек),
        // но только если размер уже подозрительно совпал. На повторных запусках с целым
        // клиентом проход быстрый.
        if (!need_copy) {
            try {
                std::string local_hash = hasher::Sha256File(dst);
                if (local_hash != e.sha256)
                    need_copy = true;
            } catch (...) {
                need_copy = true;
            }
        }

        if (need_copy) {
            jobs.push_back({src, dst, e.size, e.path});
            total_to_copy += e.size;
        }

        // Отчёт о прогрессе фазы «сверка»: считаем по числу файлов.
        if (report && (idx % 25 == 0 || idx == (int)m.files.size())) {
            Progress p{};
            p.files_done = idx;
            p.files_total = (int)m.files.size();
            p.bytes_done = 0;
            p.bytes_total = total_to_copy > 0 ? total_to_copy : 1;
            p.current_file = ToWide("Сверка: " + e.path);
            report(p);
        }
    }

    if (jobs.empty()) {
        if (report) {
            Progress p{};
            p.files_done = (int)m.files.size();
            p.files_total = (int)m.files.size();
            p.bytes_total = 1; p.bytes_done = 1;
            p.current_file = L"Все файлы актуальны.";
            report(p);
        }
        return;
    }

    // 2) Копируем.
    CopyContext ctx{};
    ctx.total_bytes_to_copy = total_to_copy;
    ctx.files_total = (int)jobs.size();
    ctx.report = &report;
    ctx.cancel = &cancel;

    for (auto& j : jobs) {
        if (cancel) return;
        ctx.current_file = ToWide(j.rel);
        ctx.files_done++;
        ctx.last_reported = ctx.base_bytes_done;
        CopyOne(j.src, j.dst, &ctx);
        ctx.base_bytes_done += j.size;
    }
}

} // namespace launcher::downloader
