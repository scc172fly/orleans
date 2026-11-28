using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Internal;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;

namespace Orleans.Runtime
{
    internal class GrainCancellationTokenRuntime : IGrainCancellationTokenRuntime
    {
        private const int MaxNumCancelErrorTries = 30;
        private readonly TimeSpan _cancelCallMaxWaitTime = TimeSpan.FromSeconds(300);
        private readonly IBackoffProvider _cancelCallBackoffProvider = new FixedBackoff(TimeSpan.FromSeconds(1));
        private readonly Func<Exception, int, bool> _cancelCallRetryExceptionFilter =
            (exception, i) => exception is GrainExtensionNotInstalledException;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "Cancellation callbacks must execute synchronously on the current Orleans scheduler context before remote propagation.")]
        public Task Cancel(Guid id, CancellationTokenSource tokenSource, ConcurrentDictionary<GrainId, GrainReference> grainReferences)
        {
            if (tokenSource.IsCancellationRequested)
            {
                // 此令牌已被取消。
                return Task.CompletedTask;
            }

            // 将 _cancellationTokenSource.Cancel 引发的异常传播回调用方，
            // 但同时也取消 _targetGrainReferences。
            Task? localTask = null;
            try
            {
                // 立即取消令牌，以防止递归。
                tokenSource.Cancel();
            }
            catch (Exception exception)
            {
                localTask = Task.FromException(exception);
            }

            List<Task>? tasks = null;
            foreach (var reference in grainReferences)
            {
                if (tasks is null)
                {
                    tasks = new();
                    if (localTask != null) tasks.Add(localTask);
                }
                tasks.Add(CancelTokenWithRetries(id, grainReferences, reference.Key, reference.Value.AsReference<ICancellationSourcesExtension>()));
            }

            return tasks is null ? localTask ?? Task.CompletedTask : Task.WhenAll(tasks);
        }

        private async Task CancelTokenWithRetries(
            Guid id,
            ConcurrentDictionary<GrainId, GrainReference> grainReferences,
            GrainId key,
            ICancellationSourcesExtension tokenExtension)
        {
            await AsyncExecutorWithRetries.ExecuteWithRetries(
                i => tokenExtension.CancelRemoteToken(id, CancellationToken.None),
                MaxNumCancelErrorTries,
                _cancelCallRetryExceptionFilter,
                _cancelCallMaxWaitTime,
                _cancelCallBackoffProvider);
            grainReferences.TryRemove(key, out _);
        }
    }

#pragma warning disable CS0618 // 保留对旧版取消令牌的序列化支持。
    [RegisterSerializer]
    internal class GrainCancellationTokenCodec : GeneralizedReferenceTypeSurrogateCodec<GrainCancellationToken, GrainCancellationTokenSurrogate>
    {
        private readonly IGrainCancellationTokenRuntime _runtime;

        public GrainCancellationTokenCodec(IGrainCancellationTokenRuntime runtime, IValueSerializer<GrainCancellationTokenSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
            _runtime = runtime;
        }

        public override GrainCancellationToken ConvertFromSurrogate(ref GrainCancellationTokenSurrogate surrogate)
        {
            return new GrainCancellationToken(surrogate.TokenId, surrogate.IsCancellationRequested, _runtime);
        }

        public override void ConvertToSurrogate(GrainCancellationToken value, ref GrainCancellationTokenSurrogate surrogate)
        {
            surrogate.IsCancellationRequested = value.IsCancellationRequested;
            surrogate.TokenId = value.Id;
        }
    }

#pragma warning restore CS0618

    [GenerateSerializer]
    internal struct GrainCancellationTokenSurrogate
    {
        [Id(0)]
        public bool IsCancellationRequested;

        [Id(1)]
        public Guid TokenId;
    }
}
