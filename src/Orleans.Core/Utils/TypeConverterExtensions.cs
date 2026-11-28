using System;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Utilities
{
    /// <summary>
    /// 用于操作 <see cref="TypeConverter"/> 的扩展方法。
    /// </summary>
    internal static class TypeConverterExtensions
    {
        private const char GenericTypeIndicator = '`';
        private const char StartArgument = '[';

        /// <summary>
        /// 如果提供的类型字符串表示泛型类型，则返回 true。
        /// </summary>
        public static bool IsGenericType(IdSpan type) => type.AsSpan().IndexOf((byte)GenericTypeIndicator) >= 0;

        /// <summary>
        /// 返回指定 Grain 类型的泛型参数数量。
        /// </summary>
        public static int GetGenericTypeArity(IdSpan type)
        {
            var typeSpan = type.AsSpan();
            var startIndex = typeSpan.IndexOf((byte)GenericTypeIndicator) + 1;
            if (startIndex <= 0 || startIndex >= typeSpan.Length)
            {
                return 0;
            }

            int endIndex;
            for (endIndex = startIndex; endIndex < typeSpan.Length; endIndex++)
            {
                var c = typeSpan[endIndex];
                if (c is < ((byte)'0') or > ((byte)'9'))
                {
                    break;
                }
            }

            if (endIndex > startIndex && Utf8Parser.TryParse(typeSpan[startIndex..endIndex], out int arity, out _))
            {
                return arity;
            }

            throw new InvalidOperationException($"Unable to parse arity from type \"{type}\"");
        }

        /// <summary>
        /// 如果提供的类型字符串表示已构造的泛型类型，则返回 true。
        /// </summary>
        public static bool IsConstructed(IdSpan type) => type.AsSpan().IndexOf((byte)StartArgument) > 0;

        /// <summary>
        /// 返回所提供泛型类型的未构造形式。
        /// </summary>
        public static IdSpan GetDeconstructed(IdSpan type)
        {
            var span = type.AsSpan();
            var index = span.IndexOf((byte)StartArgument);
            return index <= 0 ? type : new IdSpan(span[..index].ToArray());
        }

        /// <summary>
        /// 返回所提供泛型类型的已构造形式。
        /// </summary>
        public static IdSpan GetConstructed(this TypeConverter formatter, IdSpan unconstructed, params Type[] typeArguments)
        {
            var typeString = unconstructed.AsSpan();
            var indicatorIndex = typeString.IndexOf((byte)GenericTypeIndicator);
            var arityString = typeString[(indicatorIndex + 1)..];
            if (indicatorIndex < 0 || arityString.IndexOf((byte)StartArgument) >= 0)
            {
                throw new InvalidOperationException("Cannot construct an already-constructed type");
            }

            if (!Utf8Parser.TryParse(arityString, out int arity, out var len) || len < arityString.Length || typeArguments.Length != arity)
            {
                throw new InvalidOperationException($"Insufficient number of type arguments, {typeArguments.Length}, provided while constructing type \"{unconstructed}\"");
            }

            var typeSpecs = new TypeSpec[typeArguments.Length];
            for (var i = 0; i < typeArguments.Length; i++)
            {
                typeSpecs[i] = RuntimeTypeNameParser.Parse(formatter.Format(typeArguments[i]));
            }

            var constructed = new ConstructedGenericTypeSpec(new NamedTypeSpec(null!, unconstructed.ToString()!, typeArguments.Length), typeArguments.Length, typeSpecs).Format();
            return IdSpan.Create(constructed);
        }

        /// <summary>
        /// 使用所提供已构造接口类型的类型实参，返回所提供泛型 Grain 类型的已构造形式。
        /// </summary>
        public static GrainType GetConstructed(this GenericGrainType genericGrainType, GenericGrainInterfaceType genericGrainInterfaceType)
        {
            if (genericGrainType.Arity != genericGrainInterfaceType.Arity)
            {
                ThrowGenericArityMismatch(genericGrainType, genericGrainInterfaceType);
            }

            var grainType = genericGrainType.GrainType;
            var typeArguments = genericGrainInterfaceType.Value;
            var args = typeArguments.Value.AsSpan();
            var index = args.IndexOf((byte)StartArgument);
            if (index <= 0) return grainType; // 如果未提供类型实参，则当前逻辑预期得到未构造形式（不过之后的 Grain 调用无论如何都会失败……）。
            args = args[index..];

            var type = grainType.Value.AsSpan();
            var buf = new byte[type.Length + args.Length];
            type.CopyTo(buf);
            args.CopyTo(buf.AsSpan(type.Length));
            return new GrainType(buf);
        }

        /// <summary>
        /// 返回所提供已构造泛型类型字符串中的类型实参。
        /// </summary>
        public static Type[] GetArguments(this TypeConverter formatter, IdSpan constructed)
        {
            var str = constructed.AsSpan();
            var index = str.IndexOf((byte)StartArgument);
            if (index <= 0)
            {
                return Array.Empty<Type>();
            }

            var safeString = "safer" + Encoding.UTF8.GetString(str[str.IndexOf((byte)GenericTypeIndicator)..]);
            var parsed = RuntimeTypeNameParser.Parse(safeString);
            if (!(parsed is ConstructedGenericTypeSpec spec))
            {
                throw new InvalidOperationException($"Unable to correctly parse grain type {constructed}");
            }

            var result = new Type[spec.Arguments.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var arg = spec.Arguments[i];
                var formattedArg = arg.Format();
                result[i] = formatter.Parse(formattedArg);
                if (result[i] is null)
                {
                    throw new InvalidOperationException($"Unable to parse argument \"{formattedArg}\" as a type for grain type \"{constructed}\"");
                }
            }

            return result;
        }

        [DoesNotReturn]
        private static void ThrowGenericArityMismatch(GenericGrainType genericGrainType, GenericGrainInterfaceType genericInterfaceType)
            => throw new ArgumentException($"Cannot construct generic grain \"{genericGrainType.GrainType}\" using arguments from generic interface \"{genericInterfaceType}\" because the generic arities are not equal: {genericGrainType.Arity} is not equal to {genericInterfaceType.Arity}.");
    }
}
