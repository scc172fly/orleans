namespace Orleans.Durable.Http;

/// <summary>定义持久化 HTTP 操作协议使用的 HTTP 请求头与响应头名称。</summary>
public static class DurableHttpHeaders
{
    /// <summary>
    /// 用于标识启动操作请求的请求头，使重复请求只启动一次操作。
    /// </summary>
    /// <remarks>
    /// 值遵循 Stripe 和 IETF <c>Idempotency-Key</c> 草案：1 至 255 个可打印 ASCII 字符，可按
    /// 裸值或带引号的结构化字段字符串发送。该键是操作标识符，也是 URL 路径段，因此
    /// 不包含 <c>/</c>，且不为 <c>.</c> 或 <c>..</c>。
    /// </remarks>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>用于定位操作状态监视器的响应头，沿用 Azure 长时间运行操作的约定。</summary>
    public const string OperationLocation = "Operation-Location";

    /// <summary>
    /// 用于注册回调 URL 的请求头；操作进入终态时该 URL 接收操作的状态监视器。
    /// 每次交付均携带请求中名称以 <see cref="OperationCallbackHeaderPrefix"/> 开头的请求头，
    /// 并去掉此前缀。
    /// </summary>
    public const string OperationCallback = "Operation-Callback";

    /// <summary>每次回调交付携带的请求头的名称前缀，实际交付时去掉该前缀。</summary>
    public const string OperationCallbackHeaderPrefix = "Operation-Callback-";
    internal const string Prefer = "Prefer";
    internal const string PreferenceApplied = "Preference-Applied";
}
