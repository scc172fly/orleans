using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Caching;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// <see cref="SiloAddress"/> 的序列器,该序列器缓存值并避免重编码和不必要的分配。
    /// </summary>
    internal sealed class CachingSiloAddressCodec
    {
        // 长度前缀、 hash、 IPv6 地址及其长度前缀、 端口和生成 。
        internal const int MaxEncodedSize = 1 + 4 + 1 + 16 + 5 + 4;

        internal static ConcurrentLruCache<SiloAddress, (SiloAddress Value, byte[] Encoded)> SharedCache { get; } = new(capacity: 1024);

        // 清洗条目在两分钟内尚未进入。
        private const long PurgeAfterMilliseconds = 2 * 60 * 1000;

        // 每分钟过期的条目扫描
        private const long GarbageCollectionIntervalMilliseconds = 60 * 1000;

        private readonly Dictionary<int, (byte[] Encoded, SiloAddress Value, long LastSeen)> _cache = new();
        private long _lastGarbageCollectionTimestamp;

        public CachingSiloAddressCodec()
        {
            _lastGarbageCollectionTimestamp = Environment.TickCount64;
        }

        public SiloAddress? ReadRaw<TInput>(ref Reader<TInput> reader)
        {
            SiloAddress result;
            byte[]? payloadArray = default;
            var length = (int)reader.ReadVarUInt32();
            if (length == 0)
            {
                return null;
            }

            if (!reader.TryReadBytes(length, out var payloadSpan))
            {
                payloadSpan = payloadArray = reader.ReadBytes((uint)length);
            }

            var innerReader = Reader.Create(payloadSpan, session: null!);
            var hashCode = innerReader.ReadInt32();

            ref var cacheEntry = ref CollectionsMarshal.GetValueRefOrAddDefault(_cache, hashCode, out var exists);
            var currentTimestamp = Environment.TickCount64;
            if (exists && payloadSpan.SequenceEqual(cacheEntry.Encoded))
            {
                result = cacheEntry.Value;
                cacheEntry.LastSeen = currentTimestamp;
            }
            else
            {
                result = ReadSiloAddressInner(ref innerReader);
                result.InternalSetConsistentHashCode(hashCode);

                // 在给私人藏匿处添加这个价值并归还之前 通过共享的藏匿处进行内部实习 希望能减少重复
                payloadArray ??= payloadSpan.ToArray();
                (result, payloadArray) = SharedCache.GetOrAdd(result, static (key, encoded) => (key, encoded), payloadArray);

                // 如果发生散列碰撞,那么最后一次看到进入就会永远赢。
                cacheEntry.Encoded = payloadArray;
                cacheEntry.Value = result;
                cacheEntry.LastSeen = currentTimestamp;
            }

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

        private static SiloAddress ReadSiloAddressInner<TInput>(ref Reader<TInput> reader)
        {
            var ip = IPAddressCodec.ReadRaw(ref reader);
            var port = (int)reader.ReadVarUInt32();
            var generation = reader.ReadInt32();

            return SiloAddress.New(ip, port, generation);
        }

        public void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, SiloAddress? value) where TBufferWriter : IBufferWriter<byte>
        {
            var currentTimestamp = Environment.TickCount64;
            if (value is null)
            {
                writer.WriteByte(1); // writer.WriteVarUInt32(0);
                return;
            }

            var hashCode = value.GetConsistentHashCode();
            ref var cacheEntry = ref CollectionsMarshal.GetValueRefOrAddDefault(_cache, hashCode, out var exists);
            if (exists && value.Equals(cacheEntry.Value))
            {
                writer.WriteVarUInt32((uint)cacheEntry.Encoded.Length);
                writer.Write(cacheEntry.Encoded);

                cacheEntry.LastSeen = currentTimestamp;

                // 进行定期维护以防止未约束的内存泄漏。
                if (currentTimestamp - _lastGarbageCollectionTimestamp > GarbageCollectionIntervalMilliseconds)
                {
                    PurgeStaleEntries();
                    _lastGarbageCollectionTimestamp = currentTimestamp;
                }

                return;
            }

            var innerWriter = Writer.Create(new PooledBuffer(), session: null!);
            innerWriter.WriteInt32(value.GetConsistentHashCode());
            WriteSiloAddressInner(ref innerWriter, value);
            innerWriter.Commit();
            var payloadArray = innerWriter.Output.ToArray();
            innerWriter.Dispose();

            writer.WriteVarUInt32((uint)payloadArray.Length);
            writer.Write(payloadArray);

            // 在增加私人藏匿处的这个价值之前,先通过共用藏匿处进行入帐,希望减少重复。
            (_, payloadArray) = SharedCache.GetOrAdd(value, static (key, encoded) => (key, encoded), payloadArray);

            // 如果发生散列碰撞,那么最后一次看到进入就会永远赢。
            cacheEntry.Encoded = payloadArray;
            cacheEntry.Value = value;
            cacheEntry.LastSeen = currentTimestamp;
        }

        private static void WriteSiloAddressInner<TBufferWriter>(ref Writer<TBufferWriter> writer, SiloAddress value) where TBufferWriter : IBufferWriter<byte>
        {
            var ep = value.Endpoint;

            // IPIP 实施伙伴
            IPAddressCodec.WriteRaw(ref writer, ep.Address);

            // 港口端端口
            writer.WriteVarUInt16((ushort)ep.Port);

            // 代 代
            writer.WriteInt32(value.Generation);
        }
    }
}
