#include "hasher.h"

#include <windows.h>
#include <bcrypt.h>
#include <stdexcept>
#include <vector>

#ifndef NT_SUCCESS
#define NT_SUCCESS(s) ((NTSTATUS)(s) >= 0)
#endif

namespace launcher::hasher {

namespace {

class BCryptAlg {
public:
    BCryptAlg() {
        if (!NT_SUCCESS(BCryptOpenAlgorithmProvider(&h_, BCRYPT_SHA256_ALGORITHM, nullptr, 0)))
            throw std::runtime_error("BCryptOpenAlgorithmProvider failed");
    }
    ~BCryptAlg() { if (h_) BCryptCloseAlgorithmProvider(h_, 0); }
    BCRYPT_ALG_HANDLE h() const { return h_; }
private:
    BCRYPT_ALG_HANDLE h_ = nullptr;
};

std::string ToHex(const unsigned char* data, size_t len) {
    static const char k[] = "0123456789abcdef";
    std::string out(len * 2, '\0');
    for (size_t i = 0; i < len; ++i) {
        out[2 * i]     = k[(data[i] >> 4) & 0xF];
        out[2 * i + 1] = k[data[i] & 0xF];
    }
    return out;
}

} // anonymous namespace

std::string Sha256File(const std::filesystem::path& path) {
    BCryptAlg alg;

    BCRYPT_HASH_HANDLE hHash = nullptr;
    if (!NT_SUCCESS(BCryptCreateHash(alg.h(), &hHash, nullptr, 0, nullptr, 0, 0)))
        throw std::runtime_error("BCryptCreateHash failed");
    struct HashGuard { BCRYPT_HASH_HANDLE h; ~HashGuard(){ if(h) BCryptDestroyHash(h); } } guard{hHash};

    // Открываем файл через WinAPI — std::ifstream на сетевых путях у MinGW бывает капризен.
    HANDLE hFile = CreateFileW(path.wstring().c_str(), GENERIC_READ, FILE_SHARE_READ,
                               nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
        throw std::runtime_error("CreateFileW failed (" + std::to_string(GetLastError()) + ")");
    struct FileGuard { HANDLE h; ~FileGuard(){ if(h && h!=INVALID_HANDLE_VALUE) CloseHandle(h); } } fg{hFile};

    std::vector<unsigned char> buf(1 << 16);  // 64 КБ
    DWORD n = 0;
    while (true) {
        if (!ReadFile(hFile, buf.data(), (DWORD)buf.size(), &n, nullptr))
            throw std::runtime_error("ReadFile failed (" + std::to_string(GetLastError()) + ")");
        if (n == 0) break;
        if (!NT_SUCCESS(BCryptHashData(hHash, buf.data(), n, 0)))
            throw std::runtime_error("BCryptHashData failed");
    }

    unsigned char digest[32];
    if (!NT_SUCCESS(BCryptFinishHash(hHash, digest, sizeof(digest), 0)))
        throw std::runtime_error("BCryptFinishHash failed");
    return ToHex(digest, sizeof(digest));
}

} // namespace launcher::hasher
