<#
  Прогон проверок клиентских аддонов AlexWoW БЕЗ запуска клиента WoW:
    1) luacheck — статический анализ (синтаксис, неизвестные глобалы, утечки, мёртвый код)
    2) luajit   — headless-тесты логики (tools/addons/tests/*_spec.lua)

  Запуск:  pwsh tools/addons/run-checks.ps1   (или из любой папки — пути относительны скрипту)
  Код выхода != 0 — если линт или любой spec упал (пригодно для CI).

  Тулчейн (в репозиторий не коммитится, ставится разово):
    winget install DEVCOM.LuaJIT
    luacheck.exe — https://github.com/lunarmodules/luacheck/releases  (положить в PATH)
#>
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Resolve-Tool($name, [string[]]$candidates) {
  $cmd = Get-Command $name -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  foreach ($c in $candidates) { if (Test-Path $c) { return (Resolve-Path $c).Path } }
  throw "$name не найден. Поставь тулчейн (см. шапку run-checks.ps1)."
}

$luacheck = Resolve-Tool "luacheck" @("$env:LOCALAPPDATA\Programs\luacheck\luacheck.exe")
$luajit   = Resolve-Tool "luajit"   @("$env:LOCALAPPDATA\Programs\LuaJIT\bin\luajit.exe")

$fail = 0

Write-Host "== luacheck ==" -ForegroundColor Cyan
& $luacheck $root --config "$root\.luacheckrc"
if ($LASTEXITCODE -ne 0) { $fail = 1 }

Write-Host "`n== headless-тесты ==" -ForegroundColor Cyan
$specs = Get-ChildItem -Path "$root\tests" -Filter "*_spec.lua" -ErrorAction SilentlyContinue
foreach ($spec in $specs) {
  Write-Host "-- $($spec.Name)" -ForegroundColor DarkCyan
  & $luajit $spec.FullName
  if ($LASTEXITCODE -ne 0) { $fail = 1 }
}

if ($fail -ne 0) { Write-Host "`nПРОВАЛ" -ForegroundColor Red; exit 1 }
Write-Host "`nВСЁ ЗЕЛЁНОЕ" -ForegroundColor Green
