using Newtonsoft.Json;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    public interface IFooGrain : IGrain { }

    [GrainType("foo`1")]
    [StatelessWorker]
    public class FooGrain : Grain, IFooGrain { }

    /// <summary>
    /// Orleans Grain 引用功能测试。
    /// 验证 Grain 引用相等性、比较、序列化（包括 JSON），
    /// Grain 间传递 Grain 引用、处理 null 引用，
    /// 以及为分布式系统正确生成哈希码。
    /// </summary>
    [TestCategory("BVT"), TestCategory("GrainReference")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainReferenceTest : HostedTestClusterEnsureDefaultStarted
    {
        public GrainReferenceTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试 Grain 引用能否生成一致且分布均匀的哈希码。
        /// 验证哈希码生成具有确定性和稳定性，
        /// 这对于集群中一致的 Grain 放置和路由至关重要。
        /// </summary>
        [Fact]
        public void GrainReferenceComparison_ShouldProduceUniformHashCode()
        {
            var simpleGrain = this.GrainFactory.GetGrain<ISimpleGrain>(1234L, UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);
            var r = simpleGrain as GrainReference;
            Assert.NotNull(r);

            // 你好，陌生的朋友。测试在这里失败了吗？
            // 很可能是因为 GrainReference 的哈希码生成方式
            // 已经改变。如果你确定新代码能够产生可重复的结果，那么可以
            // 在此更新预期值。祝你好运，朋友。
            Assert.Equal(3643965955u, r.GetUniformHashCode());
        }

        /// <summary>
        /// 测试 Grain 引用相等性及比较运算符。
        /// 验证使用相等运算符、Equals 方法及不等检查时，
        /// 能够正确区分不同的 Grain 引用。
        /// </summary>
        [Fact]
        public void GrainReferenceComparison_DifferentReference()
        {
            ISimpleGrain ref1 = this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);
            ISimpleGrain ref2 = this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);
            Assert.True(ref1 != ref2);
            Assert.True(ref2 != ref1);
            Assert.False(ref1 == ref2);
            Assert.False(ref2 == ref1);
            Assert.False(ref1.Equals(ref2));
            Assert.False(ref2.Equals(ref1));
        }

        /// <summary>
        /// 测试将 Grain 自身的引用（this）传递给另一个 Grain。
        /// 验证 Grain 能将对自身的引用作为方法参数传递，
        /// 从而支持回调模式及 Grain 互连。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_this()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            await g1.PassThis(g2);
        }

        /// <summary>
        /// 测试在嵌套数据结构中传递 Grain 引用。
        /// 验证嵌入复杂对象中的 Grain 引用得到正确序列化，
        /// 并在 Grain 之间传递时保持其标识。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_this_Nested()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            await g1.PassThisNested(new ChainGrainHolder { Next = g2 });
        }

        /// <summary>
        /// 测试在 Grain 之间传递 null Grain 引用。
        /// 验证无论是直接作为参数传递，还是嵌入数据结构中，
        /// null 引用都能得到正确处理。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_Null()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            // g1 将向 g2 传递 null 引用
            await g1.PassNullNested(new ChainGrainHolder { Next = g2 });
            Assert.Null(await g2.GetNext());
            await g1.PassNull(g2);
            Assert.Null(await g2.GetNext());
        }

        /// <summary>
        /// 测试 Grain 引用的 JSON 序列化。
        /// 验证 Grain 引用可序列化为 JSON 并反序列化回来，
        /// 同时保持其标识和功能。
        /// </summary>
        [Fact, TestCategory("Serialization"), TestCategory("JSON")]
        public void GrainReference_Json_Serialization()
        {
            int id = Random.Shared.Next();
            TestGrainReferenceSerialization(id, true);
        }

        /// <summary>
        /// 测试复杂对象中的 Grain 引用的 JSON 序列化。
        /// 验证嵌套于数据结构的 Grain 引用能正确
        /// 序列化及反序列化，并在往返后仍然有效。
        /// </summary>
        [Fact, TestCategory("Serialization"), TestCategory("JSON")]
        public async Task GrainReference_Json_Serialization_Nested()
        {
            var settings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings(this.HostedCluster.Client!.ServiceProvider);

            var grain = HostedCluster.GrainFactory!.GetGrain<ISimpleGrain>(GetRandomGrainId());
            await grain.SetA(56820);
            var input = new GenericGrainReferenceHolder
            {
                Reference = (grain as GrainReference)!
            };

            var json = JsonConvert.SerializeObject(input, settings);
            var output = JsonConvert.DeserializeObject<GenericGrainReferenceHolder>(json, settings);

            Assert.Equal(input.Reference, output!.Reference);
            var reference = output.Reference;
            Assert.Equal(56820, await ((ISimpleGrain)reference).GetA());
        }

        [Serializable]
        [GenerateSerializer]
        public class GenericGrainReferenceHolder
        {
            [JsonProperty]
            [Id(0)]
            public GrainReference Reference { get; set; } = null!;
        }

        /// <summary>
        /// 测试尚未解析的 Grain 引用的 JSON 序列化。
        /// 验证 Grain 引用即使在解析（激活）之前也可以序列化，
        /// 从而保持延迟解析语义。
        /// </summary>
        [Fact, TestCategory("Serialization"), TestCategory("JSON")]
        public void GrainReference_Json_Serialization_Unresolved()
        {
            int id = Random.Shared.Next();
            TestGrainReferenceSerialization(id, false);
        }

        /// <summary>
        /// 测试系统 Grain 的引用驻留缓存。
        /// 验证针对同一 Grain 的多个请求返回同一引用对象（驻留复用），
        /// 从而优化内存使用。
        /// 注意：当前已跳过此测试，因为尚未实现 Grain 引用驻留缓存。
        /// </summary>
        [Fact(Skip = "GrainReference interning is not currently implemented."), TestCategory("Serialization"), TestCategory("Interner")]
        public void GrainReference_Interning_Sys_StoreGrain()
        {
            var g1 = (GrainReference)this.GrainFactory.GetGrain<IMemoryStorageGrain>(0);
            var g2 = (GrainReference)this.GrainFactory.GetGrain<IMemoryStorageGrain>(0);
            Assert.Equal(g1, g2); // 应为相等的 GrainReference。
            Assert.Same(g1, g2); // 应为同一个（经驻留复用的）GrainReference 对象

            // 通过序列化器进行往返转换。
            var g3 = this.HostedCluster.RoundTripSerializationForTesting(g1);
            Assert.Equal(g3, g1);
            Assert.Equal(g3, g2);
            Assert.Same(g3, g1);
            Assert.Same(g3, g2);
        }

        private void TestGrainReferenceSerialization(int id, bool resolveBeforeSerialize)
        {
            // 确保 Grain 引用能够通过 .NET 序列化器正确序列化。
            var grain = this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);

            if (resolveBeforeSerialize)
            {
                grain.SetA(id).Wait(); //  解析 Grain 引用（GR）
            }

            // 通过 JSON 序列化器执行序列化和反序列化
            var other = NewtonsoftJsonSerializeRoundtrip(grain);

            if (!resolveBeforeSerialize)
            {
                grain.SetA(id).Wait(); //  解析 Grain 引用（GR）
            }

            Assert.IsAssignableFrom(grain.GetType(), other);
            Assert.NotNull(other);
            Assert.Equal(grain, other);  // "Deserialized grain reference equality is preserved"
            int res = other.GetA().Result;
            Assert.Equal(id, res);  // "Returned values from call to deserialized grain reference"
        }

        private T? NewtonsoftJsonSerializeRoundtrip<T>(T obj)
        {
            var settings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings(this.HostedCluster.Client!.ServiceProvider);
            // http://james.newtonking.com/json/help/index.html?topic=html/T_Newtonsoft_Json_JsonConvert.htm
            string json = JsonConvert.SerializeObject(obj, settings);
            object? other = JsonConvert.DeserializeObject(json, typeof(T), settings);
            return (T?)other;
        }
    }
}
