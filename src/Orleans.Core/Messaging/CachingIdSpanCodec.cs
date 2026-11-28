using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers;
using Orleans.Caching;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// 用于 <see cref="IdSpan"/> 的序列化器，可缓存值以避免重复编码和不必要的内存分配。
    /// </summary>
    internal sealed class CachingIdSpanCodec
    {
        private static readonly ConcurrentLruCache<IdSpan, IdSpan> SharedCache = new(capacity: 128_000);

        // 清除超过 2 分钟未访问的条目。
        private const long PurgeAfterMilliseconds = 2 * 60 * 1000;

        // 每分钟扫描一次过期条目。
        private const long GarbageCollectionIntervalMilliseconds = 60 * 1000;

        private readonly Dictionary<int, (byte[] Value, long LastSeen)> _cache = new();
        private long _lastGarbageCollectionTimestamp;

        public CachingIdSpanCodec()
        {
            _lastGarbageCollectionTimestamp = Environment.TickCount64;
        }

        public IdSpan ReadRaw<TInput>(ref Reader<TInput> reader)
        {
            var currentTimestamp = Environment.TickCount64;

            var length = reader.ReadVarUInt32();
            if (length == 0)
                return default;

            var hashCode = reader.ReadInt32();

            IdSpan result = default;
            byte[]? payloadArray = default;
            if (!reader.TryReadBytes((int)length, out var payloadSpan))
            {
                payloadSpan = payloadArray = reader.ReadBytes(length);
            }

            ref var cacheEntry = ref CollectionsMarshal.GetValueRefOrAddDefault(_cache, hashCode, out var exists);
            if (exists && payloadSpan.SequenceEqual(cacheEntry.Value))
            {
                result = IdSpan.UnsafeCreate(cacheEntry.Value, hashCode);
            }
            else
            {
                result = IdSpan.UnsafeCreate(payloadArray ?? payloadSpan.ToArray(), hashCode);

                // 将此值添加到私有缓存并返回之前，通过共享缓存对其进行驻留（intern），以尽可能减少重复实例。
                result = SharedCache.GetOrAdd(result, static (key, _) => key, (object?)null);

                // 更新缓存。如果发生哈希冲突，则以最后写入的条目为准。
                cacheEntry.Value = IdSpan.UnsafeGetArray(result)!;
            }
            cacheEntry.LastSeen = currentTimestamp;

            // 定期执行维护，防止内存泄漏导致占用无限增长。
            if (currentTimestamp - _lastGarbageCollectionTimestamp > GarbageCollectionIntervalMilliseconds)
            {
                PurgeStaleEntries();
                _lastGarbageCollectionTimestamp = currentTimestamp;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PurgeStaleEntries()
        {
            var currentTimestamp = Environment.TickCount64;
            foreach (var entry in _cache)
            {
                if (currentTimestamp - entry.Value.LastSeen > PurgeAfterMilliseconds)
                {
                    _cache.Remove(entry.Key);
                }
            }
        }

        public void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, IdSpan value) where TBufferWriter : IBufferWriter<byte>
        {
            IdSpanCodec.WriteRaw(ref writer, value);
            SharedCache.GetOrAdd(value, static (key, _) => key, (object?)null);
        }
    }
}
