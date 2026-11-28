using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Orleans.Serialization.Utilities
{
#if NETCOREAPP3_1_OR_GREATER
#else
    /// <summary>
    /// BitOperations 的兼容性实现（polyfill）。
    /// </summary>
    // 提取自 https://github.com/dotnet/runtime/blob/e13871cb275b9f53fa82285b2a81ada28a859b50/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs
    internal static class BitOperations
    {
        private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn => new byte[32]
        {
            00, 01, 28, 02, 29, 14, 24, 03,
            30, 22, 20, 15, 25, 17, 04, 08,
            31, 27, 13, 23, 21, 19, 16, 07,
            26, 12, 18, 06, 11, 05, 10, 09
        };

        private static ReadOnlySpan<byte> Log2DeBruijn => new byte[32]
        {
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31
        };

        /// <summary>
        /// 返回指定值以 2 为底的对数，结果向下取整。
        /// 注意，按照约定，输入值为 0 时返回 0，因为 log(0) 未定义。
        /// </summary>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2(uint value)
        {
            // 通过将最低有效位（LSB）设为 1，满足输入 0 时返回 0 的约定。
            // Log(1) 为 0；对于大于 1 的值，将最低有效位设为 1 不会改变 log2 的结果。
            value |= 1;

            // 回退实现约定输入 0 时返回 0。
            return Log2SoftwareFallback(value);
        }

        /// <summary>
        /// 返回指定值以 2 为底的对数，结果向下取整。
        /// 注意，按照约定，输入值为 0 时返回 0，因为 log(0) 未定义。
        /// </summary>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2(ulong value)
        {
            value |= 1;

            uint hi = (uint)(value >> 32);

            if (hi == 0)
            {
                return Log2((uint)value);
            }

            return 32 + Log2(hi);
        }

        /// <summary>
        /// 返回指定值以 2 为底的对数，结果向下取整。
        /// 注意，按照约定，输入值为 0 时返回 0，因为 Log(0) 未定义。
        /// 不直接使用任何硬件内部函数，也不会产生分支。
        /// </summary>
        /// <param name="value">值。</param>
        private static int Log2SoftwareFallback(uint value)
        {
            // 方法体较大，因此不使用 AggressiveInlining。
            // 遵循输入 0 时返回 0 的约定（Log(0) 未定义）。

            // 将最高有效位之后的所有位填充为 1，例如 00010010 变为 00011111。
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // uint.MaxValue >> 27 的结果始终在 [0 - 31] 范围内，因此使用 Unsafe.AddByteOffset 避免边界检查。
            return Unsafe.AddByteOffset(
                // 使用 de Bruijn 序列，k=2，n=5 (2 的 5 次方 = 32)：0b_0000_0111_1100_0100_1010_1100_1101_1101u。
                ref MemoryMarshal.GetReference(Log2DeBruijn),
                // 在 32 位平台上将 uint|long 转换为 IntPtr 会执行开销较高且此处不需要的溢出检查。
                (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
        }

        /// <summary>
        /// 计算整数值末尾连续零位的数量。
        /// 行为类似于 x86 指令 TZCNT。
        /// </summary>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(uint value)
        {
            // 未加保护的回退实现约定输入 0 时返回 0，而 BSF 指令在输入 0 时结果未定义。
            if (value == 0)
            {
                return 32;
            }

            // uint.MaxValue >> 27 的结果始终在 [0 - 31] 范围内，因此使用 Unsafe.AddByteOffset 避免边界检查。
            return Unsafe.AddByteOffset(
                // 使用 de Bruijn 序列，k=2，n=5 (2 的 5 次方 = 32)：0b_0000_0111_0111_1100_1011_0101_0011_0001u。
                ref MemoryMarshal.GetReference(TrailingZeroCountDeBruijn),
                // 在 32 位平台上将 uint|long 转换为 IntPtr 会执行开销较高且此处不需要的溢出检查。
                (IntPtr)(int)(((value & (uint)-(int)value) * 0x077CB531u) >> 27)); // 通过多次类型转换减少冗余的 conv.u8 指令。
        }

        /// <summary>
        /// 计算位掩码末尾连续零位的数量。
        /// 行为类似于 x86 指令 TZCNT。
        /// </summary>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(ulong value)
        {
            uint lo = (uint)value;

            if (lo == 0)
            {
                return 32 + TrailingZeroCount((uint)(value >> 32));
            }

            return TrailingZeroCount(lo);
        }

        /// <summary>将给定整数向上取整到 2 的整数次幂。</summary>
        /// <param name="value">值。</param>
        /// <returns>
        /// 大于或等于 <paramref name="value"/> 的最小 2 的整数次幂。
        /// 如果 <paramref name="value"/> 为 0 或计算结果溢出，则返回 0。
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RoundUpToPowerOf2(uint value)
        {
            // 基于 https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
#endif
}
