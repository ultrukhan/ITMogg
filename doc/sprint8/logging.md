# Журналювання (Sprint 8, #82)

Застосунок пише події різних рівнів важливості у файли за допомогою **Serilog**,
підключеного через стандартний інтерфейс `Microsoft.Extensions.Logging.ILogger<T>`.
Serilog обрано замість log4net, бо він нативно працює з DI та `ILogger<T>` у .NET 8,
не потребує XML-конфігурації і пише структуровані повідомлення (з іменованими параметрами).

## Де лежать журнали

За замовчуванням `%LOCALAPPDATA%\RoomieSync\logs\` (наприклад `C:\Users\<user>\AppData\Local\RoomieSync\logs`).

| Файл | Що містить |
|---|---|
| `roomiesync-YYYYMMDD.log` | усі події від мінімального рівня; новий файл щодня, при розмірі > 10 МБ створюється `_001`, `_002`… |
| `errors-YYYYMMDD.log` | лише `Warning`, `Error`, `Fatal`: швидко знайти проблеми |
| `sessions\session-YYYYMMDD-HHmmss-<SessionId>.log` | окремий файл на кожен запуск програми (сесію користувача) |

Файли старші за 30 днів видаляються автоматично.

Формат рядка:

```
2026-11-25 23:11:04.399 +02:00 [WRN] [0a8d1b41] [anonymous] RoomieSync.Services.Auth.AuthService: Login failed for m***@roomiesync.app: wrong password, 2 attempt(s) left
           час                рівень  сесія    користувач   клас-джерело                          повідомлення
```

`[User]` дорівнює `anonymous` до входу і першим 8 символам Id учасника після входу.
Приклад повного журналу однієї сесії: [`sample-session.log.txt`](sample-session.log.txt).

## Рівні

| Рівень | Коли використовуємо | Приклади |
|---|---|---|
| `Debug` (`DBG`) | деталі для розробника | спроба входу, перехід між сторінками, час роботи алгоритму спрощення боргів |
| `Information` (`INF`) | нормальні бізнес-події | старт/завершення програми, вхід/вихід, додано витрату, зафіксовано розрахунок, ротація обов'язку, експорт CSV/PDF, резервна копія |
| `Warning` (`WRN`) | щось пішло не так, але програма працює далі | невірний пароль, блокування акаунта на 60 с, невалідні дані форми, Gemini не відповів за 5 с (перехід на ручне введення), відхилений файл чека, алгоритм боргів > 500 мс |
| `Error` (`ERR`) | неочікувана помилка операції (зі стеком) | помилка HTTP від Gemini, необроблений виняток у ViewModel чи UI-потоці |
| `Fatal` (`FTL`) | програма не може працювати далі | не вдалося підключитися до БД на старті, виняток у фоновому потоці |

Службові логи .NET (`Microsoft.*`, `System.*`, наприклад HttpClient) пишуться лише від `Warning`.

## Налаштування

Мінімальний рівень за замовчуванням: `Debug` у Debug-збірці, `Information` у Release.
Змінити можна без перекомпіляції, в `appsettings.json` / `appsettings.Local.json` поруч з `.exe`:

```json
{
  "Logging": {
    "MinimumLevel": "Warning",
    "Directory": "D:\\RoomieSyncLogs",
    "RetainedDays": 14
  }
}
```

або змінною середовища: `set ROOMIESYNC_Logging__MinimumLevel=Debug`.

## Безпека журналів

- Паролі, хеші, солі та ключ Gemini API **ніколи** не потрапляють у журнал.
- Email маскується: `maya@roomiesync.app` → `m***@roomiesync.app` (`LogSanitizer.MaskEmail`).
- Для файлів пишемо тільки ім'я без повного шляху (у шляху буває ім'я користувача Windows).
- Учасників ідентифікуємо за Id, а не за іменем.
- Це перевіряють тести `tests/RoomieSync.Tests/LoggingTests.cs` (наприклад, після 3 невдалих входів у журналі немає ні пароля, ні email).

## Обробка помилок

- `App.xaml.cs`: глобальні обробники `DispatcherUnhandledException` (Error, програма працює далі),
  `AppDomain.UnhandledException` (Fatal + скидання буфера на диск) і `TaskScheduler.UnobservedTaskException`.
- `ViewModelBase.RunAsync`: `DomainException` → Warning, будь-який інший виняток → Error зі стеком.
  Раніше неочікувані помилки писались лише в `Debug.WriteLine`, а кілька `catch { }` мовчки їх ковтали
  (`ShellViewModel`, `CreateHouseholdViewModel`, `DialogViewModel`): тепер вони теж логуються.

## Як використовувати в новому коді

```csharp
public sealed class MyService(IMyRepository repo, ILogger<MyService> logger) : IMyService
{
    public async Task DoAsync(Guid id)
    {
        logger.LogInformation("Doing something with {ItemId}", id); // параметри в {}, не через $"..."
        ...
    }
}
```

У ViewModel, яка наслідує `ViewModelBase`, логер уже є: `Logger.LogWarning(...)`.
