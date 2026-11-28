using System.Collections.Immutable;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class RoundtripSerializationGrain : Grain, IRoundtripSerializationGrain
    {
        public Task<CampaignEnemyTestType> GetEnemyType()
        {
            return Task.FromResult(CampaignEnemyTestType.Enemy2);
        }

        public Task<object> GetClosedGenericValue()
        {
            // 使用不太可能预注册的封闭泛型类型
            var result = new List<ImmutableList<HashSet<Tuple<int, string>>>>();
            return Task.FromResult((object)result);
        }

        // 测试记录类型支持
        public Task<RetVal> GetRetValForParamVal(ParamVal param) => Task.FromResult(new RetVal(param.Value));
    }
}
