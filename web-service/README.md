# Сервис курсов

Отдельный ASP.NET Core 10 сервис получает список действующих валют и последние
курсы из [Frankfurter v2](https://frankfurter.dev/). Ключ API не требуется.
Сервис исключает металлы и расчётные единицы, оставляя денежные валюты.

Swagger UI: [http://localhost:8082/swagger](http://localhost:8082/swagger).
OpenAPI JSON: [http://localhost:8082/swagger/v1/swagger.json](http://localhost:8082/swagger/v1/swagger.json).

| Метод | Адрес | Назначение |
| --- | --- | --- |
| GET | `/api/market` | Список валют и курсов относительно USD |
| GET | `/api/rates?from=RUB&to=GEL` | Курс одной валютной пары |
| GET | `/health` | Проверка процесса |

`/api/market` возвращает `baseCurrency`, `source`, `asOfUtc` и массив
`currencies`. У каждой валюты есть `code`, `name`, `rate` (единиц этой валюты
за 1 USD) и собственная дата курса `asOfUtc`. Курс пары вычисляется как
`rate_целевой / rate_исходной`. Ответы Frankfurter кэшируются на 10 минут;
последний успешный ответ временно сохраняется при кратком сбое провайдера.

Базовый адрес можно переопределить через `Providers__FrankfurterBaseUrl` для
локального тестирования. При недоступности провайдера без сохранённого ответа
сервис возвращает HTTP 503.
