using System;
using System.Collections.Generic;

namespace Orleans.Concurrency
{
    /// <summary>
    /// 携带不可变数据的工作类。
    /// </summary>
    /// <remarks>
    /// 已知不可变对象由 Orleans 序列器给特殊快速路径处理 
    /// 简言之,它允许 DeepCopy 中跳行,在发送和接收者所在的邮件发送时跳过 grain 相同 silo.
    /// 
    /// 一个非常常见的易变性使用模式是将位元[]参数传送到 grain。 
    /// 如果程序知道它不会改变字节的内容[ ] (例如,如果它包含从磁盘读取的静态图像文件的字节) 。
    /// 然后,将字节[]的参数标记为可变 <c>Immutable</c>,可以节省大量记忆使用和电文传输。
    /// </remarks>
    /// <typeparam name="T">由此不可变</typeparam>包绑的数据类型
    [GenerateSerializer, Immutable]
    public readonly struct Immutable<T> : IEquatable<Immutable<T>>
    {
        /// <summary> 返回引用在此不可调和的包装器中存储的原始值。 </summary>
        [Id(0)]
        public readonly T Value;

        /// <summary>
        /// 构造器, 以新的不易变换的包装器包扎指定的数据对象 。
        /// </summary>
        /// <param name="value">数值要包装并标记为不可更改。</param>
        public Immutable(T value) => Value = value;

        /// <inheritdoc/>
        public bool Equals(Immutable<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Immutable<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value!);

        /// <summary>
        /// 确定两个不可变的包装纸是否包含等值。
        /// </summary>
        public static bool operator ==(Immutable<T> left, Immutable<T> right) => left.Equals(right);

        /// <summary>
        /// 确定两个不可改变的包装纸是否含有不平等值。
        /// </summary>
        public static bool operator !=(Immutable<T> left, Immutable<T> right) => !left.Equals(right);
    }

    /// <summary>
    /// 对所有对象添加 .AsImmutable 方法的工具类。
    /// </summary>
    public static class ImmutableExtensions
    {
        /// <summary>
        /// 返回以 <c>Immutable</c>包装的此值的扩展方法。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">要包装的值。</param>
        /// <returns>原对象周围的不可变包装。</returns>
        /// <seealso cref="Immutable{T}"/>"/>
        public static Immutable<T> AsImmutable<T>(this T value) => new(value);
    }
}
