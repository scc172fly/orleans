using Microsoft.Extensions.Logging;

namespace Orleans.Docs.Snippets.Interceptors;

// <logging_incoming_filter>
public class LoggingCallFilter : IIncomingGrainCallFilter
{
    private readonly ILogger<LoggingCallFilter> _logger;

    public LoggingCallFilter(ILogger<LoggingCallFilter> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        try
        {
            await context.Invoke();

            _logger.LogInformation(
                "{GrainType}.{MethodName} returned value {Result}",
                context.Grain.GetType(),
                context.MethodName,
                context.Result);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{GrainType}.{MethodName} threw an exception",
                context.Grain.GetType(),
                context.MethodName);

            // 如果未重新抛出此异常，则认为它已被
            // 当前筛选器处理。
            throw;
        }
    }
}
// </logging_incoming_filter>

// <logging_outgoing_filter>
public class OutgoingLoggingCallFilter : IOutgoingGrainCallFilter
{
    private readonly ILogger<OutgoingLoggingCallFilter> _logger;

    public OutgoingLoggingCallFilter(ILogger<OutgoingLoggingCallFilter> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        try
        {
            await context.Invoke();

            _logger.LogInformation(
                "{GrainType}.{MethodName} returned value {Result}",
                context.Grain.GetType(),
                context.MethodName,
                context.Result);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{GrainType}.{MethodName} threw an exception",
                context.Grain.GetType(),
                context.MethodName);

            // 如果未重新抛出此异常，则认为它已被
            // 当前筛选器处理。
            throw;
        }
    }
}
// </logging_outgoing_filter>
