using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Http;

/// <summary>
/// 添加一个 <c>Idempotency-Key</c> 标题,为每次要求一项耐久任务的请求指定运行中的持久任务
/// 发送, 所以任务每次尝试和重播都会发出相同的密钥 。
/// </summary>
/// <remarks>
/// <para>
/// 关键是服务的散列、执行任务的 grain 、任务识别符号及其
/// <see cref="DurableTaskContext.Incarnation"/>:43个URL安全基数64字符,每件尝试和
/// 重放一个运行的任务, 并区分为其它任务, 包括后一个运行的相同任务标识符,
/// 服务器可能仍然可以持有其先前的密钥。 发送一个请求的一步,
/// 诸如 <c>DurableTask.Run(ct =&gt; client.PostAsJsonAsync(...))</c>因此,在
/// 服务器,该服务器将尊重信头的服务器,包括失败或停止功能后步骤的重试。
/// 不同行动的若干次请求,每个步骤都有自己的步骤,例如:
/// <see cref="DurableTaskExtensions.WithId{TResult}(DurableTask{TResult}, string)"/>,或设置他们的键本身。
/// </para>
/// <para>
/// 带有安全方法的请求,如 <c>GET</c>、已经随信头发出的请求和要求编码
/// 一项持久任务外发送的,无变化。
/// 处理器在重新输入请求的处理器前, 其重试时会携带密钥 。
/// </para>
/// </remarks>
public sealed class DurableIdempotencyKeyHandler(IGrainContextAccessor grainContextAccessor, IOptions<ClusterOptions> clusterOptions)
    : DelegatingHandler
{
    private readonly string _serviceId = clusterOptions.Value.ServiceId;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AddKey(request);
        return base.SendAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AddKey(request);
        return base.Send(request, cancellationToken);
    }

    private void AddKey(HttpRequestMessage request)
    {
        // 安全方法顾名思义是无能的,因此其请求没有钥匙。
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options || request.Method == HttpMethod.Trace
            || request.Headers.Contains(DurableHttpHeaders.IdempotencyKey)
            || !DurableTaskContext.TryGetCurrent(out var task))
        {
            return;
        }

        // 任务识别符在 grain中是独一无二的,因此键包含执行任务的 grain 。
        var grainId = grainContextAccessor.GrainContext.GrainId;
        if (grainId.IsClient())
        {
            throw new InvalidOperationException(
                $"Durable task '{task.Id}' sent a request after leaving its grain's scheduler, for example after ConfigureAwait(false), so the request's idempotency key can't identify the grain which runs the task. Send the request from the task's step before leaving the scheduler.");
        }

        request.Headers.TryAddWithoutValidation(DurableHttpHeaders.IdempotencyKey, CreateKey(_serviceId, grainId, task.Id, task.Incarnation));
    }

    internal static string CreateKey(string serviceId, GrainId grainId, TaskId taskId, Guid incarnation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, serviceId);
        Append(hash, grainId.ToString());
        Append(hash, taskId.ToString());
        Append(hash, incarnation.ToString("N"));
        return WebEncoders.Base64UrlEncode(hash.GetHashAndReset());

        static void Append(IncrementalHash hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }
}
