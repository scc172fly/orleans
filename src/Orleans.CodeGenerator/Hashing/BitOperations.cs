// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Runtime.CompilerServices;

namespace Orleans.CodeGenerator.Hashing;

internal static class BitOperations
{
    /// <summary>
    /// 旋转指定的值,以指定位数为单位。
    /// 类似行为与x86指令ROL相似。
    /// </summary>
    /// <param name="value">要旋转的值。</param>
    /// <param name="offset">要旋转的位数 。
    /// 范围[0.31] 以外的任何值均被视为同质型32。</param>
    /// <returns>旋转值。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));
}
