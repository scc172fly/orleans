using System.Net;
using System.Text;
using System.Text.Json;

namespace Orleans.Durable.Http;

/// <summary>记录一项长期任务收到的HTTP答复:其状态代码、页眉和内容。</summary>
/// <param name="StatusCode">回复状态代码。</param>
/// <param name="RequestUri">答复回复请求的 URL。</param>
/// <param name="Headers">回应的页眉和内容页头,其名称与案件不敏感比较。</param>
/// <param name="Content">回复的内容。</param>
/// <remarks>
/// 任务记录回复, 所以重放阅读它而不发送请求。 它符合 API 的响应, 直至
/// <see cref="DurableHttpClientOptions.MaxResponseContentLength"/>;在任务的一个步骤内流出大量内容。
/// 记录中略去为客户端携带服务器证书的 <c>Set-Cookie</c> 信头。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableHttpResponse")]
public sealed record DurableHttpResponse(
    HttpStatusCode StatusCode,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    ReadOnlyMemory<byte> Content)
{
    /// <summary>查找 <see cref="StatusCode"/> 是否在200-299的范围。</summary>
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    /// <summary>丢弃 <see cref="HttpRequestException"/> 时, <see cref="IsSuccessStatusCode"/> 是 <see langword="false"/>。</summary>
    /// <returns>本答复。</returns>
    /// <exception cref="HttpRequestException">响应没有成功状态代码。</exception>
    public DurableHttpResponse EnsureSuccessStatusCode() =>
        IsSuccessStatusCode ? this : throw CreateException();

    /// <summary>返回页头的第一个值 <paramref name="name"/>,或 <see langword="null"/> 当回应没有它。</summary>
    /// <param name="name">信头的名称。</param>
    /// <returns>标题的第一个值,或 <see langword="null"/>.</returns>
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var values) && values.Length > 0 ? values[0] : null;

    /// <summary>返回以 UTF-8 文本编码的内容。</summary>
    /// <returns>内容作为字符串。</returns>
    public string ReadAsString() => Encoding.UTF8.GetString(Content.Span);

    /// <summary>将内容降为 JSON,或返回空内容的 <see langword="default"/> 。</summary>
    /// <typeparam name="T">解除序列的型号。</typeparam>
    /// <param name="options">序列器选项,或 <see langword="null"/> 排排 <see cref="JsonSerializerDefaults.Web"/> 默认值。</param>
    /// <returns>消毒价值</returns>
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
