using System;
using System.Collections.Generic;

namespace Orleans.Concurrency
{
    /// <summary>
    /// 用于传递不可变数据的包装类。
    /// </summary>
    /// <remarks>
    /// Orleans 序列化器会对已知不可变的对象采用特殊的快速路径处理，
    /// 简而言之，如果发送方和接收方 Grain 位于同一 Silo 中，发送消息时便可跳过 DeepCopy 步骤。
    /// 
    /// Immutable 的一种常见用法是向 Grain 传递 byte[] 参数。
    /// 如果程序明确不会修改 byte[] 的内容（例如其中包含从磁盘读取的静态图像文件的字节），
    /// 那么将该 byte[] 参数标记为 <c>Immutable</c> 可以显著降低内存开销并提高消息吞吐量。
    /// </remarks>
    /// <typeparam name="T">此 Immutable 要包装的数据类型。</typeparam>
    [GenerateSerializer, Immutable]
    public readonly struct Immutable<T> : IEquatable<Immutable<T>>
    {
        /// <summary>返回此 Immutable 包装器所存储的原始值的引用。</summary>
        [Id(0)]
        public readonly T Value;

        /// <summary>
        /// 将指定数据对象包装在新的 Immutable 包装器中的构造函数。
        /// </summary>
        /// <param name="value">要包装并标记为不可变的值。</param>
        public Immutable(T value) => Value = value;

        /// <inheritdoc/>
        public bool Equals(Immutable<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Immutable<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value!);

        /// <summary>
        /// 判断两个不可变包装器是否包含相等的值。
        /// </summary>
        public static bool operator ==(Immutable<T> left, Immutable<T> right) => left.Equals(right);

        /// <summary>
        /// 判断两个不可变包装器是否包含不相等的值。
        /// </summary>
        public static bool operator !=(Immutable<T> left, Immutable<T> right) => !left.Equals(right);
    }

    /// <summary>
    /// 为所有对象添加 .AsImmutable 方法的工具类。
    /// </summary>
    public static class ImmutableExtensions
    {
        /// <summary>
        /// 扩展方法，返回用 <c>Immutable</c> 包装后的此值。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">要包装的值。</param>
        /// <returns>包装原始对象的 Immutable 包装器。</returns>
        /// <seealso cref="Immutable{T}"/>"/>
        public static Immutable<T> AsImmutable<T>(this T value) => new(value);
    }
}
