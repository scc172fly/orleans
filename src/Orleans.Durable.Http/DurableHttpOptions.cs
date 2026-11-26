using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Orleans.Durable.Http;

/// <summary>配置暴露耐久任务的 HTTP 操作。</summary>
public sealed class DurableHttpOptions
{
    /// <summary>
    /// 设置或确定启动耐久任务的请求是否必须包含 <c>Idempotency-Key</c> 页眉。
    /// </summary>
    /// <remarks>
    /// 何时 <see langword="true"/>,默认,没有密钥的请求被拒绝 <c>400 Bad Request</c>, 所以每
    /// 选项卡。当 <see langword="false"/>时,在没有按键的情况下请求启动
    /// 请求无法重试的特性标识符下的新操作。
    /// </remarks>
    public bool RequireIdempotencyKey { get; set; } = true;

    /// <summary>
    /// 获取或设置返回请求人调用权限的函数,例如用户或
    /// 其认证为匿名调用者的账户,或 <see langword="null"/> 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 操作的关键是它的集团范围,请求中的集团路线参数值, 呼叫者的范围,
    /// 和操作标识符,每个部分都是它自己的。
    /// 和观察、取消、信号或附加回复的请求,应用此函数,所以
    /// 呼叫者仅达到其范围范围内的业务:行动识别符号名称;呼叫者的操作
    /// 匿名来电者有相同的范围。
    /// </para>
    /// <para>
    /// 默认值 <see cref="DefaultGetCallerScope"/>返回已认证用户的
    /// <see cref="ClaimTypes.NameIdentifier"/> 或 <c>sub</c> 一项适用的范围以其
    /// 授权赠款,例如几个用户共用的账户:
    /// <c>options.GetCallerScope = context => context.User.FindFirst("account")?.Value;</c>。
    /// 其范围为 <see langword="null"/> 被否决,因此经认证的来电者永远不具有匿名范围。
    /// </para>
    /// </remarks>
    public Func<HttpContext, string?> GetCallerScope { get; set; } = DefaultGetCallerScope;

    /// <summary>
    /// 获取或设置函数,以决定服务器是否接受一个回回调的 URL,而请求登记簿则由该URL
    /// 与 <c>Operation-Callback</c> 头或 Nexus <c>callback</c>,或 <see langword="null"/>默认,
    /// 不接受任何保留。
    /// </summary>
    /// <remarks>
    /// 运行操作的 silos 向接受的 URL发送请求,因此应用程序将这些请求限制在接收器
    /// 并且相信,例如通过阴谋、主机和经认证的来电者等。
    /// 所接受的地址 <see cref="DurableCallbackOptions.AllowAddress"/> ,东道主的名称在
    /// 每一个送货都连接在一起
    /// </remarks>
    public Func<HttpContext, Uri, bool>? AllowCallback { get; set; }

    /// <summary>
    /// 查找或设置在 <c>Retry-After</c> 页眉中广告的运行操作回复间隔,以
    /// 客户再次调查这次行动之前的最小时间
    /// </summary>
    /// <remarks>信头带有整秒, 因此间隔会四舍五入。 默认时间为一秒钟 。</remarks>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置请求 <c>Prefer: wait</c> 优先选择所接受的最长时间的等待
    /// 正在运行中的行动。
    /// </summary>
    /// <remarks>默认为一分钟。</remarks>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置函数, 描述失败或取消任务在操作中的客户例外
    /// <see cref="DurableOperation.Error"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 例外信件可以描述内部状态, 如主机和 grain 身份等, 因此默认只能描述
    /// 结果: <c>Canceled</c> 用于 a <see cref="OperationCanceledException"/>,和 <c>Failed</c> 以其他方式。
    /// 其客户的例外定义在应用程序中返回了他们的细节,例如:
    /// </para>
    /// <code>
    /// options.CreateError = exception => exception is OrderRejectedException rejected
    ///     ? new("OrderRejected", rejected.Message)
    ///     : DurableHttpOptions.DefaultCreateError(exception);
    /// </code>
    /// <para>
    /// 其主机无法启动工作与 <see cref="DurableOperationError.NotStartedCode"/>失败的操作
    /// 或 <see cref="DurableOperationError.OutcomeUnknownCode"/> 代码,此函数不描述该代码。
    /// </para>
    /// </remarks>
    public Func<Exception, DurableOperationError> CreateError { get; set; } = DefaultCreateError;

    /// <summary>仅按任务的结果描述例外,没有其类型或信息。</summary>
    /// <param name="exception">任务失败或取消的例外。</param>
    /// <returns>代码为错误 <c>Canceled</c> 或 <c>Failed</c>.</returns>
    public static DurableOperationError DefaultCreateError(Exception exception) => exception is OperationCanceledException
        ? new("Canceled", "The operation was canceled.")
        : new("Failed", "The operation failed.");

    /// <summary>
    /// 返回经认证的用户 <see cref="ClaimTypes.NameIdentifier"/> 索赔要求或其 <c>sub</c> 索赔要求,或
    /// 请求没有同时提出<see langword="null"/> 。
    /// </summary>
    /// <param name="context">请求。</param>
    /// <returns>或 <see langword="null"/>.</returns>
    public static string? DefaultGetCallerScope(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User;
        return user.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value
            : null;
    }
}
