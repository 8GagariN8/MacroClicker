#!/bin/zsh
set -euo pipefail

DOTNET_CLI="/Users/lyapunov-a/.dotnet/dotnet"
PROJECT_DIR="${0:A:h}"

if [[ ! -x "$DOTNET_CLI" ]]; then
  echo "Не найден .NET SDK: $DOTNET_CLI"
  echo "Сначала установите .NET SDK 8 для macOS Arm64."
  exit 1
fi

# --force обновляет результат restore, включая старые предупреждения в obj.
# Параметры упаковки одинаковы для восстановления и публикации.
BUILD_PROPERTIES=(
  -p:Configuration=Release
  -p:SelfContained=true
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:EnableCompressionInSingleFile=true
  -p:DebugType=None
  -p:DebugSymbols=false
)

echo "Восстановление зависимостей и проверка NuGet Audit..."
if ! "$DOTNET_CLI" restore "$PROJECT_DIR/MacroClicker.csproj" \
  -r win-x64 --force \
  "${BUILD_PROPERTIES[@]}" \
  -p:NuGetAudit=true \
  -p:WarningsAsErrors=NU1900; then
  echo "Восстановление не завершено. При NU1900 проверьте доступ к api.nuget.org и повторите сборку."
  exit 1
fi

"$DOTNET_CLI" publish "$PROJECT_DIR/MacroClicker.csproj" \
  --no-restore \
  -c Release \
  -r win-x64 \
  "${BUILD_PROPERTIES[@]}" \
  -o "$PROJECT_DIR/publish"

echo ""
mkdir -p "$PROJECT_DIR/ready-for-flash"
cp "$PROJECT_DIR/publish/MacroClicker.exe" "$PROJECT_DIR/ready-for-flash/MacroClicker.exe"
cp "$PROJECT_DIR/ИНСТРУКЦИЯ.txt" "$PROJECT_DIR/ready-for-flash/ИНСТРУКЦИЯ.txt"
if [[ -f "$PROJECT_DIR/Runtime/MicrosoftEdgeWebView2RuntimeInstallerX64.exe" ]]; then
  mkdir -p "$PROJECT_DIR/ready-for-flash/Runtime"
  cp "$PROJECT_DIR/Runtime/MicrosoftEdgeWebView2RuntimeInstallerX64.exe" "$PROJECT_DIR/ready-for-flash/Runtime/"
  cp "$PROJECT_DIR/Runtime/README.txt" "$PROJECT_DIR/ready-for-flash/Runtime/"
fi
echo "Готово для переноса на флешку:"
echo "$PROJECT_DIR/ready-for-flash"
