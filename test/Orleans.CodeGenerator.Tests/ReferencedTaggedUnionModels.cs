using Orleans;

namespace Orleans.CodeGenerator.Tests.ReferencedTaggedUnions
{
    // SDK 编译这些生产者模型；生成器测试使用各测试目标引用的 Roslyn 版本读取其元数据。
    [GenerateSerializer]
    public union ReferencedShallowUnion(int, string);

    [GenerateSerializer]
    public union ReferencedDeepUnion(List<int>, int);

    [Immutable, GenerateSerializer]
    public union ReferencedImmutableUnion(List<int>);

    public interface IReferencedUnionHolder
    {
        ReferencedShallowUnion Shallow { get; set; }
        ReferencedDeepUnion Deep { get; set; }
        ReferencedDeepUnion Alias { get; set; }
        ReferencedImmutableUnion Immutable { get; set; }
    }
}

#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class UnionAttribute : Attribute { }

    internal interface IUnion
    {
        object? Value { get; }
    }
}
#endif
