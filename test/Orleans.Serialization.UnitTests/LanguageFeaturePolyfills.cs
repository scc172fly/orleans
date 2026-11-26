#if !NET11_0_OR_GREATER
// C# 汇编者要求这些类型必须声明封闭的类型和工会。 它们从 .NET 11开始是基础类图书馆的一部分。
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class IsClosedTypeAttribute : Attribute
    {
        public Type[]? DerivedTypes { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class UnionAttribute : Attribute
    {
    }

    internal interface IUnion
    {
        object? Value { get; }
    }
}
#endif
