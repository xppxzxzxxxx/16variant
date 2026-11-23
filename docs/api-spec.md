# Спецификация API и матрица прав

Базовый адрес отдельного API — https://localhost:7130. В режиме Development доступен /swagger. Авторизация передаётся заголовком Authorization: Bearer <token>.

| Запрос | Роли | Результат |
|---|---|---|
| GET /health | Все | 200 при рабочей БД, 503 при недоступной схеме |
| POST /api/auth/login | Все | LoginInput: login, password; 200 с токеном и профилем, 401 или 429 |
| GET /api/auth/me | Все роли | 200 с профилем |
| POST /api/auth/logout | Все роли | 204, токен больше не принимается |
| GET /api/users | Администратор | 200, список пользователей |
| GET /api/users/instructors | Администратор, менеджер | 200, список инструкторов |
| GET /api/students | Администратор, менеджер | 200, справочник |
| POST /api/students | Администратор, менеджер | 201, поля name, phone, email |
| PUT /api/students/{id} | Администратор, менеджер | 204, поля как при создании |
| POST /api/students/import | Администратор, менеджер | 200 с количеством; 400 с errors, 413 при превышении размера |
| GET /api/lessons?q=&status= | Все роли | 200; инструктор получает назначенные ему строки |
| GET /api/lessons/{id} | Все роли | 200; отсутствующий или чужой ID даёт 404 |
| POST /api/lessons | Администратор, менеджер | 201, исходный статус Planned |
| PUT /api/lessons/{id} | Администратор, менеджер | 204; принимает version из последней прочитанной карточки |
| POST /api/lessons/{id}/transition | Все роли | 204; инструктор меняет только свои занятия |
| GET /api/lessons/statistics | Все роли | 200; статистика по видимой области |
| GET /api/lessons/export/{format} | Администратор, менеджер | 200, csv/json/xml; принимает те же фильтры |

В создание и редактирование передаются studentId, title, address, notes, instructorId, amount, scheduledAt и, при редактировании, version. В переход передаются status, version, reason и finishedAt. Статусы перечисляются строками Planned, Confirmed, Completed, Rescheduled, Missed. Ответ 400 означает нарушение контракта или правила, 401 — отсутствующий или завершённый сеанс, 403 — операцию за пределами роли, 409 — устаревшую версию. В 500 возвращается общая ошибка.
