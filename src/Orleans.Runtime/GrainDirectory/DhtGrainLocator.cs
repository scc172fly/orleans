using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Orleans.GrainDirectory;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 执行《公约》的执行情况 <see cref="IGrainLocator"/> 用于内存分布目录中的 Orleans
    /// </summary>
    internal class DhtGrainLocator : IGrainLocator
    {
        private readonly ILocalGrainDirectory _localGrainDirectory;
        private readonly IGrainContext _grainContext;
        private readonly Lock _initLock = new();
        private BatchedDeregistrationWorker _forceWorker = null!;
        private BatchedDeregistrationWorker _neaWorker = null!;

        public DhtGrainLocator(
            ILocalGrainDirectory localGrainDirectory,
            IGrainContext grainContext)
        {
            _localGrainDirectory = localGrainDirectory;
            _grainContext = grainContext;
        }

        public async ValueTask<GrainAddress?> Lookup(GrainId grainId) => (await _localGrainDirectory.LookupAsync(grainId)).Address;

        public async Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress) => (await _localGrainDirectory.RegisterAsync(address, currentRegistration: previousAddress)).Address;

        public Task Unregister(GrainAddress address, UnregistrationCause cause)
        {
            EnsureInitialized();

            // 如果事情变得复杂, 我们可以使用列表或内部管理 单一工人。
            var worker = cause switch
            {
                UnregistrationCause.Force => _forceWorker,
                UnregistrationCause.NonexistentActivation => _neaWorker,
                _ => throw new ArgumentOutOfRangeException($"Deregistration cause {cause} is unknown and is not supported. This is a bug."),
            };

            return worker.Unregister(address);

            void EnsureInitialized()
            {
                // 不幸的是,我们现在需要拉着时间进行初始化工作,因为 SystemTarget 不有效
                // 直至它注册到目录(见 Catalog.RegisterSystemTarget),此例之后即可
                // 已经构建。
                if (_forceWorker is not null && _neaWorker is not null)
                {
                    return;
                }

                lock (_initLock)
                {
                    if (_forceWorker is not null && _neaWorker is not null)
                    {
                        return;
                    }

                    _forceWorker = new BatchedDeregistrationWorker(_localGrainDirectory, _grainContext, UnregistrationCause.Force);
                    _neaWorker = new BatchedDeregistrationWorker(_localGrainDirectory, _grainContext, UnregistrationCause.NonexistentActivation);
                }
            }
        }

        public static DhtGrainLocator FromLocalGrainDirectory(LocalGrainDirectory localGrainDirectory)
            => new(localGrainDirectory, localGrainDirectory.RemoteGrainDirectory);

        public void UpdateCache(GrainId grainId, SiloAddress siloAddress) => _localGrainDirectory.AddOrUpdateCacheEntry(grainId, siloAddress);
        public void InvalidateCache(GrainId grainId) => _localGrainDirectory.InvalidateCacheEntry(grainId);
        public void InvalidateCache(GrainAddress address) => _localGrainDirectory.InvalidateCacheEntry(address);
        public bool TryLookupInCache(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address) => _localGrainDirectory.TryLocalLookup(grainId, out address);

        private class BatchedDeregistrationWorker
        {
            private const int OperationBatchSizeLimit = 2_000;
            private readonly ILocalGrainDirectory _localGrainDirectory;
            private readonly IGrainContext _grainContext;
            private readonly UnregistrationCause _cause;
            private readonly Channel<(TaskCompletionSource<bool> tcs, GrainAddress address)> _queue;
#pragma warning disable IDE0052 // Remove unread private members
            private readonly Task _runTask;
#pragma warning restore IDE0052 // Remove unread private members

            public BatchedDeregistrationWorker(
                ILocalGrainDirectory localGrainDirectory,
                IGrainContext grainContext,
                UnregistrationCause cause)
            {
                _localGrainDirectory = localGrainDirectory;
                _grainContext = grainContext;
                _cause = cause;
                _queue = Channel.CreateUnbounded<(TaskCompletionSource<bool> tcs, GrainAddress address)>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
                _runTask = _grainContext.RunOrQueueTask(() => ProcessDeregistrationQueue());
            }

            public Task Unregister(GrainAddress address)
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Writer.TryWrite((tcs, address));
                return tcs.Task;
            }

            private async Task ProcessDeregistrationQueue()
            {
                var operations = new List<TaskCompletionSource<bool>>();
                var addresses = new List<GrainAddress>();
                var reader = _queue.Reader;

                while (await reader.WaitToReadAsync())
                {
                    // a 处理批次工作。
                    try
                    {
                        operations.Clear();
                        addresses.Clear();

                        while (operations.Count < OperationBatchSizeLimit && reader.TryRead(out var op))
                        {
                            operations.Add(op.tcs);
                            addresses.Add(op.address);
                        }

                        if (operations.Count > 0)
                        {
                            await _localGrainDirectory.UnregisterManyAsync(addresses, _cause);
                            foreach (var op in operations)
                            {
                                op.TrySetResult(true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        foreach (var op in operations)
                        {
                            op.TrySetException(ex);
                        }
                    }
                }
            }
        }
    }
}
