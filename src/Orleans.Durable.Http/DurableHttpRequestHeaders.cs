using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Orleans.Durable.Http;

/// <summary>读取 <c>Idempotency-Key</c> 和 <c>Prefer</c> 请求头。</summary>
internal static class DurableHttpRequestHeaders
{
    /// <summary>Stripe 接受的键的最大字符数。</summary>
    public const int MaxIdempotencyKeyLength = 255;

    /// <summary>
    /// 读取请求的幂等键，其形式为裸值或 RFC 9651 字符串；请求头存在但无效时返回
    /// <see langword="false"/>，并提供原因说明。
    /// </summary>
    /// <param name="headers">请求头。</param>
    /// <param name="key">键；若请求未携带键，则为 <see langword="null"/>。</param>
    /// <param name="error">无效请求头的说明。</param>
    public static bool TryGetIdempotencyKey(IHeaderDictionary headers, out string? key, [NotNullWhen(false)] out string? error)
    {
        key = null;
        error = null;
        var values = headers[DurableHttpHeaders.IdempotencyKey];
        if (values.Count == 0)
        {
            return true;
        }

        if (values.Count > 1)
        {
            error = "The request carries more than one Idempotency-Key header.";
            return false;
        }

        var value = values[0].AsSpan().Trim(' ');
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            if (!TryUnescape(value[1..^1], out key))
            {
                error = "The Idempotency-Key header is not a valid structured-field string.";
                return false;
            }
        }
        else
        {
            key = value.ToString();
        }

        if (!IsValidOperationId(key))
        {
            key = null;
            error = $"An Idempotency-Key has 1 to {MaxIdempotencyKeyLength} printable ASCII characters other than '/', and isn't '.' or '..'.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 返回 <paramref name="operationId"/> 是否为有效的操作标识符：长度为 1 至
    /// <see cref="MaxIdempotencyKeyLength"/> 个可打印 ASCII 字符，不含 <c>/</c>，且不为 <c>.</c> 或 <c>..</c>。
    /// </summary>
    /// <remarks>
    /// 操作标识符是状态监视器 URL 的一个路径段。ASP.NET Core 保持路径段中已转义的 <c>/</c>
    /// 的转义形式，而客户端会从路径中移除点路径段 <c>.</c> 和 <c>..</c>，因此这些标识符
    /// 无法正确寻址对应操作。
    /// </remarks>
    public static bool IsValidOperationId(string operationId)
    {
        if (operationId.Length is 0 or > MaxIdempotencyKeyLength || operationId is "." or "..")
        {
            return false;
        }

        foreach (var character in operationId)
        {
            if (character is < ' ' or > '~' or '/')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 返回请求的 <c>Prefer: wait=N</c> 偏好要求服务器等待结果的时长，最多为
    /// <paramref name="maxWait"/>；若请求不希望等待，则返回 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    /// <remarks>
    /// 按照 RFC 7240 的要求，忽略服务器无法识别或解析的偏好。
    /// <c>respond-async</c> 请求直接响应而不等待结果，这是默认行为。
    /// </remarks>
    public static TimeSpan GetWait(IHeaderDictionary headers, TimeSpan maxWait)
    {
        foreach (var header in headers[DurableHttpHeaders.Prefer])
        {
            foreach (var preference in (header ?? string.Empty).Split(','))
            {
                var token = preference.AsSpan();
                if (token.IndexOf(';') is var parameters and >= 0)
                {
                    token = token[..parameters];
                }

                token = token.Trim();
                if (token.IndexOf('=') is var separator and > 0
                    && token[..separator].Trim().Equals("wait", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token[(separator + 1)..].Trim().Trim('"'), System.Globalization.NumberStyles.None, null, out var seconds))
                {
                    var wait = TimeSpan.FromSeconds(seconds);
                    return wait < maxWait ? wait : maxWait;
                }
            }
        }

        return TimeSpan.Zero;
    }

    private static bool TryUnescape(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? result)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\')
            {
                if (++index == value.Length || value[index] is not ('"' or '\\'))
                {
                    result = null;
                    return false;
                }

                character = value[index];
            }
            else if (character == '"')
            {
                result = null;
                return false;
            }

            builder.Append(character);
        }

        result = builder.ToString();
        return true;
    }
}
