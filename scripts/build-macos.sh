#!/bin/zsh
set -euo pipefail

DOTNET_CLI="${DOTNET_CLI:-dotnet}"
PROJECT_DIR="${0:A:h:h}"

if ! command -v "$DOTNET_CLI" >/dev/null 2>&1; then
  echo "Не найден .NET SDK: $DOTNET_CLI"
  echo "Установите .NET SDK 8 или задайте DOTNET_CLI с путём к dotnet."
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
if ! "$DOTNET_CLI" restore "$PROJECT_DIR/src/MacroClicker/MacroClicker.csproj" \
  -r win-x64 --force \
  "${BUILD_PROPERTIES[@]}" \
  -p:NuGetAudit=true \
  -p:WarningsAsErrors=NU1900; then
  echo "Восстановление не завершено. При NU1900 проверьте доступ к api.nuget.org и повторите сборку."
  exit 1
fi

"$DOTNET_CLI" publish "$PROJECT_DIR/src/MacroClicker/MacroClicker.csproj" \
  --no-restore \
  -c Release \
  -r win-x64 \
  "${BUILD_PROPERTIES[@]}" \
  -o "$PROJECT_DIR/artifacts/publish"

echo ""
mkdir -p "$PROJECT_DIR/artifacts/portable"
cp "$PROJECT_DIR/artifacts/publish/MacroClicker.exe" "$PROJECT_DIR/artifacts/portable/MacroClicker.exe"
cp "$PROJECT_DIR/docs/user/QUICKSTART.txt" "$PROJECT_DIR/artifacts/portable/ИНСТРУКЦИЯ.txt"
if [[ -f "$PROJECT_DIR/packaging/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe" ]]; then
  mkdir -p "$PROJECT_DIR/artifacts/portable/Runtime"
  cp "$PROJECT_DIR/packaging/WebView2/MicrosoftEdgeWebView2RuntimeInstallerX64.exe" "$PROJECT_DIR/artifacts/portable/Runtime/"
  cp "$PROJECT_DIR/packaging/WebView2/README.txt" "$PROJECT_DIR/artifacts/portable/Runtime/"
fi
echo "Готово для переноса на флешку:"
echo "$PROJECT_DIR/artifacts/portable"
