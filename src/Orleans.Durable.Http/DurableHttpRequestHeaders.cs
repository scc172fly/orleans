using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Orleans.Durable.Http;

/// <summary>读取 <c>Idempotency-Key</c> 和 <c>Prefer</c> 请求信头 。</summary>
internal static class DurableHttpRequestHeaders
{
    /// <summary>字符中最长的键,用字符表示,条形接受。</summary>
    public const int MaxIdempotencyKeyLength = 255;

    /// <summary>
    /// 读取请求请求的“应急”密钥,即一个光值或 RFC 9651 字符串,并返回
    /// <see langword="false"/> ,其中说明信头存在但无效。
    /// </summary>
    /// <param name="headers">请求信头。</param>
    /// <param name="key">键,或 <see langword="null"/> 当请求无任何要求时。</param>
    /// <param name="error">无效信头的描述。</param>
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
    /// 返回 <paramref name="operationId"/> 是否是一个有效的操作识别身份: 1 to
    /// <see cref="MaxIdempotencyKeyLength"/> 打印 ASCII 字符以外的可 <c>/</c>,但除 <c>.</c> 和 <c>..</c>.
    /// </summary>
    /// <remarks>
    /// 操作识别标识符是状态监视器的 URL 路径路路段。 ASP.NET Core 将一个逃出 <c>/</c> 放在
    /// 路径中逃脱了, 而客户端则从路径中删除点段 <c>.</c> 和 <c>..</c> 因此这些识别符号
    /// 无法处理他们的行动
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
    /// 返回请求 <c>Prefer: wait=N</c> 优先偏爱要求服务器等待结果的时间,最多
    /// <paramref name="maxWait"/>或 <see cref="TimeSpan.Zero"/> ,如果请求中选择不等待。
    /// </summary>
    /// <remarks>
    /// 服务器不承认或无法分析的优惠被忽略, RFC 7240 要求这样做。
    /// <c>respond-async</c> 请求在等待结果的情况下作出答复,这是默认的。
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
