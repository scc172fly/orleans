using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Orleans.Serialization.Buffers;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 序列和分机类型功能。
    /// </summary>
    public sealed class TypeCodec
    {
        private const byte Version1 = 1;
        private readonly ConcurrentDictionary<Type, TypeKey> _typeCache = new ConcurrentDictionary<Type, TypeKey>();
        private readonly ConcurrentDictionary<int, (TypeKey Key, Type Type)> _typeKeyCache = new ConcurrentDictionary<int, (TypeKey, Type)>();
        private readonly TypeConverter _typeConverter;
        private readonly Func<Type, TypeKey> _getTypeKey;

        /// <summary>
        /// 初始化 <see cref="TypeCodec"/> 类的新实例。
        /// </summary>
        /// <param name="typeConverter">类型转换器。</param>
        public TypeCodec(TypeConverter typeConverter)
        {
            _typeConverter = typeConverter;
            _getTypeKey = type => new TypeKey(Encoding.UTF8.GetBytes(_typeConverter.Format(type)));
        }

        /// <summary>
        /// 以长度前缀写入类型。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="type">类型。</param>
        public void WriteLengthPrefixed<TBufferWriter>(ref Writer<TBufferWriter> writer, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            var key = _typeCache.GetOrAdd(type, _getTypeKey);
            writer.WriteVarUInt32((uint)key.TypeName.Length);
            writer.Write(key.TypeName);
        }

        /// <summary>
        /// 写一个类型。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="type">类型。</param>
        public void WriteEncodedType<TBufferWriter>(ref Writer<TBufferWriter> writer, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            var key = _typeCache.GetOrAdd(type, _getTypeKey);
            writer.WriteByte(Version1);
            writer.WriteInt32(key.HashCode);
            writer.WriteVarUInt32((uint)key.TypeName.Length);
            writer.Write(key.TypeName);
        }

        /// <summary>
        /// 读取类型 。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <returns>如果成功读取了类型, <see langword="null" /> 否则。</returns>
        public unsafe Type? TryRead<TInput>(ref Reader<TInput> reader)
        {
            var version = reader.ReadByte();
            if (version != Version1)
            {
                ThrowUnsupportedVersion(version);
            }

            var hashCode = reader.ReadInt32();
            var count = (int)reader.ReadVarUInt32();

            if (!reader.TryReadBytes(count, out var typeName))
            {
                typeName = reader.ReadBytes((uint)count);
            }

            // 搜索通过 
            var candidateHashCode = hashCode;
            while (_typeKeyCache.TryGetValue(candidateHashCode, out var entry))
            {
                var existingKey = entry.Key;
                if (existingKey.HashCode != hashCode)
                {
                    break;
                }

                if (existingKey.TypeName.AsSpan().SequenceEqual(typeName))
                {
                    return entry.Type;
                }

                // 试试下一个条目
                ++candidateHashCode;
            }

            // 为类型名称指定字符串。
            string typeNameString;
            fixed (byte* typeNameBytes = typeName)
            {
                typeNameString = Encoding.UTF8.GetString(typeNameBytes, typeName.Length);
            }

            Type? type = null;
            if (_typeConverter.TryParse(typeNameString, out var parsedType))
            {
                type = parsedType;
                var key = new TypeKey(hashCode, typeName.ToArray());
                while (!_typeKeyCache.TryAdd(candidateHashCode++, (key, type)))
                {
                    // 在第一个可用位置插入类型。
                }
            }

            return type;
        }

        /// <summary>
        /// 读取长度前缀类型。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <returns>类型。</returns>
        public unsafe Type ReadLengthPrefixed<TInput>(ref Reader<TInput> reader)
        {
            var count = (int)reader.ReadVarUInt32();

            if (!reader.TryReadBytes(count, out var typeName))
            {
                typeName = reader.ReadBytes((uint)count);
            }

            // 为类型名称指定字符串。
            string typeNameString;
            fixed (byte* typeNameBytes = typeName)
            {
                typeNameString = Encoding.UTF8.GetString(typeNameBytes, typeName.Length);
            }

            var type = _typeConverter.Parse(typeNameString);
            return type;
        }

        /// <summary>
        /// 尝试阅读一种类型,以便诊断。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="type">类型。</param>
        /// <param name="typeString">以字符串为类型名称。</param>
        /// <returns><see langword="true" /> 如果一个类型读得成功,则 <see langword="false" /> 。</returns>
        public unsafe bool TryReadForAnalysis<TInput>(ref Reader<TInput> reader, [NotNullWhen(true)] out Type? type, out string typeString)
        {
            var version = reader.ReadByte();
            var hashCode = reader.ReadInt32();
            var count = (int)reader.ReadVarUInt32();

            if (!reader.TryReadBytes(count, out var typeName))
            {
                typeName = reader.ReadBytes((uint)count);
            }

            // 为类型名称指定字符串。
            string typeNameString;
            fixed (byte* typeNameBytes = typeName)
            {
                typeNameString = Encoding.UTF8.GetString(typeNameBytes, count);
            }

            _ = _typeConverter.TryParse(typeNameString, out type);
            var key = new TypeKey(hashCode, typeName.ToArray());
            typeString = key.ToString();
            return type is not null;
        }

        [DoesNotReturn]
        private static void ThrowUnsupportedVersion(byte version) => throw new NotSupportedException($"Type encoding version {version} is not supported");

        /// <summary>
        /// 指一个名称类型,用于序列化。
        /// </summary>
        internal readonly struct TypeKey
        {
            public readonly int HashCode;

            public readonly byte[] TypeName;

            public TypeKey(int hashCode, byte[] key)
            {
                HashCode = hashCode;
                TypeName = key;
            }

            public TypeKey(byte[] key)
            {
                TypeName = key;
                Unsafe.SkipInit(out int hash);
                XxHash32.TryHash(key, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref hash, 1)), out _);
                HashCode = BitConverter.IsLittleEndian ? hash : BinaryPrimitives.ReverseEndianness(hash);
            }

            public bool Equals(in TypeKey other)
            {
                if (HashCode != other.HashCode)
                {
                    return false;
                }

                var a = TypeName;
                var b = other.TypeName;
                return ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);
            }

            public override bool Equals(object? obj) => obj is TypeKey key && Equals(key);

            public override int GetHashCode() => HashCode;

            public override string ToString() => $"TypeName \"{Encoding.UTF8.GetString(TypeName)}\" (hash {HashCode:X8})";
        }
    }
}
