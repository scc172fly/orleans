using Orleans;

namespace Orleans.CodeGenerator.Tests.ReferencedTaggedUnions
{
    // SDK汇编了这些生产者模型;发电机测试消耗其元数据,使用每个试验目标引用的Roslyn版本。
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
