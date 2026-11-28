using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 负责探测远程 Silo 是否能够响应。
    /// </summary>
    internal interface IRemoteSiloProber
    {
        /// <summary>
        /// 探测指定 <paramref name="silo"/> 是否能够响应。
        /// </summary>
        /// <param name="silo">要探测的 Silo。</param>
        /// <param name="probeNumber">用于诊断的探测标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>
        /// 一个 <see cref="Task"/>，在探测成功返回时完成，在探测失败时进入故障状态。
        /// </returns>
        Task Probe(SiloAddress silo, int probeNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// 间接探测指定 <paramref name="target"/>，通过 <paramref name="intermediary"/> 执行。
        /// </summary>
        /// <param name="intermediary">将执行直接探测的 Silo。</param>
        /// <param name="target">将被探测的 Silo。</param>
        /// <param name="probeTimeout">超时时间，<paramref name="intermediary" /> 应将此超时时间用于探测。</param>
        /// <param name="probeNumber">用于诊断的探测编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task<IndirectProbeResponse> ProbeIndirectly(SiloAddress intermediary, SiloAddress target, TimeSpan probeTimeout, int probeNumber, CancellationToken cancellationToken = default);
    }
}
