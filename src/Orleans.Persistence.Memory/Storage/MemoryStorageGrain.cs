using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Storage.Internal;

namespace Orleans.Storage
{
    /// <summary>
    /// 内存存储提供程序使用的存储 Grain 的实现类：
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
            // 不要从字典中移除此条目，只将值设置为 null，以记录该条目
            // 曾存在于存储中，但现已删除。
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
                // 如果条目为 null，则表示它已从存储中删除。
                currentETag = entry != null ? ((IGrainState<T>)entry).ETag : string.Empty;
            }
            return currentETag;
        }

        private void ValidateEtag(string? currentETag, string? receivedEtag, string grainStoreKey, string operation)
        {
            // 如果没有当前 ETag，则接受用户的数据。
            // 这用于缓解因 Silo 崩溃而丢失内存存储 Grain 的情况。
            if (currentETag == null)
                return;

            // 如果这是第一次写入，并且 ETag 为空，则可以继续。
            if (string.IsNullOrEmpty(currentETag) && receivedEtag == null)
                return;

            // 如果当前状态与新状态的 ETag 匹配，或允许忽略 ETag，则可以继续。
            if (receivedEtag == currentETag || receivedEtag == "*")
                return;

            // 否则，ETag 不匹配。
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
