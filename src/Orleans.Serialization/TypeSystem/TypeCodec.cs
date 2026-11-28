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
    /// 提供类型序列化与反序列化功能。
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
        /// 写入带长度前缀的类型。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="type">类型。</param>
        public void WriteLengthPrefixed<TBufferWriter>(ref Writer<TBufferWriter> writer, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            var key = _typeCache.GetOrAdd(type, _getTypeKey);
            writer.WriteVarUInt32((uint)key.TypeName.Length);
            writer.Write(key.TypeName);
        }

        /// <summary>
        /// 写入类型。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
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
        /// 读取类型。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>成功读取时返回该类型；否则为 <see langword="null" />。</returns>
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

            // 遍历搜索。
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

                // 尝试下一个条目。
                ++candidateHashCode;
            }

            // 为类型名称分配字符串。
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
                    // 将类型插入第一个可用位置。
                }
            }

            return type;
        }

        /// <summary>
        /// 读取带长度前缀的类型。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>类型。</returns>
        public unsafe Type ReadLengthPrefixed<TInput>(ref Reader<TInput> reader)
        {
            var count = (int)reader.ReadVarUInt32();

            if (!reader.TryReadBytes(count, out var typeName))
            {
                typeName = reader.ReadBytes((uint)count);
            }

            // 为类型名称分配字符串。
            string typeNameString;
            fixed (byte* typeNameBytes = typeName)
            {
                typeNameString = Encoding.UTF8.GetString(typeNameBytes, typeName.Length);
            }

            var type = _typeConverter.Parse(typeNameString);
            return type;
        }

        /// <summary>
        /// 尝试读取类型，用于诊断。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="type">类型。</param>
        /// <param name="typeString">字符串形式的类型名称。</param>
        /// <returns><see langword="true" />，如果成功读取类型；否则为 <see langword="false" />。</returns>
        public unsafe bool TryReadForAnalysis<TInput>(ref Reader<TInput> reader, [NotNullWhen(true)] out Type? type, out string typeString)
        {
            var version = reader.ReadByte();
            var hashCode = reader.ReadInt32();
            var count = (int)reader.ReadVarUInt32();

            if (!reader.TryReadBytes(count, out var typeName))
            {
                typeName = reader.ReadBytes((uint)count);
            }

            // 为类型名称分配字符串。
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
        /// 表示用于序列化的命名类型。
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
