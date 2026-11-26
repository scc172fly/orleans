using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 指定提供商内部的频道
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct ChannelId : IEquatable<ChannelId>, IComparable<ChannelId>, ISerializable, ISpanFormattable
    {
        [Id(0)]
        private readonly byte[] fullKey;

        [Id(1)]
        private readonly ushort keyIndex;

        [Id(2)]
        private readonly int hash;

        /// <summary>
        /// 拿到了全部钥匙
        /// </summary>
        /// <value>整键。</value>
        public ReadOnlyMemory<byte> FullKey => fullKey;

        /// <summary>
        /// 获得命名空间。
        /// </summary>
        /// <value>命名空间。</value>
        public ReadOnlyMemory<byte> Namespace => fullKey.AsMemory(0, this.keyIndex);

        /// <summary>
        /// 拿到钥匙
        /// </summary>
        /// <value>密钥。</value>
        public ReadOnlyMemory<byte> Key => fullKey.AsMemory(this.keyIndex);

        private ChannelId(byte[] fullKey, ushort keyIndex, int hash)
        {
            this.fullKey = fullKey;
            this.keyIndex = keyIndex;
            this.hash = hash;
        }

        internal ChannelId(byte[] fullKey, ushort keyIndex)
            : this(fullKey, keyIndex, (int)StableHash.ComputeHash(fullKey))
        {
        }

        private ChannelId(SerializationInfo info, StreamingContext context)
        {
            fullKey = (byte[])info.GetValue("fk", typeof(byte[]))!;
            this.keyIndex = info.GetUInt16("ki");
            this.hash = info.GetInt32("fh");
        }

        /// <summary>
        /// 初始化 <see cref="ChannelId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static ChannelId Create(ReadOnlySpan<byte> ns, ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
                throw new ArgumentNullException(nameof(key));

            if (!ns.IsEmpty)
            {
                var fullKeysBytes = new byte[ns.Length + key.Length];
                ns.CopyTo(fullKeysBytes.AsSpan());
                key.CopyTo(fullKeysBytes.AsSpan(ns.Length));
                return new(fullKeysBytes, (ushort)ns.Length);
            }
            else
            {
                return new(key.ToArray(), 0);
            }
        }

        /// <summary>
        /// 初始化 <see cref="ChannelId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static ChannelId Create(string ns, Guid key)
        {
            if (ns is null)
            {
                var buf = new byte[32];
                Utf8Formatter.TryFormat(key, buf, out var len, 'N');
                Debug.Assert(len == 32);
                return new ChannelId(buf, 0);
            }
            else
            {
                var nsLen = Encoding.UTF8.GetByteCount(ns);
                var buf = new byte[nsLen + 32];
                Encoding.UTF8.GetBytes(ns, 0, ns.Length, buf, 0);
                Utf8Formatter.TryFormat(key, buf.AsSpan(nsLen), out var len, 'N');
                Debug.Assert(len == 32);
                return new ChannelId(buf, (ushort)nsLen);
            }
        }

        /// <summary>
        /// 初始化 <see cref="ChannelId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static ChannelId Create(string ns, string key)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (ns is null)
                return new ChannelId(Encoding.UTF8.GetBytes(key), 0);

            var nsLen = Encoding.UTF8.GetByteCount(ns);
            var keyLen = Encoding.UTF8.GetByteCount(key);
            var buf = new byte[nsLen + keyLen];
            Encoding.UTF8.GetBytes(ns, 0, ns.Length, buf, 0);
            Encoding.UTF8.GetBytes(key, 0, key.Length, buf, nsLen);
            return new ChannelId(buf, (ushort)nsLen);
        }

        /// <inheritdoc/>
        public int CompareTo(ChannelId other) => fullKey.AsSpan().SequenceCompareTo(other.fullKey);

        /// <inheritdoc/>
        public bool Equals(ChannelId other) => fullKey.AsSpan().SequenceEqual(other.fullKey);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ChannelId other ? this.Equals(other) : false;

        /// <summary>
        /// 比较了两个 <see cref="ChannelId"/> 平等实例。
        /// </summary>
        /// <param name="s1">第一个流身份。</param>
        /// <param name="s2">第二个流身份。</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator ==(ChannelId s1, ChannelId s2) => s1.Equals(s2);

        /// <summary>
        /// 比较了两个 <see cref="ChannelId"/> 平等实例。
        /// </summary>
        /// <param name="s1">第一个流身份。</param>
        /// <param name="s2">第二个流身份。</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator !=(ChannelId s1, ChannelId s2) => !s2.Equals(s1);

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);

            info.AddValue("fk", fullKey);
            info.AddValue("ki", this.keyIndex);
            info.AddValue("fh", this.hash);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{this}";
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var len = Encoding.UTF8.GetCharCount(fullKey);
            if (keyIndex == 0)
            {
                if (destination.Length >= len + 5)
                {
                    "null/".CopyTo(destination);
                    charsWritten = Encoding.UTF8.GetChars(fullKey, destination[5..]) + 5;
                    return true;
                }
            }
            else if (destination.Length > len)
            {
                len = Encoding.UTF8.GetChars(fullKey.AsSpan(0, keyIndex), destination);
                destination[len++] = '/';
                charsWritten = Encoding.UTF8.GetChars(fullKey.AsSpan(keyIndex), destination[len..]) + len;
                return true;
            }

            charsWritten = 0;
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode() => this.hash;

        internal uint GetUniformHashCode() => (uint)hash;

        internal uint GetKeyIndex() => keyIndex;

        /// <summary>
        /// 返回此实例的 <see cref="Key"/> 组件,作为字符串。
        /// </summary>
        /// <returns>此实例的关键组成部分。</returns>
        public string GetKeyAsString() => Encoding.UTF8.GetString(fullKey, keyIndex, fullKey.Length - keyIndex);

        /// <summary>
        /// 返回此实例的 <see cref="Namespace"/> 组件,作为字符串。
        /// </summary>
        /// <returns>此实例的命名空间组件。</returns>
        public string? GetNamespace() => keyIndex == 0 ? null : Encoding.UTF8.GetString(fullKey, 0, keyIndex);

        internal IdSpan GetKeyIdSpan() => keyIndex == 0 ? IdSpan.UnsafeCreate(fullKey, hash) : new(fullKey.AsSpan(keyIndex).ToArray());
    }

    [Serializable, GenerateSerializer, Immutable]
    internal readonly struct InternalChannelId : IEquatable<InternalChannelId>, IComparable<InternalChannelId>, ISerializable, ISpanFormattable
    {
        [Id(0)]
        public readonly ChannelId ChannelId;

        [Id(1)]
        public readonly string ProviderName;

        public InternalChannelId(string providerName, ChannelId streamId)
        {
            ProviderName = providerName;
            ChannelId = streamId;
        }

        private InternalChannelId(SerializationInfo info, StreamingContext context)
        {
            ProviderName = info.GetString("pvn")!;
            ChannelId = (ChannelId)info.GetValue("sid", typeof(ChannelId))!;
        }

        public static implicit operator ChannelId(InternalChannelId internalStreamId) => internalStreamId.ChannelId;

        public bool Equals(InternalChannelId other) => ChannelId.Equals(other) && ProviderName.Equals(other.ProviderName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is InternalChannelId other ? this.Equals(other) : false;

        public static bool operator ==(InternalChannelId s1, InternalChannelId s2) => s1.Equals(s2);

        public static bool operator !=(InternalChannelId s1, InternalChannelId s2) => !s2.Equals(s1);

        public int CompareTo(InternalChannelId other) => ChannelId.CompareTo(other.ChannelId);

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("pvn", ProviderName);
            info.AddValue("sid", ChannelId, typeof(ChannelId));
        }

        public override int GetHashCode() => HashCode.Combine(ProviderName, ChannelId);

        public override string ToString() => $"{ProviderName}/{ChannelId}";
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"{ProviderName}/{ChannelId}", out charsWritten);

        internal string? GetNamespace() => ChannelId.GetNamespace();
    }
}
