using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Storage.Internal;

namespace Orleans.Storage
{
    /// <summary>
    /// 内质存储供应商使用的存储 Grain 实施类别
    /// <c>Orleans.Storage.MemoryStorage</c>
    /// </summary>
    [KeepAlive]
    internal partial class MemoryStorageGrain : Grain, IMemoryStorageGrain
    {
        private readonly Dictionary<string, object?> _store = new();
        private readonly ILogger _logger;

        public MemoryStorageGrain(ILogger<MemoryStorageGrain> logger)
        {
            _logger = logger;
        }

        public Task<IGrainState<T>?> ReadStateAsync<T>(string grainStoreKey)
            => ReadStateAsync<T>(grainStoreKey, CancellationToken.None);

        public Task<IGrainState<T>?> ReadStateAsync<T>(
            string grainStoreKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugReadState(grainStoreKey);
            _store.TryGetValue(grainStoreKey, out var entry);
            return Task.FromResult((IGrainState<T>?)entry);
        }

        public Task<string> WriteStateAsync<T>(string grainStoreKey, IGrainState<T> grainState)
            => WriteStateAsync(grainStoreKey, grainState, CancellationToken.None);

        public Task<string> WriteStateAsync<T>(
            string grainStoreKey,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugWriteState(grainStoreKey, grainState.ETag);
            var currentETag = GetETagFromStorage<T>(grainStoreKey);
            ValidateEtag(currentETag, grainState.ETag, grainStoreKey, "Update");
            grainState.ETag = NewEtag();
            _store[grainStoreKey] = grainState;
            LogDebugDoneWriteState(grainStoreKey, grainState.ETag);
            return Task.FromResult(grainState.ETag);
        }

        public Task DeleteStateAsync<T>(string grainStoreKey, string? etag)
            => DeleteStateAsync<T>(grainStoreKey, etag, CancellationToken.None);

        public Task DeleteStateAsync<T>(
            string grainStoreKey,
            string? etag,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugDeleteState(grainStoreKey, etag);

            var currentETag = GetETagFromStorage<T>(grainStoreKey);
            ValidateEtag(currentETag, etag, grainStoreKey, "Delete");
            // 不要从字典中删除它, 只要设置为空值以记住此项目
            // 曾经在店里,现在被删除
            _store[grainStoreKey] = null;
            return Task.CompletedTask;
        }

        private static string NewEtag()
        {
            return Guid.NewGuid().ToString("N");
        }

        private string? GetETagFromStorage<T>(string grainStoreKey)
        {
            string? currentETag = null;
            if (_store.TryGetValue(grainStoreKey, out var entry))
            {
                // 如果条目为无效,则从存储中移除
                currentETag = entry != null ? ((IGrainState<T>)entry).ETag : string.Empty;
            }
            return currentETag;
        }

        private void ValidateEtag(string? currentETag, string? receivedEtag, string grainStoreKey, string operation)
        {
            // 如果我们没有当前数据, 我们将接受用户数据 。
            // 这是当存储 grain 因 silo 崩溃而丢失时的缓解。
            if (currentETag == null)
                return;

            // 如果这是我们第一次写作, 并且我们有一个空的牌子, 我们就很好
            if (string.IsNullOrEmpty(currentETag) && receivedEtag == null)
                return;

            // 如果现在的状态和新状态 都配有匹配的标签 或者我们忽略ETag,我们就很好
            if (receivedEtag == currentETag || receivedEtag == "*")
                return;

            // 换句话说,我们有一个不匹配的
            LogWarningEtagMismatch(operation, grainStoreKey, currentETag, receivedEtag);
            throw new MemoryStorageEtagMismatchException(currentETag, receivedEtag);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "ReadStateAsync for grain: {GrainStoreKey}"
        )]
        private partial void LogDebugReadState(string grainStoreKey);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "WriteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugWriteState(string grainStoreKey, string? etag);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Done WriteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugDoneWriteState(string grainStoreKey, string? etag);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "DeleteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugDeleteState(string grainStoreKey, string? etag);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = 0,
            Message = "Etag mismatch during {Operation} for grain {GrainStoreKey}: Expected = {Expected} Received = {Received}"
        )]
        private partial void LogWarningEtagMismatch(string operation, string grainStoreKey, string expected, string? received);
    }
}
