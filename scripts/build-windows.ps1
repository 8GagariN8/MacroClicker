$ErrorActionPreference = "Stop"

Write-Host "Проверка .NET SDK..."
dotnet --version
if ($LASTEXITCODE -ne 0) { throw "Не удалось запустить .NET SDK." }

$root = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $root "src/MacroClicker/MacroClicker.csproj"
$buildProperties = @(
  "-p:Configuration=Release"
  "-p:SelfContained=true"
  "-p:PublishSingleFile=true"
  "-p:IncludeNativeLibrariesForSelfExtract=true"
  "-p:EnableCompressionInSingleFile=true"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

Write-Host "Восстановление зависимостей и проверка NuGet Audit..."
dotnet restore $projectPath -r win-x64 --force @buildProperties -p:NuGetAudit=true -p:WarningsAsErrors=NU1900
if ($LASTEXITCODE -ne 0) {
  throw "Восстановление не завершено. При NU1900 проверьте доступ к api.nuget.org и повторите сборку."
}

Write-Host "Сборка переносимого EXE для Windows x64..."
dotnet publish $projectPath --no-restore -c Release -r win-x64 @buildProperties -o (Join-Path $root "artifacts/publish")
if ($LASTEXITCODE -ne 0) { throw "Сборка MacroClicker завершилась ошибкой." }

$portablePath = Join-Path $root "artifacts/portable"
New-Item -ItemType Directory -Path $portablePath -Force | Out-Null
Copy-Item (Join-Path $root "artifacts/publish/MacroClicker.exe") (Join-Path $portablePath "MacroClicker.exe") -Force
Copy-Item (Join-Path $root "docs/user/QUICKSTART.txt") (Join-Path $portablePath "ИНСТРУКЦИЯ.txt") -Force
if (Test-Path (Join-Path $root "packaging/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe")) {
  New-Item -ItemType Directory -Path (Join-Path $portablePath "Runtime") -Force | Out-Null
  Copy-Item (Join-Path $root "packaging/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe") (Join-Path $portablePath "Runtime") -Force
  Copy-Item (Join-Path $root "packaging/WebView2/README.txt") (Join-Path $portablePath "Runtime") -Force
}

Write-Host ""
Write-Host "Готово для переноса: $portablePath"
