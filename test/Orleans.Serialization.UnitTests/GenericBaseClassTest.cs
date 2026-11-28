using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orleans.Serialization.UnitTests
{
    [GenerateSerializer]
    abstract class Base<T>;

    class Derived : Base<int>;

    /// <summary>
    /// 具有泛型基类的类型的序列化行为测试。
    /// 
    /// Orleans 要求使用 [GenerateSerializer] 显式标记可序列化类型。
    /// 当泛型基类已标记为可序列化时，将其泛型类型参数具体化的派生类
    /// 不会自动继承序列化能力，
    /// 除非这些派生类本身也带有 [GenerateSerializer] 标记。
    /// 
    /// 此测试验证序列化器能够正确识别哪些类型可以序列化，
    /// 依据的是显式特性标记而非继承关系。
    /// </summary>
    [Trait("Category", "BVT")]
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    public class GenericBaseClassTest
    {
        private readonly ServiceProvider _services;
        private readonly Serializer _serializer;
        public GenericBaseClassTest()
        {
            _services = new ServiceCollection()
                .AddSerializer()
                .BuildServiceProvider();
            _serializer = _services.GetRequiredService<Serializer>();
        }

        [Fact]
        public void DerivedNoGeneric()
        {
            Assert.False(_serializer.CanSerialize(typeof(Derived)));
        }
    }
}
