namespace Orleans.Durable.Http;

/// <summary>命名耐久 HTTP 操作协议的 HTTP 页头。</summary>
public static class DurableHttpHeaders
{
    /// <summary>
    /// 请求页头可识别启动操作的请求, 请求的重写可一次启动 。
    /// </summary>
    /// <remarks>
    /// 数值遵循Srakee和IETTF <c>Idempotency-Key</c> 草稿:1至255可打印的 ASCII 字符,发送
    /// 或作为引用的结构字段字符串。关键是操作的识别符号,即一个 URL 路径路路段,因此它已经
    /// 无 无 <c>/</c> 而不是 <c>.</c> 或 <c>..</c>.
    /// </remarks>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>定位行动状态监测器的应答信头,作为 Azure 长期行动就是如此。</summary>
    public const string OperationLocation = "Operation-Location";

    /// <summary>
    /// 注册回调 URL 的请求信头,一旦操作即接收运行状态监视器
    /// 即为终端。每发货都携带请求书信头,以 <see cref="OperationCallbackHeaderPrefix"/>开头,
    /// 没有前缀。
    /// </summary>
    public const string OperationCallback = "Operation-Callback";

    /// <summary>请求信头的前缀,每个回击交付时携带的请求信头的前缀没有前缀。</summary>
    public const string OperationCallbackHeaderPrefix = "Operation-Callback-";
    internal const string Prefer = "Prefer";
    internal const string PreferenceApplied = "Preference-Applied";
}
