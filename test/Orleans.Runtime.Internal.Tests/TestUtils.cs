using Orleans.Runtime;
using Orleans.TestingHost;

namespace UnitTests.TestHelper
{
    public static class TestUtils
    {
        /// <summary>从指定 Silo 获取详细的 Grain 报告。</summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="grainId">要请求其信息的 Grain ID。</param>
        /// <param name="siloHandle">应从自身缓存提供此信息的目标 Silo。</param>
        internal static Task<DetailedGrainReport> GetDetailedGrainReport(IInternalGrainFactory grainFactory, GrainId grainId, SiloHandle siloHandle)
        {
            // 此处使用 siloAddress，而非网关地址，因为目标 Silo 可能并非
            // 我们连接到其网关的那个 Silo。
            var siloControl = grainFactory.GetSystemTarget<ISiloControl>(Constants.SiloControlType, siloHandle.SiloAddress);
            return siloControl.GetDetailedGrainReport(grainId);
        }
    }
}
