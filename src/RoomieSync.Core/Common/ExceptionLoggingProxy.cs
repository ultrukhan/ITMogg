using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace RoomieSync.Core.Common;

public static class TechnicalErrorLog
{
    public static void Write(Exception exception, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        // TODO: Підключити реальний логер тут; передавати йому exception і context, не змінюючи місця виклику.
        Trace.TraceError("{0}{1}{2}", context is null ? string.Empty : context + ": ", exception, Environment.NewLine);
    }
}

public class ExceptionLoggingProxy<T> : DispatchProxy where T : class
{
    private T _decorated = null!;

    public static T Create(T decorated)
    {
        ArgumentNullException.ThrowIfNull(decorated);
        var proxy = DispatchProxy.Create<T, ExceptionLoggingProxy<T>>();
        ((ExceptionLoggingProxy<T>)(object)proxy)._decorated = decorated;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new InvalidOperationException("The intercepted method is unavailable.");

        try
        {
            var result = targetMethod.Invoke(_decorated, args);
            if (result is Task task)
            {
                _ = task.ContinueWith(
                    completed => LogTaskFailure(completed, targetMethod.Name),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            return result;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            LogAndRethrow(ex.InnerException, targetMethod.Name);
            throw;
        }
        catch (Exception ex)
        {
            LogAndRethrow(ex, targetMethod.Name);
            throw;
        }
    }

    private static void LogTaskFailure(Task task, string methodName)
    {
        var exception = task.Exception;
        if (exception is not null && exception.Flatten().InnerExceptions
                .Any(inner => inner is not DomainException and not OperationCanceledException))
            TechnicalErrorLog.Write(exception, $"{typeof(T).Name}.{methodName}");
    }

    private static void LogAndRethrow(Exception exception, string methodName)
    {
        if (exception is not DomainException and not OperationCanceledException)
            TechnicalErrorLog.Write(exception, $"{typeof(T).Name}.{methodName}");
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
