
using System;
using System.Linq;
using System.Text;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 代表类型。
    /// </summary>
    public abstract class TypeSpec
    {
        /// <summary>
        /// 以 <see cref="RuntimeTypeNameParser"/>能够解析的方式对本实例进行格式化。
        /// </summary>
        public abstract string Format();
    }

    /// <summary>
    /// 表示指针类型(*) 。
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
        /// 获取元素类型 。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}*";
    }

    /// <summary>
    /// 表示引用类型(&amp;) 。
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
        /// 获取元素类型 。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}&";
    }

    /// <summary>
    /// 代表数组类型。
    /// </summary>
    public class ArrayTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="ArrayTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elementType">数组元素类型。</param>
        /// <param name="dimensions">数组的尺寸数量。</param>
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
        /// 获得数组维数。
        /// </summary>
        public int Dimensions { get; }

        /// <summary>
        /// 获取元素类型 。
        /// </summary>
        public TypeSpec ElementType { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{ElementType}[{new string(',', Dimensions - 1)}]";
    }

    /// <summary>
    /// 表示已建通用型。
    /// </summary>
    public class ConstructedGenericTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="ConstructedGenericTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="unconstructedType">未构建的类型。</param>
        /// <param name="arity">类型参数的预期数量。</param>
        /// <param name="arguments">通用类型参数。</param>
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
        /// 获得非构造型号。
        /// </summary>
        public TypeSpec UnconstructedType { get; }

        /// <summary>
        /// 获取类型参数。
        /// </summary>
        public TypeSpec[] Arguments { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"{UnconstructedType}[{string.Join(",", Arguments.Select(a => $"[{a}]"))}]";
    }

    /// <summary>
    /// 指一个命名类型,该类型可能是未制造的通用类型。
    /// </summary>
    public class NamedTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="NamedTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="containingType">包含类型。</param>
        /// <param name="name">类型名称。</param>
        /// <param name="arity">类型的一般性,必须大于或等于含有型的一般性。</param>
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
        /// 获取该类型所需的通用参数数。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 获取类型名称,如果不是嵌套类型,则包含命名空间。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 获得包含型号。
        /// </summary>
        public NamedTypeSpec? ContainingType { get; }

        /// <summary>
        /// 获取名称为空间合格型号的名称,包括包含类型(用于嵌套类型)。
        /// </summary>
        /// <returns>命名空间合格类型名称。</returns>
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
    /// 代表符合资格的组装类型。
    /// </summary>
    public class AssemblyQualifiedTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="AssemblyQualifiedTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="assembly">组装。</param>
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
        /// 获得组装规格
        /// </summary>
        public string? Assembly { get; }

        /// <summary>
        /// 获得合格的类型。
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
    /// 代表类型函数。
    /// </summary>
    public class TupleTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="TupleTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="elements">等量元素。</param>
        /// <param name="arity">类型所接受的通用类型参数数。</param>
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
        /// 获取该类型所需的通用参数数。
        /// </summary>
        public int Arity { get; }

        /// <summary>
        /// 得到图普元素。
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
    /// 代表字面意思。
    /// </summary>
    public class LiteralTypeSpec : TypeSpec
    {
        /// <summary>
        /// 初始化 <see cref="LiteralTypeSpec"/> 类的新实例。
        /// </summary>
        /// <param name="value">数值。</param>
        public LiteralTypeSpec(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            Value = value;
        }

        /// <summary>
        /// 得到价值。
        /// </summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override string Format() => ToString();

        /// <inheritdoc/>
        public override string ToString() => $"\"{Value}\"";
    }
}
