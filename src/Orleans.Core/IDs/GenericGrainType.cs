using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Orleans.Serialization.TypeSystem;
using Orleans.Utilities;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示使用类型参数参数参数参数参数的 <see cref="GrainType"/> 。
    /// </summary>
    [Immutable]
    public readonly struct GenericGrainType : IEquatable<GenericGrainType>
    {
        /// <summary>
        /// 初始化 <see cref="GenericGrainType"/> 结构的新实例。
        /// </summary>
        /// <param name="grainType">根本 grain 类型。</param>
        /// <param name="arity">普遍平等的普遍性 grain 类型。</param>
        private GenericGrainType(GrainType grainType, int arity)
        {
            GrainType = grainType;
            Arity = arity;
        }

        /// <summary>
        /// 底部 grain 类型。
        /// </summary>
        public GrainType GrainType { get; }

        /// <summary>
        /// grain 类型的一般对等性。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 如果此实例包含具体类型参数, 返回 <see langword="true" /> 。
        /// </summary>
        public bool IsConstructed => TypeConverterExtensions.IsConstructed(this.GrainType.Value);

        /// <summary>
        /// 返回与所提供的值相对应的通用 grain 类型。
        /// </summary>
        public static bool TryParse(GrainType grainType, out GenericGrainType result)
        {
            var arity = TypeConverterExtensions.GetGenericTypeArity(grainType.Value);
            if (arity > 0)
            {
                result = new GenericGrainType(grainType, arity);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// 返回此实例的非构建版本。
        /// </summary>
        public GenericGrainType GetUnconstructedGrainType()
        {
            var generic = TypeConverterExtensions.GetDeconstructed(GrainType.Value);
            return new GenericGrainType(new GrainType(generic), Arity);
        }

        /// <summary>
        /// 返回此实例的构建版本。
        /// </summary>
        public GenericGrainType Construct(TypeConverter formatter, params Type[] typeArguments)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            ArgumentNullException.ThrowIfNull(typeArguments);

            if (Arity != typeArguments.Length)
            {
                ThrowIncorrectArgumentLength(typeArguments);
            }

            var constructed = formatter.GetConstructed(this.GrainType.Value, typeArguments);
            return new GenericGrainType(new GrainType(constructed), Arity);
        }

        /// <summary>
        /// 使用所提供的类型转换器获取类型参数。
        /// </summary>
        /// <param name="converter">型转换器</param>
        /// <returns>类型参数。</returns>
        public Type[] GetArguments(TypeConverter converter)
        {
            ArgumentNullException.ThrowIfNull(converter);
            return converter.GetArguments(this.GrainType.Value);
        }

        /// <inheritdoc/>
        public override string ToString() => this.GrainType.ToString();

        /// <inheritdoc/>
        public bool Equals(GenericGrainType other) => this.GrainType.Equals(other.GrainType);

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is GenericGrainType other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => this.GrainType.GetHashCode();

        [DoesNotReturn]
        private void ThrowIncorrectArgumentLength(Type[] typeArguments) => throw new ArgumentException($"Incorrect number of type arguments, {typeArguments.Length}, to construct a generic grain type with arity {Arity}.", nameof(typeArguments));
    }
}
