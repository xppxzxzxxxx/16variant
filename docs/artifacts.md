# Артефакты и протоколы

| Артефакт | Назначение |
|---|---|
| DrivingSchool.slnx, .slnLaunch, *.csproj | Решение, зависимости и профиль F5 |
| DrivingSchool.Data/Migrations | Версия схемы SQLite |
| docs/*.drawio, docs/*.png | Исходники и изображения диаграмм |
| requests.http, Smoke-Api.ps1 | Запросы IDE и автоматический прогон живого HTTPS |
| docs/examples | Примеры импорта и форматов экспорта |
| DrivingSchool.Tests, DrivingSchool.UiTests | xUnit, WebApplicationFactory и FlaUI |
| .github/ | CI, шаблоны дефекта и PR |
| TestResults, publish | Локальные результаты и публикации, исключены из Git |
| ZIP, отчёт, дневник | Материалы сдачи |

Клиент и сервер обмениваются JSON по HTTPS. Даты API принимаются по ISO 8601. Импорт принимает text/csv, выгрузка возвращает файл с подходящим типом содержимого. Ошибки с диагностикой следуют формату ProblemDetails.
