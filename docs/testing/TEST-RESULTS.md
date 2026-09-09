# Проверки исходников 2.1.6 — 2026-09-09

- Текущие проверки движка/хранилища: 41/41; записи: 29/29.
- Дополнительные ноды, расписание, отмена, окна, паузы, JSON и bridge: 18/18 сценариев в tests/EngineChecks/PeriodicChecks.cs.
- Все семь tests/Web/Web*.cjs проходят; новый WebPeriodicChecks.cjs содержит 12/12 групп проверок редактора и JSON.
- В браузерном предпросмотре проверены три основные ноды и три условия, два условия у одной ноды, переходы 2 с / 500 мс / 1 с при повторении, обе темы, минимальное окно 900×640 и редактор интервалов.
- Сборка Windows-хоста/EXE и GitHub Actions для 2.1.6 не запускались. NativeFakes не доказывают реальную доставку Windows-ввода. Ручной smoke описан в docs/PERIODIC-ACTIONS.md.

После реорганизации исходников в `src`, проверок в `tests/EngineChecks` и `tests/Web` все перечисленные проверки выполнены повторно и прошли. MSBuild без компиляции подтвердил 22 исходных файла и 7 встроенных ресурсов с прежними LogicalName; файл решения содержит оба проекта.

Команды из корня проекта:

```sh
dotnet run --project tests/EngineChecks/EngineChecks.csproj -c Release
node -e "for(const f of require('fs').readdirSync('tests/Web').filter(f=>/^Web.*[.]cjs$/.test(f))){require('./tests/Web/'+f)}"
git diff --check
```

Исторический отчёт предыдущих выпусков: [архив 2.1.4](../archive/TEST-RESULTS-2.1.4.md). Его результаты Windows-сборок не относятся к 2.1.6.
