using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 负责对远程 silos 进行响应性调查。
    /// </summary>
    internal interface IRemoteSiloProber
    {
        /// <summary>
        /// 检测到指定的反应 <paramref name="silo"/> 。
        /// </summary>
        /// <param name="silo">用于探测的 silo 。</param>
        /// <param name="probeNumber">用于诊断目的的探测识别符号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>
        /// 一种 <see cref="Task"/> ,当探测器成功返回时完成,而一旦探测器失败,故障就会发生。
        /// </returns>
        Task Probe(SiloAddress silo, int probeNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检测指定 <paramref name="target"/> 间接、通过 <paramref name="intermediary"/>.
        /// </summary>
        /// <param name="intermediary">下 silo 将进行直接探测</param>
        /// <param name="target">下 silo 将被考查的。</param>
        /// <param name="probeTimeout">超时 <paramref name="intermediary" /> 应用到探测器上</param>
        /// <param name="probeNumber">用于诊断目的的探测号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        Task<IndirectProbeResponse> ProbeIndirectly(SiloAddress intermediary, SiloAddress target, TimeSpan probeTimeout, int probeNumber, CancellationToken cancellationToken = default);
    }
}
