using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Orleans.Durable.Http;

/// <summary>配置对外公开持久化任务的 HTTP 操作。</summary>
public sealed class DurableHttpOptions
{
    /// <summary>
    /// 获取或设置启动持久化任务的请求是否必须携带 <c>Idempotency-Key</c> 请求头。
    /// </summary>
    /// <remarks>
    /// 为 <see langword="true"/>（默认值）时，无键请求以 <c>400 Bad Request</c> 被拒绝，因此每个
    /// 操作都具有客户端重试时使用的键。为 <see langword="false"/> 时，无键请求会以生成的标识符启动
    /// 新操作，后续重试无法访问该操作。
    /// </remarks>
    public bool RequireIdempotencyKey { get; set; } = true;

    /// <summary>
    /// 获取或设置一个函数，用于返回请求调用方的授权作用域，例如该请求经过身份验证所代表的用户或
    /// 帐户；匿名调用方返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 操作的键由组作用域、请求的组路由参数值、调用方作用域
    /// 及操作标识符组成，各自作为独立的段。所有指定操作的请求，包括启动请求和
    /// 观察、取消、发送信号或附加回调的请求，均应用该函数，因此
    /// 调用方只能访问自身作用域中的操作：操作标识符所指向的操作
    /// 由使用它的调用方决定，而不是任何知悉该值的调用方都拥有的能力。匿名调用方共用一个作用域。
    /// </para>
    /// <para>
    /// 默认实现 <see cref="DefaultGetCallerScope"/> 返回已经通过身份验证的用户的
    /// <see cref="ClaimTypes.NameIdentifier"/> 声明或 <c>sub</c> 声明。应用可根据其授权范围划分操作作用域，
    /// 例如多个用户共享的帐户：
    /// <c>options.GetCallerScope = context => context.User.FindFirst("account")?.Value;</c>。对于已通过身份验证的调用方，
    /// 作用域为 <see langword="null"/> 的请求会被拒绝，因此已认证调用方永远不会共用匿名作用域。
    /// </para>
    /// </remarks>
    public Func<HttpContext, string?> GetCallerScope { get; set; } = DefaultGetCallerScope;

    /// <summary>
    /// 获取或设置一个函数，用于判定服务器是否接受请求注册的回调 URL，例如通过
    /// <c>Operation-Callback</c> 请求头或 Nexus <c>callback</c> 注册的 URL；默认值为 <see langword="null"/>，
    /// 表示不接受任何回调 URL。
    /// </summary>
    /// <remarks>
    /// 执行操作的 Silo 将向已接受的 URL 发送请求，因此应用应将其限制为
    /// 它信任的目标，例如根据方案、主机及已认证调用方进行判断。投递也仅连接到
    /// 由 <see cref="DurableCallbackOptions.AllowAddress"/> 接受的地址，该地址是主机名在
    /// 每次交付建立连接时解析得到。
    /// </remarks>
    public Func<HttpContext, Uri, bool>? AllowCallback { get; set; }

    /// <summary>
    /// 获取或设置运行中的操作在响应的 <c>Retry-After</c> 响应头中声明的间隔，作为
    /// 客户端再次轮询操作之前的最短时间。
    /// </summary>
    /// <remarks>响应头使用整秒，因此间隔向上取整。默认值为一秒。</remarks>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置请求的 <c>Prefer: wait</c> 偏好允许等待的最长时长，超过该时长后响应将报告
    /// 操作仍在运行。
    /// </summary>
    /// <remarks>默认值为一分钟。</remarks>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置一个函数，用于向客户端描述失败或被取消的任务的异常，描述写入操作的
    /// <see cref="DurableOperation.Error"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 异常消息可能透露内部状态（如主机和 Grain 标识），因此默认实现仅描述
    /// 结果：<c>Canceled</c> 表示 <see cref="OperationCanceledException"/>，<c>Failed</c> 表示其他情况。若
    /// 应用为客户端定义了异常类型，可以返回其详细信息，例如：
    /// </para>
    /// <code>
    /// options.CreateError = exception => exception is OrderRejectedException rejected
    ///     ? new("OrderRejected", rejected.Message)
    ///     : DurableHttpOptions.DefaultCreateError(exception);
    /// </code>
    /// <para>
    /// 宿主未能启动工作的操作会以 <see cref="DurableOperationError.NotStartedCode"/>
    /// 或 <see cref="DurableOperationError.OutcomeUnknownCode"/> 错误码失败，这些错误不由此函数描述。
    /// </para>
    /// </remarks>
    public Func<Exception, DurableOperationError> CreateError { get; set; } = DefaultCreateError;

    /// <summary>仅根据任务结果描述异常，不包含异常的类型或消息。</summary>
    /// <param name="exception">导致任务失败或被取消的异常。</param>
    /// <returns>错误码为 <c>Canceled</c> 或 <c>Failed</c> 的错误。</returns>
    public static DurableOperationError DefaultCreateError(Exception exception) => exception is OperationCanceledException
        ? new("Canceled", "The operation was canceled.")
        : new("Failed", "The operation failed.");

    /// <summary>
    /// 返回已通过身份验证的用户的 <see cref="ClaimTypes.NameIdentifier"/> 声明或 <c>sub</c> 声明；若请求中两者都没有，则返回
    /// <see langword="null"/>。
    /// </summary>
    /// <param name="context">请求。</param>
    /// <returns>调用方作用域，或 <see langword="null"/>。</returns>
    public static string? DefaultGetCallerScope(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User;
        return user.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value
            : null;
    }
}
