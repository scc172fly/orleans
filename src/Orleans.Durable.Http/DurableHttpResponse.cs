using System.Net;
using System.Text;
using System.Text.Json;

namespace Orleans.Durable.Http;

/// <summary>记录持久化任务收到的 HTTP 响应：状态码、响应头和内容。</summary>
/// <param name="StatusCode">响应的状态码。</param>
/// <param name="RequestUri">该响应对应的请求 URL。</param>
/// <param name="Headers">响应头和内容头，其名称以不区分大小写的方式比较。</param>
/// <param name="Content">响应内容。</param>
/// <remarks>
/// 任务记录响应，因此重放时无需再次发送请求即可读取该响应。适用于大小不超过
/// <see cref="DurableHttpClientOptions.MaxResponseContentLength"/> 的 API 响应；较大的内容应在任务步骤中进行流式处理。
/// 记录省略 <c>Set-Cookie</c> 响应头，其中可能携带服务器为客户端设置的凭据。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableHttpResponse")]
public sealed record DurableHttpResponse(
    HttpStatusCode StatusCode,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    ReadOnlyMemory<byte> Content)
{
    /// <summary>获取 <see cref="StatusCode"/> 是否处于 200 至 299 的范围内。</summary>
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    /// <summary>抛出 <see cref="HttpRequestException"/>（当 <see cref="IsSuccessStatusCode"/> 为 <see langword="false"/> 时）。</summary>
    /// <returns>此响应。</returns>
    /// <exception cref="HttpRequestException">响应的状态码不是成功状态码。</exception>
    public DurableHttpResponse EnsureSuccessStatusCode() =>
        IsSuccessStatusCode ? this : throw CreateException();

    /// <summary>返回名为 <paramref name="name"/> 的响应头的第一个值；不存在该响应头时返回 <see langword="null"/>。</summary>
    /// <param name="name">响应头名称。</param>
    /// <returns>响应头的第一个值，或 <see langword="null"/>。</returns>
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var values) && values.Length > 0 ? values[0] : null;

    /// <summary>将内容解码为 UTF-8 文本并返回。</summary>
    /// <returns>字符串形式的内容。</returns>
    public string ReadAsString() => Encoding.UTF8.GetString(Content.Span);

    /// <summary>将内容作为 JSON 反序列化；内容为空时返回 <see langword="default"/>。</summary>
    /// <typeparam name="T">要反序列化的类型。</typeparam>
    /// <param name="options">序列化器选项；为 <see langword="null"/> 时使用 <see cref="JsonSerializerDefaults.Web"/> 的默认选项。</param>
    /// <returns>反序列化得到的值。</returns>
    public T? ReadFromJson<T>(JsonSerializerOptions? options = null) =>
        Content.IsEmpty ? default : JsonSerializer.Deserialize(Content.Span, (options ?? DurableHttpClientOptions.WebSerializerOptions).GetTypeInfo<T>());

    internal HttpRequestException CreateException() =>
        new($"'{RequestUri}' returned {(int)StatusCode} ({StatusCode}).", inner: null, StatusCode);

    internal static async Task<DurableHttpResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.NonValidated)
        {
            if (!name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) && !name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase))
            {
                headers[name] = [.. values];
            }
        }

        foreach (var (name, values) in response.Content.Headers.NonValidated)
        {
            headers[name] = [.. values];
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new(response.StatusCode, response.RequestMessage?.RequestUri, headers, content);
    }
}
