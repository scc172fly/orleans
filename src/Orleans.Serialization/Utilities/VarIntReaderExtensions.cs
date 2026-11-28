using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// 用于处理变长整数的扩展方法。
    /// </summary>
    public static class VarIntReaderExtensions
    {
        /// <summary>
        /// 读取一个可变宽度的 <see cref="sbyte"/>。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ReadVarInt8<TInput>(this ref Reader<TInput> reader) => ZigZagDecode(checked((byte)reader.ReadVarUInt32()));

        /// <summary>
        /// 读取一个可变宽度的 <see cref="ushort"/>。（译注：此方法的返回类型实际为 short。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadVarInt16<TInput>(this ref Reader<TInput> reader) => ZigZagDecode(checked((ushort)reader.ReadVarUInt32()));

        /// <summary>
        /// 读取一个可变宽度的 <see cref="byte"/>。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadVarUInt8<TInput>(this ref Reader<TInput> reader) => checked((byte)reader.ReadVarUInt32());

        /// <summary>
        /// 读取一个可变宽度的 <see cref="ushort"/>。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadVarUInt16<TInput>(this ref Reader<TInput> reader) => checked((ushort)reader.ReadVarUInt32());

        /// <summary>
        /// 读取一个可变宽度的 <see cref="int"/>。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadVarInt32<TInput>(this ref Reader<TInput> reader) => ZigZagDecode(reader.ReadVarUInt32());

        /// <summary>
        /// 读取一个可变宽度的 <see cref="long"/>。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadVarInt64<TInput>(this ref Reader<TInput> reader) => ZigZagDecode(reader.ReadVarUInt64());

        /// <summary>
        /// 读取一个可变宽度的 <see cref="byte"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadUInt8<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarUInt8(),
            WireType.Fixed32 => checked((byte)reader.ReadUInt32()),
            WireType.Fixed64 => checked((byte)reader.ReadUInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((byte)UInt128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<byte>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="ushort"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarUInt16(),
            WireType.Fixed32 => checked((ushort)reader.ReadUInt32()),
            WireType.Fixed64 => checked((ushort)reader.ReadUInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((ushort)UInt128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<ushort>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="uint"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarUInt32(),
            WireType.Fixed32 => reader.ReadUInt32(),
            WireType.Fixed64 => checked((uint)reader.ReadUInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((uint)UInt128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<uint>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="ulong"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarUInt64(),
            WireType.Fixed32 => reader.ReadUInt32(),
            WireType.Fixed64 => reader.ReadUInt64(),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((ulong)UInt128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<ulong>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="sbyte"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ReadInt8<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarInt8(),
            WireType.Fixed32 => checked((sbyte)reader.ReadInt32()),
            WireType.Fixed64 => checked((sbyte)reader.ReadInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((sbyte)Int128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<sbyte>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="short"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadInt16<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarInt16(),
            WireType.Fixed32 => checked((short)reader.ReadInt32()),
            WireType.Fixed64 => checked((short)reader.ReadInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((short)Int128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<short>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="int"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32<TInput>(this ref Reader<TInput> reader, WireType wireType)
        {
            if (wireType == WireType.VarInt)
            {
                return reader.ReadVarInt32();
            }

            return ReadInt32Slower(ref reader, wireType);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadInt32Slower<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.Fixed32 => reader.ReadInt32(),
            WireType.Fixed64 => checked((int)reader.ReadInt64()),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((int)Int128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<int>(nameof(wireType)),
        };

        /// <summary>
        /// 读取一个可变宽度的 <see cref="long"/>。（译注：此重载根据 wireType 选择编码格式，并不限于可变宽度编码。）
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="wireType">线协议类型。</param>
        /// <returns>可变宽度整数。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadInt64<TInput>(this ref Reader<TInput> reader, WireType wireType) => wireType switch
        {
            WireType.VarInt => reader.ReadVarInt64(),
            WireType.Fixed32 => reader.ReadInt32(),
            WireType.Fixed64 => reader.ReadInt64(),
#if NET7_0_OR_GREATER
            WireType.LengthPrefixed => checked((long)Int128Codec.ReadRaw(ref reader)),
#endif
            _ => ExceptionHelper.ThrowArgumentOutOfRange<long>(nameof(wireType)),
        };

        private const sbyte Int8Msb = unchecked((sbyte)0x80);
        private const short Int16Msb = unchecked((short)0x8000);
        private const int Int32Msb = unchecked((int)0x80000000);
        private const long Int64Msb = unchecked((long)0x8000000000000000);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static sbyte ZigZagDecode(byte encoded)
        {
            var value = (sbyte)encoded;
            return (sbyte)(-(value & 0x01) ^ ((sbyte)(value >> 1) & ~Int8Msb));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short ZigZagDecode(ushort encoded)
        {
            var value = (short)encoded;
            return (short)(-(value & 0x01) ^ ((short)(value >> 1) & ~Int16Msb));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ZigZagDecode(uint encoded)
        {
            var value = (int)encoded;
            return -(value & 0x01) ^ ((value >> 1) & ~Int32Msb);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ZigZagDecode(ulong encoded)
        {
            var value = (long)encoded;
            return -(value & 0x01L) ^ ((value >> 1) & ~Int64Msb);
        }
    }
}
