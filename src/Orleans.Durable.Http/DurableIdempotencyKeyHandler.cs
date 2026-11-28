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
/// 为持久化任务发送的每个请求添加标识正在运行的持久化任务的 <c>Idempotency-Key</c> 请求头，
/// 因此每次尝试和重放都发送相同的键。
/// </summary>
/// <remarks>
/// <para>
/// 该键是对服务、运行任务的 Grain、任务标识符及任务的
/// <see cref="DurableTaskContext.Incarnation"/> 计算的哈希值：由 43 个 URL 安全的 Base64 字符组成，在任务同一次运行中的每次尝试和
/// 重放中相同，而与其他任务不同，包括使用同一任务标识符的后续运行（服务器可能仍保留
/// 较早的键）。因此，发送单个请求的步骤（例如
/// <c>DurableTask.Run(ct =&gt; client.PostAsJsonAsync(...))</c>）可使该请求在
/// 支持此请求头的服务器上具有幂等性，包括失败或停用后重试该步骤的情况。若要发送
/// 多个指向不同操作的请求，应为每个请求安排独立的步骤，例如使用
/// <see cref="DurableTaskExtensions.WithId{TResult}(DurableTask{TResult}, string)"/>，或自行设置各自的键。
/// </para>
/// <para>
/// 使用安全方法（如 <c>GET</c>）、已携带此请求头，或者由持久化任务之外的代码
/// 发送的请求保持不变。应在
/// 重试请求的处理程序之前注册此处理程序，使重试也携带幂等键。
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
        // 按定义安全方法具有幂等性，因此这些请求不携带幂等键。
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options || request.Method == HttpMethod.Trace
            || request.Headers.Contains(DurableHttpHeaders.IdempotencyKey)
            || !DurableTaskContext.TryGetCurrent(out var task))
        {
            return;
        }

        // 任务标识符在其所属 Grain 中唯一，因此该键也涵盖运行任务的 Grain。
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
