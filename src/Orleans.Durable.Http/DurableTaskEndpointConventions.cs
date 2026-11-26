using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>配置处理者返回持久工作的终点,端点以操作开始。</summary>
internal static class DurableTaskEndpointConventions
{
    private static readonly string[] JsonContentTypes = ["application/json"];
    private static readonly string[] ProblemContentTypes = ["application/problem+json"];

    /// <summary>
    /// 替换耐久启动源处理器处理器计算响应元数据,因为端点是
    /// 行动的反应是持久工作,而不是持久工作。
    /// </summary>
    /// <exception cref="InvalidOperationException">启动回答只安全,例如 <c>GET</c>。</exception>
    public static void DescribeStart(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.OfType<MethodInfo>().LastOrDefault() is not { } method || GetResultType(method) is not { } resultType)
        {
            return;
        }

        // 开始创造一种操作, 安全的方法, 客户和缓存可以自由重复, 而不是。
        if (endpoint.Metadata.OfType<IHttpMethodMetadata>().LastOrDefault() is { HttpMethods: { Count: > 0 } methods }
            && methods.All(static method => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)))
        {
            throw new InvalidOperationException(
                $"'{endpoint.DisplayName}' returns durable work, which starts an operation, from a safe method. Map the start with POST, PUT, PATCH, or DELETE.");
        }

        for (var index = endpoint.Metadata.Count - 1; index >= 0; index--)
        {
            if (endpoint.Metadata[index] is IProducesResponseTypeMetadata { Type: { } type } && typeof(DurableTask).IsAssignableFrom(type))
            {
                endpoint.Metadata.RemoveAt(index);
            }
        }

        endpoint.Metadata.Add(resultType == typeof(void)
            ? new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void), [])
            : new ProducesResponseTypeMetadata(StatusCodes.Status200OK, resultType, JsonContentTypes));
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted, typeof(DurableOperation), JsonContentTypes));
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status424FailedDependency, typeof(DurableOperation), JsonContentTypes));
        foreach (var status in (ReadOnlySpan<int>)[StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity, StatusCodes.Status503ServiceUnavailable])
        {
            endpoint.Metadata.Add(new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), ProblemContentTypes));
        }
    }

    /// <summary>
    /// 返回一个处理器的结果类型,该处理器将返回耐久工作, <see cref="void"/> 工作无结果,或
    /// <see langword="null"/> 处理者不会回来的持久工作。
    /// </summary>
    public static Type? GetResultType(MethodInfo handler)
    {
        var returnType = handler.ReturnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Task<>) || definition == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
        }

        for (var type = returnType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DurableTask<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return typeof(DurableTask).IsAssignableFrom(returnType) ? typeof(void) : null;
    }

    /// <summary>丢弃时, <paramref name="name"/>即是一个 URL 路径段的名称,其字符除 URL - 无保留的字符外,还有其他字符。</summary>
    public static void ThrowIfInvalidName(string name, string? parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, parameter);
        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.' or '_' or '~'))
            {
                throw new ArgumentException($"A name in a route has URL-unreserved characters, but '{name}' has '{character}'.", parameter);
            }
        }
    }
}
