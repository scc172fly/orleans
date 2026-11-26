using System.Threading.Tasks;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.SerializerTests
{
    /// <summary>
    /// 测试各种数据类型的序列序列环程,包括记录、图案和继承的非专利物。
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
            Assert.Equal(CampaignEnemyTestType.Enemy2, result); //Enum 返回值没有正确传输
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
