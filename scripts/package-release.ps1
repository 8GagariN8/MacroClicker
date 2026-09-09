param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'artifacts/publish/MacroClicker.exe'
if (!(Test-Path $exe) -or (Get-Item $exe).Length -lt 1MB) { throw 'Published executable is missing or empty.' }
$actual = (Get-Item $exe).VersionInfo.ProductVersion.Split('+')[0]
if ($actual -ne $Version) { throw "Executable version $actual does not match $Version." }
$package = Join-Path $root 'artifacts/release'
if (Test-Path $package) { throw 'Use a clean checkout: artifacts/release already exists.' }
New-Item -ItemType Directory $package | Out-Null
Copy-Item $exe (Join-Path $package 'MacroClicker.exe')
$instructions = @"
MACROCLICKER $Version — WINDOWS X64

1. Download MacroClicker.exe from this release and save it to a local folder.
2. Run MacroClicker.exe. Installing .NET is not required.
3. Microsoft WebView2 Runtime is required. If it is missing, install the x64
   Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/
   and run MacroClicker again. Do not reinstall it if it is already present.

Only MacroClicker.exe is needed from this release. Source code archives are for
developers. SHA256.txt contains optional file integrity checksums.
For usage, click «Справка» inside the application. UI language: Russian.
Macros and settings remain in %LOCALAPPDATA%\MacroClicker when updating the EXE.

УДАЛЕНИЕ: корзина справа от названия в списке «Мои макросы» → подтверждение.
Удалённые макросы хранятся 30 дней. В разделе «Корзина» их можно восстановить
или удалить навсегда кнопкой «Очистить корзину…» с подтверждением.
Просроченные файлы удаляются при запуске/открытии корзины и раз в час работы приложения.
Закрытое приложение ничего не удаляет. Папка: %LOCALAPPDATA%\MacroClicker\Macros\Deleted.
"@
[IO.File]::WriteAllText((Join-Path $package 'INSTRUCTIONS.txt'), $instructions.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
$checksums = foreach ($name in @('MacroClicker.exe', 'INSTRUCTIONS.txt')) {
    $hash = (Get-FileHash (Join-Path $package $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllText((Join-Path $package 'SHA256.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$notesFile = Join-Path $root "docs/releases/$Version.md"
if (!(Test-Path $notesFile)) { throw "Add release notes: docs/releases/$Version.md" }
Copy-Item $notesFile (Join-Path $package 'RELEASE-NOTES.md')
