#!/usr/bin/env python3
"""
Генератор manifest.json для C++ лаунчера WoW 3.3.5a.

Рекурсивно проходит каталог клиента, считает SHA-256 каждого файла, пишет манифест
рядом с клиентом (`manifest.json` в корне каталога). Лаунчер читает этот манифест,
сверяет локальные файлы и докачивает только отличающиеся.

Использование:
    python tools/WowLauncher/generate-manifest.py "Z:\\client\\WoW335"
    python tools/WowLauncher/generate-manifest.py "\\\\homeserver\\WowProject\\client\\WoW335"

Запускать **на той же машине, где лежит клиент** (sm-чтение всего клиента, 30+ ГБ —
по сети медленно). На homeserver: через ssh + python.
"""
from __future__ import annotations
import argparse, hashlib, json, sys, time
from pathlib import Path

# Файлы/папки, которые НЕ включаем в манифест (волатильно, генерируется клиентом).
EXCLUDE_DIRS = {"WTF", "Cache", "Errors", "Logs", "Screenshots"}
EXCLUDE_FILES = {"manifest.json"}  # сам манифест


def sha256_of(path: Path, *, block: int = 1 << 16) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        while chunk := f.read(block):
            h.update(chunk)
    return h.hexdigest()


def collect(root: Path):
    """Yield Path-объектов: все файлы дерева, кроме EXCLUDE_DIRS / EXCLUDE_FILES."""
    for p in root.rglob("*"):
        if not p.is_file():
            continue
        rel = p.relative_to(root)
        # пропуск если хоть один сегмент пути — в EXCLUDE_DIRS
        if any(part in EXCLUDE_DIRS for part in rel.parts[:-1]):
            continue
        if rel.name in EXCLUDE_FILES:
            continue
        yield p, rel


def main() -> int:
    p = argparse.ArgumentParser(description="Generate manifest.json for WoW client.")
    p.add_argument("client_dir", help="Корень клиента (содержит Wow.exe, Data/)")
    p.add_argument("--out", help="Куда писать manifest.json (default: <client_dir>/manifest.json)")
    p.add_argument("--version", default="3.3.5a-12340", help="Версия клиента (метаданные манифеста)")
    args = p.parse_args()

    root = Path(args.client_dir).resolve()
    if not root.is_dir():
        print(f"[!] Не каталог: {root}", file=sys.stderr)
        return 1
    if not (root / "Wow.exe").exists():
        print(f"[!] {root} не похож на WoW-клиент (нет Wow.exe)", file=sys.stderr)
        return 1

    out_path = Path(args.out) if args.out else (root / "manifest.json")

    print(f"[*] Сканирую {root}...")
    files = list(collect(root))
    total_size = sum(p.stat().st_size for p, _ in files)
    print(f"    файлов: {len(files)}; общий размер: {total_size / (1024 ** 3):.2f} ГБ")

    entries = []
    bytes_done = 0
    t0 = time.time()
    for i, (path, rel) in enumerate(files, 1):
        size = path.stat().st_size
        digest = sha256_of(path)
        # Хранится POSIX-стиль (forward slashes) — лаунчер сам нормализует под платформу.
        entries.append({
            "path": str(rel).replace("\\", "/"),
            "sha256": digest,
            "size": size,
        })
        bytes_done += size
        if i % 50 == 0 or i == len(files):
            elapsed = time.time() - t0
            speed = bytes_done / elapsed / (1024 * 1024) if elapsed > 0 else 0
            print(f"    [{i:4d}/{len(files)}] {bytes_done / (1024**3):6.2f} ГБ · {speed:6.1f} МБ/с")

    manifest = {
        "version": args.version,
        "source": str(root).replace("\\", "/"),
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "total_size": total_size,
        "files": sorted(entries, key=lambda e: e["path"]),
    }
    out_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n[+] {len(entries)} файлов записано в {out_path}")
    print(f"    размер манифеста: {out_path.stat().st_size / 1024:.1f} КБ")
    return 0


if __name__ == "__main__":
    sys.exit(main())
