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
        /// 停止目录服务的本地部分 。
        /// </summary>
        Task StopAsync();

        RemoteGrainDirectory RemoteGrainDirectory { get; }
        RemoteGrainDirectory CacheValidator { get; }

        /// <summary>
        /// 从目录服务中删除非已激活的记录。
        /// 当收到无法找到的激活请求时, 使用此选项, 
        /// 来懒惰地清理远程目录。
        /// 时间戳用于防止在可能(但不太可能)中删除有效条目
        /// 种族:在提出新的启动请求之前,收到要求重新激活
        /// 新建激活要创建 。
        /// 请注意,如果全球配置参数为全局配置参数,此方法为无操作值 DirectoryLazyDeregistrationDelay
        /// 是零或负 TimeSpan。
        /// <para>此方法必须从排程器线索中调用 。</para>
        /// </summary>
        /// <param name="address">激活要删除的地址。</param>
        /// <param name="origin"> 发送给不存在的激活电文的 silo</param>
        Task UnregisterAfterNonexistingActivation(GrainAddress address, SiloAddress origin);

        /// <summary>
        /// 无效指定激活地址的缓存条目 。
        /// 每当目录客户端试图访问该目录时,该方法将被调用 
        /// 上一个目录查找返回的激活, 从目标中获取拒绝 silo 
        /// 通知他启动并不存在
        /// </summary>
        /// <param name="activation">激活地址, 需要将该地址在给定 grain的目录缓存中失效。</param>
        void InvalidateCacheEntry(GrainAddress activation);

        /// <summary>
        /// 无效的给定 grain的缓存条目 。
        /// </summary>
        void InvalidateCacheEntry(GrainId grainId);

        /// <summary>
        /// 添加或更新给定激活地址的缓存条目 。
        /// 每当作出安置决定时,即采用这种方法。
        /// </summary>
        void AddOrUpdateCacheEntry(GrainId grainId, SiloAddress siloAddress);

        /// <summary>
        /// 仅用于测试。
        /// 返回 silo ,该 silo 认为它是目录信息的主要所有者
        /// 提供 grain ID。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        SiloAddress? GetPrimaryForGrain(GrainId grain);

        /// <summary>
        /// 返回在本地目录分区中保存的目录信息,以用于提供 grain ID。
        /// 如果没有信息, 则结果为无效 。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        AddressAndTag GetLocalDirectoryData(GrainId grain);

        /// <summary>
        /// 试图寻找 grain 在此所在的地址 silo本地缓存或权威本地目录分区。
        /// </summary>
        /// <param name="grainId">下 grain 需要找到的ID。</param>
        /// <param name="address">由此结果 grain 地址, 如果找到的话, 或者 <see langword="null"/> 找不到的话。</param>
        /// <returns>值,表示是否找到有效条目。</returns>
        bool TryLocalLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);

        /// <summary>
        /// 仅用于测试和排除故障。
        /// 返回在本地目录缓存中保存的目录信息,以用于提供 grain ID。
        /// 如果没有信息, 则结果为无效 。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        GrainAddress? GetLocalCacheData(GrainId grain);

        /// <summary>
        /// 试图在目录缓存中查找指定的 grain 。
        /// </summary>
        bool TryCachedLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);
    }
}
