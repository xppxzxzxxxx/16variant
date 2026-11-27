# Автошкола · УП.02 · вариант 16

Учебное приложение для учёта практических занятий по вождению. WPF-клиент запускает локальный ASP.NET Core API на HTTPS и сохраняет данные в SQLite. При первом запуске на стандартном пути создаются четыре пользователя, три ученика и семь занятий.

## Запуск

- **Автономный клиент Windows x64.** Распакуйте ZIP целиком в одну папку и запустите DrivingSchool.Desktop.exe.
- **Из исходников.** Откройте DrivingSchool.sln в Visual Studio 2026 (.NET 10) или в актуальной Visual Studio 2022 17.14 с .NET 8 SDK и рабочей нагрузкой «Разработка классических приложений .NET». Выберите профиль «Автошкола» и нажмите F5. Если Visual Studio запомнила отдельный API, в обозревателе решений назначьте DrivingSchool.Desktop запускаемым проектом. Профиль запускает WPF, который поднимает сервер на свободном порту.
- **Из терминала Windows.** Выполните dotnet run --project DrivingSchool.Desktop.
- **Отдельный API.** Доверяйте сертификату разработки через dotnet dev-certs https --trust. Запустите dotnet run --project DrivingSchool.Api --launch-profile "Только HTTPS API", затем используйте Swagger и requests.http. Для заполнения новой базы занятий задайте School__SeedDemo=true.

| Роль | Логин | Учебный пароль |
|---|---|---|
| Администратор | admin | Auto16-admin! |
| Менеджер | manager | Auto16-manager! |
| Инструктор Никита | nikita | Auto16-nikita! |
| Инструктор Сергей | sergey | Auto16-sergey! |

База и журналы по умолчанию находятся в %LOCALAPPDATA%/DrivingSchool. Настройка School__Database задаёт отдельный файл SQLite. Демоданные и пользователи предназначены для локальной учебной среды.

## Проверки

В PowerShell 7.4+ выполните ./build.ps1. Для сборки .NET 8 добавьте -Legacy. Ключ -Ui добавляет FlaUI и требует доступного интерактивного рабочего стола. ./Smoke-Api.ps1 запускает Release API с временной SQLite и проверяет живое HTTPS-соединение. Проект tools/DrivingSchool.Benchmarks измеряет расчёт статистики на 10 000 занятиях.

[Требования](docs/requirements.md) · [Архитектура](docs/architecture.md) · [API](docs/api-spec.md) · [Работы 1–29](docs/works-1-29.md) · [Руководство](docs/user-guide.md)
