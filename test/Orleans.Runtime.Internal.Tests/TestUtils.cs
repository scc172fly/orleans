using Orleans.Runtime;
using Orleans.TestingHost;

namespace UnitTests.TestHelper
{
    public static class TestUtils
    {
        /// <summary>详细分析 grain 指定报告来自 silo</summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="grainId">我们要求 grain 提供信息的</param>
        /// <param name="siloHandle">目标 silo 应该提供其缓存中的信息</param>
        internal static Task<DetailedGrainReport> GetDetailedGrainReport(IInternalGrainFactory grainFactory, GrainId grainId, SiloHandle siloHandle)
        {
            // 使用此处的 siloAddress ,而不是网关地址,因为我们可能针对的是我们没有选择的 silo 
            // 连接到网关
            var siloControl = grainFactory.GetSystemTarget<ISiloControl>(Constants.SiloControlType, siloHandle.SiloAddress);
            return siloControl.GetDetailedGrainReport(grainId);
        }
    }
}
