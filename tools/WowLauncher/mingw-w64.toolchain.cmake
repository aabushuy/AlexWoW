# CMake toolchain-file для cross-compile WowLauncher.exe из Linux через MinGW-w64.
#
# Использование:
#   cmake -B build -S . -DCMAKE_TOOLCHAIN_FILE=mingw-w64.toolchain.cmake -DCMAKE_BUILD_TYPE=Release
#   cmake --build build -j
#
# Зависимости (apt): mingw-w64 cmake build-essential
# Бинарь: build/WowLauncher.exe — самодостаточный (статическая линковка libgcc/libstdc++).

set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

set(CMAKE_C_COMPILER   x86_64-w64-mingw32-gcc-posix)
set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++-posix)
set(CMAKE_RC_COMPILER  x86_64-w64-mingw32-windres)

set(CMAKE_FIND_ROOT_PATH /usr/x86_64-w64-mingw32)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)
