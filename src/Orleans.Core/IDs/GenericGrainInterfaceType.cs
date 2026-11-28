using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.TypeSystem;
using Orleans.Utilities;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示使用类型参数进行参数化的 <see cref="GrainInterfaceType"/>。
    /// </summary>
    [Immutable]
    public readonly struct GenericGrainInterfaceType
    {
        /// <summary>
        /// 初始化 <see cref="GenericGrainInterfaceType"/> 结构的新实例。
        /// </summary>
        /// <param name="value">底层 Grain 接口类型。</param>
        /// <param name="arity">泛型类型参数的数量。</param>
        private GenericGrainInterfaceType(GrainInterfaceType value, int arity)
        {
            Value = value;
            Arity = arity;
        }

        /// <summary>
        /// 底层 <see cref="GrainInterfaceType"/>。
        /// </summary>
        public GrainInterfaceType Value { get; }

        /// <summary>
        /// 泛型类型的类型参数数量。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 如果此实例包含具体类型参数，则返回 <see langword="true" />。
        /// </summary>
        public bool IsConstructed => TypeConverterExtensions.IsConstructed(this.Value.Value);

        /// <summary>
        /// 返回与所提供值对应的泛型接口 ID。
        /// </summary>
        public static bool TryParse(GrainInterfaceType grainType, out GenericGrainInterfaceType result)
        {
            if (grainType.IsDefault)
            {
                result = default;
                return false;
            }

            var arity = TypeConverterExtensions.GetGenericTypeArity(grainType.Value);
            if (arity > 0)
            {
                result = new GenericGrainInterfaceType(grainType, arity);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// 返回此实例的未构造版本。
        /// </summary>
        public GenericGrainInterfaceType GetGenericGrainType()
        {
            var generic = TypeConverterExtensions.GetDeconstructed(Value.Value);
            return new GenericGrainInterfaceType(new GrainInterfaceType(generic), Arity);
        }

        /// <summary>
        /// 返回此实例的已构造版本。
        /// </summary>
        public GenericGrainInterfaceType Construct(TypeConverter formatter, params Type[] typeArguments)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            ArgumentNullException.ThrowIfNull(typeArguments);

            if (Arity != typeArguments.Length)
            {
                ThrowIncorrectArgumentLength(typeArguments);
            }

            var constructed = formatter.GetConstructed(this.Value.Value, typeArguments);
            return new GenericGrainInterfaceType(new GrainInterfaceType(constructed), Arity);
        }

        /// <summary>
        /// 返回构造此实例时使用的类型实参。
        /// </summary>
        public Type[] GetArguments(TypeConverter formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            return formatter.GetArguments(this.Value.Value);
        }

        /// <summary>
        /// 返回当前实例的 UTF-8 字符串表示。
        /// </summary>
        public override string ToString() => Value.ToString();

        [DoesNotReturn]
        private void ThrowIncorrectArgumentLength(Type[] typeArguments) => throw new ArgumentException($"Incorrect number of type arguments, {typeArguments.Length}, to construct a generic grain type with arity {Arity}.", nameof(typeArguments));
    }
}
