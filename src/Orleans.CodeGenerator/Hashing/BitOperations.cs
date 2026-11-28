// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace Orleans.CodeGenerator.Hashing;

internal static class BitOperations
{
    /// <summary>
    /// 将指定值向左循环移位指定的位数。
    /// 行为类似于 x86 指令 ROL。
    /// </summary>
    /// <param name="value">要循环移位的值。</param>
    /// <param name="offset">要循环移位的位数。
    /// 超出 [0..31] 范围的任何值均按模 32 同余处理。</param>
    /// <returns>循环移位后的值。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));
}
