# Искра

Свой язык программирования на русском и среда для создания интерактивных проектов и игр. Редактор для Windows, мобильный клиент для Android, общий компилятор и исполнитель в ядре.

## Состав репозитория

| Путь | Назначение |
|---|---|
| `IskraStudio` | WPF-редактор для Windows: проекты, сцены, объекты, образы, звуки, Paintroid, запуск игры, диагностика |
| `Iskra.Core` | Компилятор языка (`ScriptCompiler`) и исполнитель (`ScriptEngine`), хранилище проектов, ресурсы, архивы `.iskp`, политика запуска сцен, движок рисования |
| `Iskra.Android` | MAUI-клиент для Android: библиотека проектов, сцены, объекты, Paintroid, игровой экран с паузой и отладкой |
| `tests` | xUnit-тесты, PowerShell-проверки интерфейса и тесты C++-интерпретатора |
| `design` | Исходные SVG-иконки и инструмент сборки иконок |
| `main.cpp` | Консольный интерпретатор языка (легаси-диалект) |
| `example-studio.isk` | Пример скрипта для IskraStudio |
| `example.isk` | Пример скрипта для консольного интерпретатора |

## Архитектура рантайма

Компилятор и исполнитель — один на обе платформы и живут в `Iskra.Core`. Windows и Android компилируют одни и те же скрипты в одинаковые списки команд и исполняют их общим движком (`ScriptEngine`): события, переменные, условия, циклы, звук, сцены со снапшотами, физика, касания. Платформенные хосты отвечают только за отрисовку, ввод и звук: WPF рисует в отдельном окне, Android — на игровом экране с паузой и Debug menu. Перед запуском на Android показывается список команд и событий, которые мобильный хост пока не поддерживает.

## Быстрый старт

### Windows

```powershell
dotnet build Iskra.sln
dotnet run --project IskraStudio\IskraStudio.csproj
```

Требуется .NET 8 SDK и Windows.

### Android

```powershell
dotnet workload install maui-android
dotnet build Iskra.Android\Iskra.Android.csproj -f net10.0-android
```

Требуется .NET 10 SDK с рабочей нагрузкой `maui-android`.

### Консольный интерпретатор

```powershell
cl /std:c++17 /O2 /EHsc /utf-8 main.cpp /Fe:iskra-runtime.exe
chcp 65001
.\iskra-runtime.exe example.isk
```

Или через `g++`:

```sh
g++ -std=c++17 -O2 -o iskra main.cpp
./iskra example.isk
```

## Язык

Полное описание команд студийного диалекта — в `Язык Искры — новые команды.txt` и `DESIGN.md`. Пример скрипта студийного диалекта — `example-studio.isk`; пример консольного диалекта — `example.isk`.

## Тесты

```powershell
dotnet test tests\IskraStudio.Tests.csproj
```

PowerShell-проверки интерфейса:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\diagnostics_background_navigation_test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\runtime_overlay_layout_test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\sound_command_syntax_test.ps1
```

Тесты C++-интерпретатора:

```powershell
cl /std:c++17 /EHsc /utf-8 tests\interpreter_tests.cpp /Fe:iskra-tests.exe
.\iskra-tests.exe
```

## Данные приложения

Windows: `%LOCALAPPDATA%\IskraStudio`.
Android: `FileSystem.AppDataDirectory\IskraStudio`.
Переменная окружения `ISKRA_DATA_DIR` переопределяет каталог данных.

## Лицензия

Собственный код проекта распространяется по лицензии MIT. Сторонние компоненты и шрифты используют собственные лицензии; см. `THIRD-PARTY-NOTICES.md` и `LICENSE`.
