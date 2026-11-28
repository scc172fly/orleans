using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// 对 Grain 目录执行的递归分布式操作。
    /// 每个操作都可以将请求转发到远程所有者，并增加 hopCount。
    ///
    /// 这些方法可在远程调用（由 IRemoteGrainDirectory 扩展时），也可
    /// 在本地调用（由 ILocalGrainDirectory 扩展时）。
    /// </summary>
    internal interface IDhtGrainDirectory
    {
        /// <summary>
        /// 将新的 Grain 激活实例添加到目录中以记录它。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="address">新激活实例的地址。</param>
        /// <param name="hopCount">记录跨 Silo 的递归深度。</param>
        /// <returns>已注册的地址及与此目录映射关联的版本。</returns>
        Task<AddressAndTag> RegisterAsync(GrainAddress address, int hopCount = 0);

        /// <summary>
        /// 将新的 Grain 激活实例添加到目录中以记录它。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="address">新激活实例的地址。</param>
        /// <param name="currentRegistration">现有注册记录，可能为 null。</param>
        /// <param name="hopCount">记录跨 Silo 的递归深度。</param>
        /// <returns>已注册的地址及与此目录映射关联的版本。</returns>
        Task<AddressAndTag> RegisterAsync(GrainAddress address, GrainAddress? currentRegistration, int hopCount = 0);

        /// <summary>
        /// 从目录服务中移除现有激活实例的记录。
        /// 在删除激活实例时使用此方法。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="address">要移除的激活实例的地址。</param>
        /// <param name="hopCount">计算跨 Silo 的递归深度。</param>
        /// <param name="cause">注销的原因。</param>
        /// <returns>确认注销已完成。</returns>
        Task UnregisterAsync(GrainAddress address, UnregistrationCause cause, int hopCount = 0);

        /// <summary>
        /// 一次注销一批地址。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="addresses">要注销的地址。</param>
        /// <param name="hopCount">计算跨 Silo 的递归深度。</param>
        /// <param name="cause">注销的原因。</param>
        /// <returns>确认注销已完成。</returns>
        Task UnregisterManyAsync(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount = 0);

        /// <summary>
        /// 移除有关某个 Grain 的所有目录信息。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="grainId">Grain 的 ID。</param>
        /// <param name="hopCount">计算跨 Silo 的递归深度。</param>
        /// <returns>
        /// 确认删除已完成。
        /// </returns>
        Task DeleteGrainAsync(GrainId grainId, int hopCount = 0);

        /// <summary>
        /// 获取 Grain 的完整目录信息。
        /// 如果没有本地信息，则此方法将查询相应的远程目录节点。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="grainId">要查找的 Grain 的 ID。</param>
        /// <param name="hopCount">计算跨 Silo 的递归深度。</param>
        /// <returns>此 Grain 的所有已知激活实例的列表及 ETag。</returns>
        Task<AddressAndTag> LookupAsync(GrainId grainId, int hopCount = 0);
    }

    /// <summary>
    /// 表示 Grain 的地址及版本标记。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    internal readonly struct AddressAndTag
    {
        /// <summary>
        /// 地址。
        /// </summary>
        [Id(0)]
        public readonly GrainAddress? Address;

        /// <summary>
        /// 此条目的版本。
        /// </summary>
        [Id(1)]
        public readonly int VersionTag;

        public AddressAndTag(GrainAddress? address, int versionTag)
        {
            Address = address;
            VersionTag = versionTag;
        }
    }

    /// <summary>
    /// 指示从目录移除激活实例的原因。
    /// 此原因影响判断是否移除条目时所应用的条件。
    /// </summary>
    public enum UnregistrationCause : byte
    {
        /// <summary>
        /// 无条件强制移除目录条目。
        /// </summary>
        Force = 0,

        /// <summary>
        /// 仅当目录条目不是刚注册不久时才移除它（以避免与新注册产生竞态）。
        /// </summary>
        NonexistentActivation = 1,
    }
}
