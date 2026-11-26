using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// grain 目录中递归的分布操作 。
    /// 每项作业均可将请求转交远程所有人,增加 hopCount。
    ///
    /// 这里的方法可以远程命名(如果扩展为 IRemoteGrainDirectory)或
    /// 当地(以 ILocalGrainDirectory延长)
    /// </summary>
    internal interface IDhtGrainDirectory
    {
        /// <summary>
        /// 将新 grain 激活添加到目录中, 记录它。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="address">新激活的地址。</param>
        /// <param name="hopCount">计数递回深度 silos</param>
        /// <returns>注册地址和与该目录映射相关的版本。</returns>
        Task<AddressAndTag> RegisterAsync(GrainAddress address, int hopCount = 0);

        /// <summary>
        /// 将新 grain 激活添加到目录中, 记录它。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="address">新激活的地址。</param>
        /// <param name="currentRegistration">现有的登记,可能无效。</param>
        /// <param name="hopCount">计数递回深度 silos</param>
        /// <returns>注册地址和与该目录映射相关的版本。</returns>
        Task<AddressAndTag> RegisterAsync(GrainAddress address, GrainAddress? currentRegistration, int hopCount = 0);

        /// <summary>
        /// 从目录服务中删除现有激活的记录。
        /// 当激活被删除时使用此选项 。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="address">激活要删除的地址。</param>
        /// <param name="hopCount">计数在 silos之间重复的深度。</param>
        /// <param name="cause">取消登记的原因。</param>
        /// <returns>确认已完成注销登记。</returns>
        Task UnregisterAsync(GrainAddress address, UnregistrationCause cause, int hopCount = 0);

        /// <summary>
        /// 立即解登记一批地址
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="addresses">解除登记地址。</param>
        /// <param name="hopCount">计数在 silos之间重复的深度。</param>
        /// <param name="cause">取消登记的原因。</param>
        /// <returns>确认未登记工作已经完成。</returns>
        Task UnregisterManyAsync(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount = 0);

        /// <summary>
        /// 删除有关 grain的全部目录信息。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="grainId">身份识别 grain.</param>
        /// <param name="hopCount">计数在 silos之间重复的深度。</param>
        /// <returns>
        /// 确认删除已经完成。
        /// </returns>
        Task DeleteGrainAsync(GrainId grainId, int hopCount = 0);

        /// <summary>
        /// 获取 grain的完整目录信息。
        /// 如果没有本地信息, 则此方法会查询合适的远程目录节点 。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="grainId">要查找的 grain 代号。</param>
        /// <param name="hopCount">计数在 silos之间重复的深度。</param>
        /// <returns> grain和电子标签所有已知激活的列表。</returns>
        Task<AddressAndTag> LookupAsync(GrainId grainId, int hopCount = 0);
    }

    /// <summary>
    /// 表示 grain 的地址和版本标记。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    internal readonly struct AddressAndTag
    {
        /// <summary>
        /// 地址 地址 地址
        /// </summary>
        [Id(0)]
        public readonly GrainAddress? Address;

        /// <summary>
        /// 本条目的版本 。
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
    /// 表示从目录中删除激活的原因。
    /// 这影响到在决定是否删除条目时适用的条件。
    /// </summary>
    public enum UnregistrationCause : byte
    {
        /// <summary>
        /// 强行删除目录条目, 不带任何条件
        /// </summary>
        Force = 0,

        /// <summary>
        /// 仅在目录条目不新鲜时删除该条目(以避免在新注册上进行竞赛)
        /// </summary>
        NonexistentActivation = 1,
    }
}
