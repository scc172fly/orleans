using System.Threading.Tasks;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.SerializerTests
{
    /// <summary>
    /// 测试多种数据类型的序列化往返，包括记录、枚举和继承的泛型。
    /// </summary>
    [TestCategory("Serialization"), TestCategory("BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Serialization")]
    public class RoundTripSerializerTests : HostedTestClusterEnsureDefaultStarted
    {
        public RoundTripSerializerTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Serialize_TestMethodResultRecord()
        {
            var grain = this.GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            RetVal retVal = await grain.GetRetValForParamVal(new ParamVal(42));
            Assert.Equal(42, retVal.Value);
        }

        [Fact]
        public async Task Serialize_TestMethodResultEnum()
        {
            var grain = this.GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            CampaignEnemyTestType result = await grain.GetEnemyType();
            Assert.Equal(CampaignEnemyTestType.Enemy2, result); //枚举返回值未正确传输
        }

        [Fact]
        public async Task Serialize_TestMethodResultWithInheritedClosedGeneric()
        {
            var grain = this.GrainFactory.GetGrain<IRoundtripSerializationGrain>(GetRandomGrainId());
            object result = await grain.GetClosedGenericValue();
            Assert.NotNull(result);
        }
    }
}
