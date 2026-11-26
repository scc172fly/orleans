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
    /// <see cref="IdSpan"/> 的序列器,该序列器缓存值并避免重编码和不必要的分配。
    /// </summary>
    internal sealed class CachingIdSpanCodec
    {
        private static readonly ConcurrentLruCache<IdSpan, IdSpan> SharedCache = new(capacity: 128_000);

        // 清洗条目在两分钟内尚未进入。 
        private const long PurgeAfterMilliseconds = 2 * 60 * 1000;

        // 每分钟过期的条目扫描
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

                // 在给私人藏匿处添加这个价值并归还之前 通过共享的藏匿处进行内部实习 希望能减少重复
                result = SharedCache.GetOrAdd(result, static (key, _) => key, (object?)null);

                // 更新缓存。 如果发生散列碰撞, 最后一项会赢 。
                cacheEntry.Value = IdSpan.UnsafeGetArray(result)!;
            }
            cacheEntry.LastSeen = currentTimestamp;

            // 进行定期维护以防止未约束的内存泄漏。
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
