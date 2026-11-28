#if !NET11_0_OR_GREATER
// C# 编译器需要这些类型来声明封闭类型和联合类型。从 .NET 11 起，它们属于基类库的一部分。
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
