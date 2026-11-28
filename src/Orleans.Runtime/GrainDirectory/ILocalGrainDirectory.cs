using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.GrainDirectory;

namespace Orleans.Runtime.GrainDirectory
{
    internal interface ILocalGrainDirectory : IDhtGrainDirectory
    {
        /// <summary>
        /// 启动目录服务的本地部分。
        /// </summary>
        void Start();

        /// <summary>
        /// 停止目录服务的本地部分。
        /// </summary>
        Task StopAsync();

        RemoteGrainDirectory RemoteGrainDirectory { get; }
        RemoteGrainDirectory CacheValidator { get; }

        /// <summary>
        /// 从目录服务中移除不存在的激活实例的记录。
        /// 当收到发往无法找到的激活实例的请求时，使用此方法
        /// 延迟清理远程目录。
        /// 时间戳用于避免在一种可能发生（但概率很低）的竞态中移除有效条目：
        /// 发往新激活实例的请求，先于促使该激活实例
        /// 被创建的请求到达。
        /// 注意：如果全局配置参数 DirectoryLazyDeregistrationDelay
        /// 为零或负的 TimeSpan，则此方法不执行任何操作。
        /// <para>必须从调度器线程调用此方法。</para>
        /// </summary>
        /// <param name="address">要移除的激活实例地址。</param>
        /// <param name="origin">向不存在的激活实例发送消息的 Silo。</param>
        Task UnregisterAfterNonexistingActivation(GrainAddress address, SiloAddress origin);

        /// <summary>
        /// 使给定激活实例地址的缓存条目失效。
        /// 当目录客户端尝试访问
        /// 上次目录查找所返回的激活实例，并收到目标 Silo 的拒绝响应，
        /// 得知该激活实例不存在时，应调用此方法。
        /// </summary>
        /// <param name="activation">给定 Grain 的目录缓存中需要失效的激活实例地址。</param>
        void InvalidateCacheEntry(GrainAddress activation);

        /// <summary>
        /// 使给定 Grain 的缓存条目失效。
        /// </summary>
        void InvalidateCacheEntry(GrainId grainId);

        /// <summary>
        /// 添加或更新给定激活实例地址的缓存条目。
        /// 每次作出放置决策时都应调用此方法。
        /// </summary>
        void AddOrUpdateCacheEntry(GrainId grainId, SiloAddress siloAddress);

        /// <summary>
        /// 仅用于测试。
        /// 返回当前 Silo 认为是给定 Grain ID 的目录信息
        /// 主要所有者的 Silo。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        SiloAddress? GetPrimaryForGrain(GrainId grain);

        /// <summary>
        /// 返回本地目录分区中保存的指定 Grain ID 的目录信息。
        /// 若未保存相关信息，则结果为 null。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        AddressAndTag GetLocalDirectoryData(GrainId grain);

        /// <summary>
        /// 尝试在此 Silo 的本地缓存或权威本地目录分区中查找 Grain 地址。
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID。</param>
        /// <param name="address">若找到，则为相应 Grain 地址，否则为 <see langword="null"/>。</param>
        /// <returns>指示是否找到有效条目的值。</returns>
        bool TryLocalLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);

        /// <summary>
        /// 仅用于测试和故障排查。
        /// 返回本地目录缓存中保存的指定 Grain ID 的目录信息。
        /// 若未保存相关信息，则结果为 null。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        GrainAddress? GetLocalCacheData(GrainId grain);

        /// <summary>
        /// 尝试在目录缓存中查找指定的 Grain。
        /// </summary>
        bool TryCachedLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);
    }
}
