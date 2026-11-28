using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>配置处理程序返回持久化工作的端点，由端点将该工作作为操作启动。</summary>
internal static class DurableTaskEndpointConventions
{
    private static readonly string[] JsonContentTypes = ["application/json"];
    private static readonly string[] ProblemContentTypes = ["application/problem+json"];

    /// <summary>
    /// 用操作的响应元数据替换持久化启动处理程序推断出的响应元数据，因为端点返回的是
    /// 操作而不是持久化工作本身。
    /// </summary>
    /// <exception cref="InvalidOperationException">启动端点仅响应安全方法，例如 <c>GET</c>。</exception>
    public static void DescribeStart(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.OfType<MethodInfo>().LastOrDefault() is not { } method || GetResultType(method) is not { } resultType)
        {
            return;
        }

        // 启动会创建操作，而安全方法不会执行此类操作，其请求可由客户端及缓存自由重复发送。
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
    /// 返回处理程序所返回的持久化工作的结果类型；无结果的工作返回 <see cref="void"/>，而
    /// 不返回持久化工作的处理程序返回 <see langword="null"/>。
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

    /// <summary>当 <paramref name="name"/>（URL 路径段名称）包含 URL 非保留字符以外的字符时抛出异常。</summary>
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
