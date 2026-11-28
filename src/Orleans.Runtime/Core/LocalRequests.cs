using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

internal static partial class LocalRequests
{
    /// <summary>
    /// 对 <paramref name="message"/> 中携带的本地请求，使用 <paramref name="response"/> 将其完成，然后释放该请求。
    /// </summary>
    /// <remarks>
    /// 请求完成或释放期间的失败会记录到日志，而不是抛出异常，以便批量完成请求的调用方能够完成每个请求。
    /// </remarks>
    public static void Complete(Message message, Response response, ILogger logger)
    {
        Debug.Assert(message.IsLocalOnly);
        var request = (ILocalRequest)message.BodyObject!;
        try
        {
            try
            {
                request.Complete(response);
            }
            finally
            {
                request.Dispose();
            }
        }
        catch (Exception exception)
        {
            LogErrorCompletingLocalRequest(logger, exception, request);
        }
    }

    /// <summary>
    /// 对 <paramref name="message"/> 中携带的本地请求，使用 <paramref name="exception"/> 将其完成，然后释放该请求。
    /// </summary>
    /// <inheritdoc cref="Complete(Message, Response, ILogger)" path="/remarks"/>
    public static void Reject(Message message, Exception exception, ILogger logger) => Complete(message, Response.FromException(exception), logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error completing local request {Request}."
    )]
    private static partial void LogErrorCompletingLocalRequest(ILogger logger, Exception exception, ILocalRequest request);
}
