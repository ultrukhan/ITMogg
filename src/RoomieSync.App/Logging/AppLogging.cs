using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomieSync.Core.Abstractions.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace RoomieSync.App.Logging;

/// <summary>
/// Налаштування журналювання застосунку (Serilog через Microsoft.Extensions.Logging).
///
/// Куди пишемо (за замовчуванням <c>%LOCALAPPDATA%\RoomieSync\logs</c>):
/// <list type="bullet">
///   <item><c>roomiesync-YYYYMMDD.log</c> — усі події від мінімального рівня, новий файл щодня (зберігаємо 30 днів);</item>
///   <item><c>errors-YYYYMMDD.log</c> — лише Warning, Error, Fatal, щоб швидко знайти проблеми;</item>
///   <item><c>sessions\session-YYYYMMDD-HHmmss-XXXXXXXX.log</c> — окремий файл на кожен запуск програми (сесію).</item>
/// </list>
/// Рівні: Verbose &lt; Debug &lt; Information &lt; Warning &lt; Error &lt; Fatal.
/// Мінімальний рівень змінюється ключем <c>Logging:MinimumLevel</c> в appsettings.json
/// або змінною середовища <c>ROOMIESYNC_Logging__MinimumLevel</c>.
/// </summary>
public static class AppLogging
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SessionId}] [{User}] {SourceContext}: {Message:l}{NewLine}{Exception}";

    private static ILoggerFactory _factory = NullLoggerFactory.Instance;

    /// <summary>Ідентифікатор поточного запуску програми (сесії).</summary>
    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];

    public static string LogDirectory { get; private set; } = DefaultLogDirectory();

    public static string? SessionLogFile { get; private set; }

    /// <summary>Логер для класів, які створюються не через DI (базові ViewModel, App тощо).</summary>
    public static ILogger CreateLogger(Type type) => _factory.CreateLogger(type);

    public static ILogger<T> CreateLogger<T>() => _factory.CreateLogger<T>();

    public static void Initialize(IConfiguration configuration)
    {
        var section = configuration.GetSection("Logging");
        LogDirectory = section["Directory"] is { Length: > 0 } dir
            ? Environment.ExpandEnvironmentVariables(dir)
            : DefaultLogDirectory();

        var minimum = ParseLevel(section["MinimumLevel"],
#if DEBUG
            LogEventLevel.Debug);
#else
            LogEventLevel.Information);
#endif
        var retainDays = int.TryParse(section["RetainedDays"], out var d) && d > 0 ? d : 30;

        var sessionsDir = Path.Combine(LogDirectory, "sessions");
        Directory.CreateDirectory(sessionsDir);
        SessionLogFile = Path.Combine(sessionsDir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{SessionId}.log");
        CleanupOldSessionFiles(sessionsDir, retainDays);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimum)
            // Службові логи .NET (HttpClient, DI) лише від Warning, щоб не засмічувати файли.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("SessionId", SessionId)
            .Enrich.With(CurrentUserEnricher.Instance)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .WriteTo.File(
                Path.Combine(LogDirectory, "roomiesync-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainDays,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: OutputTemplate)
            .WriteTo.File(
                Path.Combine(LogDirectory, "errors-.log"),
                restrictedToMinimumLevel: LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainDays,
                shared: true,
                outputTemplate: OutputTemplate)
            .WriteTo.File(SessionLogFile, outputTemplate: OutputTemplate)
            .CreateLogger();

        _factory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddSerilog(dispose: false));

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
        Log.ForContext("SourceContext", "RoomieSync.App")
            .Information("RoomieSync {Version} started on {OS}, .NET {Runtime}; minimum log level {Level}, logs in {Dir}",
                version, Environment.OSVersion, Environment.Version, minimum, LogDirectory);
    }

    /// <summary>Підключає Serilog як провайдер для <see cref="ILogger{T}"/> у DI.</summary>
    public static void AttachTo(ILoggingBuilder builder)
        => builder.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddSerilog(dispose: false);

    /// <summary>Дозволяє писати в кожен рядок, хто з користувачів зараз увійшов.</summary>
    public static void TrackSession(ISessionContext session) => CurrentUserEnricher.Instance.Session = session;

    public static void Shutdown(int exitCode)
    {
        Log.ForContext("SourceContext", "RoomieSync.App").Information("RoomieSync exited with code {ExitCode}", exitCode);
        Log.CloseAndFlush();
    }

    private static LogEventLevel ParseLevel(string? value, LogEventLevel fallback)
        => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) ? level : fallback;

    private static string DefaultLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoomieSync", "logs");

    private static void CleanupOldSessionFiles(string dir, int retainDays)
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-retainDays);
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("session-*.log"))
                if (f.LastWriteTime < threshold) f.Delete();
        }
        catch (IOException) { /* файл може бути відкритий іншою копією програми */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Додає до кожної події властивість User: Id учасника або "anonymous" до входу.</summary>
    private sealed class CurrentUserEnricher : ILogEventEnricher
    {
        public static readonly CurrentUserEnricher Instance = new();
        public ISessionContext? Session { get; set; }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        {
            var member = Session?.CurrentMember;
            var user = member is null ? "anonymous" : member.Id.ToString("N")[..8];
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("User", user));
        }
    }
}
