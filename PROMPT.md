# Промт для создания проекта TaskbarFold

Готовый промт для агента (Claude Code, Cursor, аналоги). Скопировать целиком
и выдать в пустой папке репозитория. Детали архитектуры и полный список
задач — в `PROJECT.md`, который лежит рядом.

---

## Промт

Ты создаёшь десктопное приложение **TaskbarFold** для Windows 10/11.

### Что это

Утилита, которая добавляет «мини-папки» в панель задач Windows. Пользователь
создаёт группу, закрепляет её на панели задач, и по клику всплывает компактное
окно с иконками приложений — как папки на iPhone. Клик по иконке запускает
приложение и закрывает окно.

Цель — заменить заброшенный `tjackenpacken/taskbar-groups` (последний коммит
январь 2021, WinForms, .NET Framework 4.7.2) на живой аналог без его недостатков.

### Стек — соблюдать строго

- .NET 8, C# 12, `nullable` включён, `TreatWarningsAsErrors` включён
- WPF (не WinForms, не WinUI 3, не MAUI)
- CommunityToolkit.Mvvm — для ViewModel и команд
- H.NotifyIcon.Wpf — иконка в трее
- System.Text.Json с генератором исходников — для конфига
- xUnit + FluentAssertions — тесты
- Публикация: `win-x64`, self-contained, single-file

### Структура решения

```
TaskbarFold.sln
├── src/TaskbarFold.Core/     доменная логика, БЕЗ ссылок на WPF
│   ├── Models/               Group, Shortcut, AppSettings
│   ├── Services/             GroupRepository, ShortcutResolver, IconCache
│   └── Interop/              ShellLink, IconExtractor, TaskbarInfo, AppxResolver
├── src/TaskbarFold.App/      WPF: окна, ViewModel, трей, точка входа
├── tests/TaskbarFold.Tests/  xUnit по Core
└── Directory.Build.props
```

Жёсткое правило: `TaskbarFold.Core` не ссылается на WPF и не открывает окон.
Interop-обёртки возвращают чистые модели, а не хендлы. Любой `MessageBox`,
`Window` или `Dispatcher` в Core — ошибка.

### Режимы запуска

```
TaskbarFold.exe                 → окно настроек, резидент садится в трей
TaskbarFold.exe --group <GUID>  → сигнал резиденту показать всплывашку
```

Второй режим не создаёт UI вообще: подключается к named pipe
`TaskbarFold.{SID}`, отправляет GUID и позицию курсора, завершается.
Резидент показывает предсозданное скрытое окно. Если резидент не отвечает —
запускает его и повторяет запрос один раз.
