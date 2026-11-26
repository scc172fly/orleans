using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orleans.Serialization.UnitTests
{
    [GenerateSerializer]
    abstract class Base<T>;

    class Derived : Base<int>;

    /// <summary>
    /// 测试普通基级的序列行为。
    /// 
    /// Orleans 要求可连号类型必须明确标有[GenerateSerializer]。
    /// 当基类为通用,并标记序列化时,则关闭的衍生类别
    /// 通用类型参数不自动继承序列化能力
    /// 除非也用标记标出[GenerateSerializer].
    /// 
    /// 此测试校验序列器正确识别哪些类型可以序列
    /// 依据明确的属性标识而不是继承。
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
