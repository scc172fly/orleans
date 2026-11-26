using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Orleans.Serialization.Utilities
{
#if NETCOREAPP3_1_OR_GREATER
#else
    /// <summary>
    /// BitOperations 复方填充。
    /// </summary>
    // 摘自 https://github.com/dotnet/runtime/blob/e13871cb275b9f53fa82285b2a81ada28a859b50/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs
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
        /// 返回指定值的整数(底)日志,基数2。
        /// 请注意,根据常规,输入值 0 返回 0, 因为对日志( 0) 未定义 。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2(uint value)
        {
            // 履行0->0合同的办法是将LSB设为1。
            // 日志(1)为0,而设定值 > 1的 LSB 不改变对日志结果。
            value |= 1;

            // 后退合同为0- >0
            return Log2SoftwareFallback(value);
        }

        /// <summary>
        /// 返回指定值的整数(底)日志,基数2。
        /// 请注意,根据常规,输入值 0 返回 0, 因为对日志( 0) 未定义 。
        /// </summary>
        /// <param name="value">数值。</param>
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
        /// 返回指定值的整数(底)日志,基数2。
        /// 请注意,根据常规,输入值 0 返回 0, 因为日志( 0) 未定义 。
        /// 不直接使用任何硬件的内在特性,也不产生分支。
        /// </summary>
        /// <param name="value">数值。</param>
        private static int Log2SoftwareFallback(uint value)
        {
            // 无 AggressiveInlining 由于方法大小大
            // 合同为0- >0(记录(0)未定义)

            // 填充末端零, 以一个填充零, eg00010010 变成00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // uint.MaxValue 27总是在范围[0 -31],所以我们使用 Unsafe.AddByteOffset 避免边框检查
            return Unsafe.AddByteOffset(
                // 使用 deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
                ref MemoryMarshal.GetReference(Log2DeBruijn),
                // 昆特隆 ->32位平台上 IntPtr 的投影使用昂贵的溢出检查,此处不需要
                (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
        }

        /// <summary>
        /// 以整数值计算末端零位数。
        /// 类似x86指令TZCNT的行为。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TrailingZeroCount(uint value)
        {
            // 无担保的后退合同为0->0, BSF合同为0>未定义
            if (value == 0)
            {
                return 32;
            }

            // uint.MaxValue 27总是在范围[0 -31],所以我们使用 Unsafe.AddByteOffset 避免边框检查
            return Unsafe.AddByteOffset(
                // 使用 deBruijn 序列, k=2, n=5 (2°5=32) : 0b_ 0000_ 011_0111_ 1100_ 1011_ 0101_ 0011_0001u
                ref MemoryMarshal.GetReference(TrailingZeroCountDeBruijn),
                // 昆特隆 ->32位平台上 IntPtr 的投影使用昂贵的溢出检查,此处不需要
                (IntPtr)(int)(((value & (uint)-(int)value) * 0x077CB531u) >> 27)); // 多播减轻冗余 conv.u8
        }

        /// <summary>
        /// 计算面具中末端零位数。
        /// 类似x86指令TZCNT的行为。
        /// </summary>
        /// <param name="value">数值。</param>
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

        /// <summary>将给定整体值四舍五入至2.</summary>
        /// <param name="value">数值。</param>
        /// <returns>
        /// 2个最小功率大于或等于 <paramref name="value"/>。
        /// 如果 <paramref name="value"/> 是 0 或结果溢出,返回 0。
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
