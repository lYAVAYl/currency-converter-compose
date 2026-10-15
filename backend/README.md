# Бекенд

ASP.NET Core 10 API работает с PostgreSQL через EF Core и обращается к
отдельному `web-service` за курсами Frankfurter. При загрузке рынка новые
валюты добавляются в таблицу `currencies`; история хранится в `conversions`.

Swagger UI: [http://localhost:8081/swagger](http://localhost:8081/swagger).
OpenAPI JSON: [http://localhost:8081/swagger/v1/swagger.json](http://localhost:8081/swagger/v1/swagger.json).

| Метод | Адрес | Назначение |
| --- | --- | --- |
| GET | `/api/market` | Доступные валюты и курсы для интерфейса |
| GET | `/api/currencies` | Список доступных валют |
| POST | `/api/conversions` | Сохранить одну пару |
| POST | `/api/conversions/batch` | Сохранить несколько пар одним запросом |
| GET | `/api/conversions?limit=20` | Последние записи из БД |
| GET | `/api/conversions/{id}` | Одна запись |
| DELETE | `/api/conversions/{id}` | Удалить запись |
| GET | `/health/ready` | Проверка соединения с БД |

Пример сохранения результатов для нескольких валют:

```json
{"amount":1000,"fromCurrency":"RUB","toCurrencies":["GEL","USD","EUR"]}
```

Сумма должна быть больше нуля и не превышать 1 000 000 000 000. В одном
запросе можно выбрать от одной до десяти разных целевых валют. Бекенд сохраняет
все результаты одним вызовом `SaveChanges`, если каждый расчёт корректен.

При запуске применяются миграции. Устаревшие записи с криптовалютой, если они
уже есть в базе от прежней версии, сохраняются, но не попадают в новую историю
и список доступных валют.
