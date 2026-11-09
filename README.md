# Quick Orbit

!!!ВНИМАНИЕ: Данный проект создан с помощью AI DeepSeek!!!

Орбитальный радиальный лаунчер для Windows 10/11. WPF, C# 12, .NET 8.
Прозрачный оверлей с кольцами, иконками, глубиной, hover-свечением и параллаксом.
Глобальный hotkey по умолчанию — `Ctrl+Alt+Space`. Настройки и трей — через
значок Quick Orbit в области уведомлений.

## Сборка

```powershell
dotnet publish QuickOrbit.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

Готовый запуск: `publish\QuickOrbit.exe`. Рядом должны лежать пять нативных
библиотек WPF (`D3DCompiler_47_cor3.dll`, `wpfgfx_cor3.dll`,
`PresentationNative_cor3.dll`, `PenImc_cor3.dll`, `vcruntime140_cor3.dll`).
Это self-contained x64 сборка без консоли и без установки .NET.

Именно такой вариант нужен на машинах, где распаковка настоящего single-file
`.exe` в `%TEMP%` блокируется политикой или антивирусом: здесь нативные
библиотеки загружаются рядом с `.exe`, и распаковка не требуется.

Компактный framework-dependent вариант (нужен установленный .NET 8 Desktop Runtime):

```powershell
dotnet publish QuickOrbit.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:DebugType=None -o publish-fd
```

## Запуск

```powershell
.\publish\QuickOrbit.exe
```

При ручном запуске меню показывается сразу. Повторный запуск не создаёт второй
экземпляр: он лишь переключает видимость меню у уже работающего приложения.

## Данные

- Конфигурация: `%APPDATA%\QuickOrbit\config.json`
- Кэш иконок и favicon: `%APPDATA%\QuickOrbit\icons`
- Лог: `%APPDATA%\QuickOrbit\quickorbit.log`
- Если `%APPDATA%` недоступен для записи, приложение автоматически использует
  `%LOCALAPPDATA%\QuickOrbit`, а затем папку `QuickOrbitData` рядом с `.exe`.
  Фактический путь виден в логе и в предупреждении трея.
- Переопределение каталога данных: переменная окружения `QUICKORBIT_DATA`

При первом запуске создаётся конфигурация с двумя орбитами. Пример лежит в
`config.example.json`.

## Управление

- `Ctrl+Alt+Space` — показать/скрыть меню (если сочетание занято, приложение
  временно использует `Ctrl+Alt+Shift+Space` и показывает предупреждение).
- Клик по пустому месту оверлея, `Esc` или клик вне меню — скрыть.
- Перетащите файлы, папки или ссылки на оверлей — они добавятся в ближайшую
  орбиту. То же можно сделать в таблице настроек: элементы добавятся в
  выбранную орбиту. Favicon для ссылок скачивается автоматически.
- Размер иконок (32–96 px) задаётся отдельно для каждой орбиты в её блоке настроек.
  Галочка «Применять ко всем орбитам» синхронизирует изменение размера.
- В блоке «Иконки» выбирается общая форма: скруглённый квадрат или круг.
- Трей: «Настройки», «Показать/скрыть», «Выход».
- `--settings` — сразу открыть настройки.
- Автозапуск не используется: приложение не прописывается в реестр и не создаёт
  заданий. Запускайте `QuickOrbit.exe` вручную или положите ярлык в папку
  «Автозагрузка» средствами Windows.
