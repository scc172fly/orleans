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
    /// Orleans grain 参考功能测试。
    /// 验证 grain 参考平等、比较、序列化(包括 JSON),
    /// 通过 grain 引用之间 grains处理无效引用,
    /// 以及适当的散装系统散装代号生成。
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
        /// 测试 grain 参考文献是否产生一致的统一散列编码。
        /// 证实散列代码的生成是确定性和稳定的
        /// 这对于在组内连贯一致地 grain 安排和路线至关重要。
        /// </summary>
        [Fact]
        public void GrainReferenceComparison_ShouldProduceUniformHashCode()
        {
            var simpleGrain = this.GrainFactory.GetGrain<ISimpleGrain>(1234L, UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);
            var r = simpleGrain as GrainReference;
            Assert.NotNull(r);

            // 嘿,陌生人,所以测试失败了?
            // 可能是因为哈希代码的生成方式 GrainReference
            // 已经更改。 如果您确定新代码是可重复的, 那么可以
            // 更新这里的预期值。 祝你好运,朋友。
            Assert.Equal(3643965955u, r.GetUniformHashCode());
        }

        /// <summary>
        /// 测试 grain 参考平等和比较操作员。
        /// 验证不同的 grain 参考文献是否作了适当区分
        /// 利用平等操作者、平等方法和不平等检查。
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
        /// 测试通过 grain自己的引用(此)到另一个 grain。
        /// 证明 grains 能够将参考文献作为方法参数,
        /// 启用回调模式和 grain 互联。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_this()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            await g1.PassThis(g2);
        }

        /// <summary>
        /// 测试在嵌套数据结构中通过 grain 参考。
        /// 验证嵌入复杂对象的 grain 引用正确
        /// 在 grains之间通过时,他们的身份被序列并保持。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_this_Nested()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            await g1.PassThisNested(new ChainGrainHolder { Next = g2 });
        }

        /// <summary>
        /// 测试通过无效 grain 引用之间 grains.
        /// 证明无完全引用文献均直接得到妥善处理
        /// 参数通过时和嵌入数据结构时。
        /// </summary>
        [Fact]
        public async Task GrainReference_Pass_Null()
        {
            IChainedGrain g1 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());
            IChainedGrain g2 = this.GrainFactory.GetGrain<IChainedGrain>(GetRandomGrainId());

            // g1 将会通过g2的无效引用
            await g1.PassNullNested(new ChainGrainHolder { Next = g2 });
            Assert.Null(await g2.GetNext());
            await g1.PassNull(g2);
            Assert.Null(await g2.GetNext());
        }

        /// <summary>
        /// 测试 JSON 序列序列 grain 参考文献。
        /// 验证 grain 参考文献可序列化为 JSON 和解除
        /// 保持其特性和功能。
        /// </summary>
        [Fact, TestCategory("Serialization"), TestCategory("JSON")]
        public void GrainReference_Json_Serialization()
        {
            int id = Random.Shared.Next();
            TestGrainReferenceSerialization(id, true);
        }

        /// <summary>
        /// 测试 JSON 序列序列 grain 复数对象中的引用。
        /// 验证嵌套在数据结构中的 grain 参考文献结构是否适当
        /// 连续/分级,在往返后继续运作。
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
        /// 测试 JSON 未决的系列问题 grain 参考文献。
        /// 校验 grain 参考文献甚至在它们之前就可进行序列编号
        /// 解决(激活),保持懒惰的解析语义。
        /// </summary>
        [Fact, TestCategory("Serialization"), TestCategory("JSON")]
        public void GrainReference_Json_Serialization_Unresolved()
        {
            int id = Random.Shared.Next();
            TestGrainReferenceSerialization(id, false);
        }

        /// <summary>
        /// 测试 grain 系统内部参考查询 grains.
        /// 验证对同一 grain 的多次请求返回相同
        /// 参考对象(外派),优化内存使用。
        /// 注:由于 grain 参考内插未执行,目前跳过。
        /// </summary>
        [Fact(Skip = "GrainReference interning is not currently implemented."), TestCategory("Serialization"), TestCategory("Interner")]
        public void GrainReference_Interning_Sys_StoreGrain()
        {
            var g1 = (GrainReference)this.GrainFactory.GetGrain<IMemoryStorageGrain>(0);
            var g2 = (GrainReference)this.GrainFactory.GetGrain<IMemoryStorageGrain>(0);
            Assert.Equal(g1, g2); // 应该是相等 GrainReferences。
            Assert.Same(g1, g2); // 对象应该是相同的 / 被困 GrainReference 对象

            // 通过序列器的圆曲程
            var g3 = this.HostedCluster.RoundTripSerializationForTesting(g1);
            Assert.Equal(g3, g1);
            Assert.Equal(g3, g2);
            Assert.Same(g3, g1);
            Assert.Same(g3, g2);
        }

        private void TestGrainReferenceSerialization(int id, bool resolveBeforeSerialize)
        {
            // 确保 grain 参考文件通过 .NET 序列器进行完整序列化。
            var grain = this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), UnitTests.Grains.SimpleGrain.SimpleGrainNamePrefix);

            if (resolveBeforeSerialize)
            {
                grain.SetA(id).Wait(); //  决 定 决 决 决 国
            }

            // 通过 Json 连载器实现 + 的串行 + 降序
            var other = NewtonsoftJsonSerializeRoundtrip(grain);

            if (!resolveBeforeSerialize)
            {
                grain.SetA(id).Wait(); //  决 定 决 决 决 国
            }

            Assert.IsAssignableFrom(grain.GetType(), other);
            Assert.NotNull(other);
            Assert.Equal(grain, other);  // "序列 grain 参考平等得到维护"
            int res = other.GetA().Result;
            Assert.Equal(id, res);  // “从调用到解密的 grain 参考值返回数值”
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
