# NU1900: причина и исправление

Дата проверки: 2026-09-09. Проект MacroClicker 2.0, .NET SDK 8.0.424, macOS.

## Наблюдения

1. Проверка api.nuget.org из песочницы завершалась «curl: (6) Could not resolve host: api.nuget.org».
2. Тот же адрес вне сетевой песочницы ответил HTTP 200. Это не требовало отключения TLS-проверки или изменения системных сетевых настроек.
3. До исправления NU1900 присутствовала в obj/project.assets.json и obj/project.nuget.cache.
4. Предыдущий обычный повтор публикации сообщал «All projects are up-to-date for restore» и вновь выводил сохранённую NU1900.
5. При обычной компиляции в проекте нет PackageReference. При PublishSingleFile=true SDK добавляет служебную зависимость Microsoft.NET.ILLink.Tasks 8.0.30, поэтому восстановление для обычной компиляции недостаточно для проверки режима публикации.

## Выполненное восстановление

В каталоге проекта, с сетевым доступом:

    /Users/lyapunov-a/.dotnet/dotnet restore MacroClicker.csproj --runtime win-x64 --force -p:RestoreNoHttpCache=true -p:SelfContained=true -p:PublishSingleFile=true -p:NuGetAudit=true -p:WarningsAsErrors=NU1900 --verbosity normal

Вывод подтверждает HTTP GET/OK для:

- https://api.nuget.org/v3/vulnerabilities/index.json
- https://api.nuget.org/v3-vulnerabilities/2026.09.02.23.52.57/vulnerability.base.json
- https://api.nuget.org/v3-vulnerabilities/2026.09.02.23.52.57/2026.09.08.14.43.59/vulnerability.update.json

Результат: exit 0, 0 Warning(s), 0 Error(s). В новом project.nuget.cache — success: true и logs: []. Аудит включён, сообщений об уязвимостях восстановленных PackageReference не получено. Это не является отдельным аудитом безопасности исходного кода или встроенного .NET Runtime.

## Изменения в скриптах

build-macos.sh и build-windows.ps1 теперь выполняют:

1. Явный restore --force с теми же параметрами self-contained/single-file, которые используются при публикации.
2. NuGetAudit=true; NU1900 считается ошибкой, чтобы при недоступном аудите скрипт не сообщал о полностью успешной сборке.
3. publish --no-restore после успешного восстановления.

--force перепроверяет зависимости вместо переиспользования старого результата restore. Обычный HTTP-кэш NuGet остаётся доступен; глобальные кэши и настройки других проектов не удалялись.

Финальный ./build-macos.sh с сетевым доступом завершился успешно без NU1900 и других предупреждений. Синтаксис zsh-скрипта проверен через zsh -n. PowerShell-скрипт обновлён аналогично, но на Windows в этой сессии не запускался.

## Если ошибка вернётся

Проверьте сетевой доступ к api.nuget.org и повторите скрипт. Отсутствие сети в песочнице нельзя исправить настройкой проекта: запуск restore должен иметь сетевое разрешение. NuGetAudit=false, NoWarn=NU1900 и отключение проверки сертификатов не применялись.

Справка Microsoft: [NU1900](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1900), [NuGet Audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).
