using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

internal static partial class LocalRequests
{
    /// <summary>
    /// 完成 <paramref name="message"/> 随 <paramref name="response"/> 提出的本地请求,然后予以处置。
    /// </summary>
    /// <remarks>
    /// 请求完成或处置失败的记录是记录而不是扔掉,因此填写了一批申请的打电话者完成所有申请。
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
    /// 完成 <paramref name="message"/> 随 <paramref name="exception"/> 提出的本地请求,然后予以处置。
    /// </summary>
    /// <inheritdoc cref="Complete(Message, Response, ILogger)" path="/remarks"/>
    public static void Reject(Message message, Exception exception, ILogger logger) => Complete(message, Response.FromException(exception), logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error completing local request {Request}."
    )]
    private static partial void LogErrorCompletingLocalRequest(ILogger logger, Exception exception, ILocalRequest request);
}
