
using System;
using System.Linq;
using System.Text;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 表示类型。
    /// </summary>
    public abstract class TypeSpec
    {
        /// <summary>
        /// 将此实例格式化为可由 <see cref="RuntimeTypeNameParser"/> 解析的形式。
        /// </summary>
        public abstract string Format();
    }

    /// <summary>
    /// 表示指针（*）类型。
    /// </summary>
    public class PointerTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="PointerTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elementType">元素类型。</param>
        public PointerTypeSpec(TypeSpec elementType)
        {
            if (elementType is null)
            {
                throw new ArgumentNullException(nameof(elementType));
            }

            ElementType = elementType;
        }

        /// <summary>
        /// 获取元素类型。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}*";
    }

    /// <summary>
    /// 表示按引用（&amp;）类型。
    /// </summary>
    public class ReferenceTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="ReferenceTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elementType">元素类型。</param>
        public ReferenceTypeSpec(TypeSpec elementType)
        {
            if (elementType is null)
            {
                throw new ArgumentNullException(nameof(elementType));
            }

            ElementType = elementType;
        }

        /// <summary>
        /// 获取元素类型。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}&";
    }

    /// <summary>
    /// 表示数组类型。
    /// </summary>
    public class ArrayTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="ArrayTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elementType">数组元素类型。</param>
        /// <param name="dimensions">数组的维度数。</param>
        public ArrayTypeSpec(TypeSpec elementType, int dimensions)
        {
            if (elementType is null)
            {
                throw new ArgumentNullException(nameof(elementType));
            }

            if (dimensions <= 0)
            {
                throw new ArgumentOutOfRangeException($"An array cannot have a dimension count of {dimensions}");
            }

            ElementType = elementType;
            Dimensions = dimensions;
        }

        /// <summary>
        /// 获取数组的维度数。
        /// </summary>
        public int Dimensions { get; }

        /// <summary>
        /// 获取元素类型。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}[{new string(',', Dimensions - 1)}]";
    }

    /// <summary>
    /// 表示构造后的泛型类型。
    /// </summary>
    public class ConstructedGenericTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="ConstructedGenericTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="unconstructedType">尚未构造的泛型类型。</param>
        /// <param name="arity">预期的类型实参数量。</param>
        /// <param name="arguments">泛型类型实参。</param>
        public ConstructedGenericTypeSpec(TypeSpec unconstructedType, int arity, TypeSpec[] arguments)
        {
            if (unconstructedType is AssemblyQualifiedTypeSpec)
            {
                throw new InvalidOperationException();
            }

            if (unconstructedType is null)
            {
                throw new ArgumentNullException(nameof(unconstructedType));
            }

            if (arguments is null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            if (arity != arguments.Length)
            {
                throw new ArgumentException($"Invalid number of arguments {arguments.Length} provided while constructing generic type of arity {arity}: {unconstructedType}", nameof(arguments));
            }

            foreach (var arg in arguments)
            {
                if (arg is null)
                {
                    throw new ArgumentNullException(nameof(arguments), "Cannot construct a generic type using a null argument");
                }
            }

            UnconstructedType = unconstructedType;
            Arguments = arguments;
        }

        /// <summary>
        /// 获取尚未构造的泛型类型。
        /// </summary>
        public TypeSpec UnconstructedType { get; }

        /// <summary>
        /// 获取类型实参。
        /// </summary>
        public TypeSpec[] Arguments { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{UnconstructedType}[{string.Join(",", Arguments.Select(a => $"[{a}]"))}]";
    }

    /// <summary>
    /// 表示命名类型，可以是尚未构造的泛型类型。
    /// </summary>
    public class NamedTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="NamedTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="containingType">包含此类型的外层类型。</param>
        /// <param name="name">类型名称。</param>
        /// <param name="arity">此类型的泛型参数个数，必须大于或等于其外层类型的泛型参数个数。</param>
        public NamedTypeSpec(NamedTypeSpec containingType, string name, int arity)
        {
            ContainingType = containingType;
            Name = name;
            if (containingType is NamedTypeSpec c && c.Arity > arity)
            {
                throw new ArgumentException("A named type cannot have an arity less than that of its containing type", nameof(arity));
            }

            if (arity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(arity), "A type cannot have a negative arity");
            }

            Arity = arity;
        }

        /// <summary>
        /// 获取此类型所需的泛型参数数量。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 获取类型名称；如果此类型不是嵌套类型，则名称包含命名空间。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 获取外层类型。
        /// </summary>
        public NamedTypeSpec? ContainingType { get; }

        /// <summary>
        /// 获取由命名空间限定的类型名称，对于嵌套类型还包含其外层类型。
        /// </summary>
        /// <returns>由命名空间限定的类型名称。</returns>
        public string GetNamespaceQualifiedName()
        {
            var builder = new StringBuilder();
            GetQualifiedNameInternal(this, builder);
            return builder.ToString();

            static void GetQualifiedNameInternal(NamedTypeSpec n, StringBuilder b)
            {
                if (n.ContainingType is not null)
                {
                    GetQualifiedNameInternal(n.ContainingType, b);
                    _ = b.Append('+');
                }

                _ = b.Append(n.Name);
            }
        }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => ContainingType is not null ? $"{ContainingType}+{Name}" : Name;
    }

    /// <summary>
    /// 表示由程序集限定的类型。
    /// </summary>
    public class AssemblyQualifiedTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="AssemblyQualifiedTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="assembly">程序集。</param>
        public AssemblyQualifiedTypeSpec(TypeSpec type, string? assembly)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (string.IsNullOrWhiteSpace(assembly))
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            Type = type;
            Assembly = assembly;
        }

        /// <summary>
        /// 获取程序集规范。
        /// </summary>
        public string? Assembly { get; }

        /// <summary>
        /// 获取限定类型。
        /// </summary>
        public TypeSpec Type { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => Assembly switch
        {
            { Length: > 0 } => $"{Type},{Assembly!}",
            _ => $"{Type}"
        };
    }

    /// <summary>
    /// 表示类型函数。
    /// </summary>
    public class TupleTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="TupleTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elements">元组元素。</param>
        /// <param name="arity">该类型接受的泛型类型参数数量。</param>
        public TupleTypeSpec(TypeSpec[] elements, int arity)
        {
            if (elements is null)
            {
                throw new ArgumentNullException(nameof(elements));
            }

            if (elements is { Length: 0 })
            {
                throw new ArgumentNullException(nameof(elements));
            }

            if (arity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(arity), "A type cannot have a negative arity");
            }

            Arity = arity;

            Elements = elements;
        }

        /// <summary>
        /// 获取此类型所需的泛型参数数量。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 获取元组元素。
        /// </summary>
        public TypeSpec[] Elements { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString()
        {
            var elements = string.Join(",", Elements.Select(element => element switch
            {
                LiteralTypeSpec => element.ToString(),
                _ => $"[{element}]"
            }));

            return Arity switch
            {
                > 0 => $"({elements})`{Arity}",
                _ => $"({elements})"
            };
        }
    }

    /// <summary>
    /// 表示字面量。
    /// </summary>
    public class LiteralTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="LiteralTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="value">值。</param>
        public LiteralTypeSpec(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 获取值。
        /// </summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"\"{Value}\"";
    }
}
