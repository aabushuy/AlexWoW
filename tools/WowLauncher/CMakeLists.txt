cmake_minimum_required(VERSION 3.20)
project(WowLauncher LANGUAGES CXX RC)

# ─── Глобально ─────────────────────────────────────────────────────────────
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    # /MT — статический CRT, без VC redist
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    add_compile_options(/W4 /permissive- /utf-8 /Zc:__cplusplus)
endif()

add_compile_definitions(_UNICODE UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _WIN32_WINNT=0x0601)

# ─── Зависимости ───────────────────────────────────────────────────────────
include(FetchContent)
FetchContent_Declare(
    json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d
)
set(JSON_BuildTests OFF CACHE INTERNAL "")
FetchContent_MakeAvailable(json)

# ─── Лаунчер ───────────────────────────────────────────────────────────────
add_executable(WowLauncher WIN32       # /SUBSYSTEM:WINDOWS, не консольный
    src/main.cpp
    src/ui.cpp
    src/manifest.cpp
    src/hasher.cpp
    src/downloader.cpp
    src/config.cpp
    src/process.cpp
    resources.rc
)

target_include_directories(WowLauncher PRIVATE src)
target_link_libraries(WowLauncher PRIVATE
    nlohmann_json::nlohmann_json
    bcrypt        # SHA-256
    comctl32      # ProgressBar control
    user32 kernel32 shell32
)

# Опции компилятора и линковки под конкретный тоолчейн.
if(MSVC)
    set_target_properties(WowLauncher PROPERTIES LINK_FLAGS "/MANIFEST:NO")
else()
    # MinGW / GCC: -municode обязателен для wWinMain (иначе тянет main, и линкер падает).
    # -static* — самодостаточный .exe без зависимостей от libgcc_s_seh-1.dll / libstdc++-6.dll.
    target_compile_options(WowLauncher PRIVATE -Wall -Wextra -municode)
    target_link_options(WowLauncher PRIVATE -municode -static -static-libgcc -static-libstdc++)
endif()
