# Структура репозитория

Откройте `MacroClicker.sln` в IDE. Приложение находится в `src/MacroClicker/MacroClicker.csproj`, автономные проверки — в `tests/EngineChecks/EngineChecks.csproj`. Пространство имён C# осталось `MacroClicker`; перенос файлов не меняет формат пользовательской библиотеки и поведение приложения.

```text
MacroClicker.sln
src/MacroClicker/
  Program.cs                 # Запуск WebMainForm
  MacroClicker.csproj
  Core/                      # Модель/хранилище, исполнение, расписание, логика записи
  Windows/                   # Windows API: отправка и перехват ввода, окна
  Desktop/                   # WinForms-хост, настройки, журнал, общие диалоги
  Web/                       # Встроенный HTML, bridge модели и политика навигации
  Legacy/                    # Прежний WinForms-редактор; не является точкой входа
  Assets/                    # Иконки и исходные изображения приложения
tests/
  EngineChecks/              # C#-проверки с подменой Windows API
  Web/                       # Проверки JavaScript настоящей HTML-страницы
docs/
  user/                      # Запуск и использование
  development/               # Структура, сборка и релизы
  testing/                   # Текущие результаты и ручные проверки Windows
  examples/                  # JSON-примеры
  releases/                  # Описания версий
  plans/                     # Планы изменений и зафиксированный контракт
  screenshots/               # Иллюстрации документации
  archive/                   # Исторические отчёты и контрольные суммы
scripts/                     # Сборка и упаковка
packaging/WebView2/          # Инструкция и необязательный локальный установщик
artifacts/                   # Результаты сборки, исключены из Git
```

`Legacy` сохранён для истории и совместной компиляции. Рабочий интерфейс запускается через `Program.cs` → `Desktop/WebMainForm.cs` → встроенный `Web/index.html`. Изменять старый редактор вместо него не нужно.

Документы встроенной справки хранятся в `docs`, а проект подключает их по относительным путям с прежними `LogicalName`. Иконка остаётся `MacroClicker.AppIcon`, страница — `MacroClicker.Web.index.html`.

## Проверки

Команды выполняются из корня репозитория. Нужны .NET SDK 8 и Node.js.

```sh
dotnet run --project tests/EngineChecks/EngineChecks.csproj -c Release
node -e "for(const f of require('fs').readdirSync('tests/Web').filter(f=>/^Web.*[.]cjs$/.test(f))){require('./tests/Web/'+f)}"
```

Просмотр HTML без Windows-ввода:

```sh
python3 -m http.server 8000 --bind 127.0.0.1 --directory src/MacroClicker/Web
```

Откройте `http://127.0.0.1:8000`. Предпросмотр не читает настоящую библиотеку макросов и не отправляет клавиши.

## Сборка и выходные файлы

Основной способ сборки и выпуска — [GitHub Actions](RELEASING.md). Скрипты ищут корень относительно собственного расположения и не зависят от текущей рабочей папки:

- `scripts/build-windows.ps1` — сборка Windows x64;
- `scripts/build-macos.sh` — дополнительный вариант с macOS; использует `dotnet` из PATH либо путь из переменной `DOTNET_CLI`;
- `scripts/package-release.ps1 -Version 2.1.6` — проверка EXE, создание пакета и SHA-256.

Результаты: `artifacts/publish`, `artifacts/portable` и `artifacts/release`. Скрипты создают только локальные каталоги; сами ничего не копируют на флешку. Установщик WebView2 при необходимости помещается локально в `packaging/WebView2` и исключён из Git. Сохраняемые в профиле Windows макросы и настройки не относятся к этим каталогам.
