using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.GrainDirectory.AzureStorage
{
    /// <summary>
    /// 提供以 Azure Table Storage 为后端的 Grain 目录。
    /// </summary>
    public class AzureTableGrainDirectory : IGrainDirectory, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly AzureTableDataManager<GrainDirectoryEntity> tableDataManager;
        private readonly string clusterId;

        internal class GrainDirectoryEntity : ITableEntity
        {
            public required string SiloAddress { get; set; }
            public required string ActivationId { get; set; }
            public required long MembershipVersion { get; set; }
            public required string PartitionKey { get; set; }
            public required string RowKey { get; set; }
            public DateTimeOffset? Timestamp { get; set; }
            public ETag ETag { get; set; }

            public GrainAddress ToGrainAddress()
            {
                return new GrainAddress
                {
                    GrainId = RowKeyToGrainId(this.RowKey),
                    SiloAddress = Runtime.SiloAddress.FromParsableString(this.SiloAddress),
                    ActivationId = Runtime.ActivationId.FromParsableString(this.ActivationId),
                    MembershipVersion = new MembershipVersion(this.MembershipVersion)
                };
            }

            public static GrainDirectoryEntity FromGrainAddress(string clusterId, GrainAddress address)
            {
                ArgumentNullException.ThrowIfNull(address.SiloAddress);

                return new GrainDirectoryEntity
                {
                    PartitionKey = clusterId,
                    RowKey = GrainIdToRowKey(address.GrainId),
                    SiloAddress = address.SiloAddress.ToParsableString(),
                    ActivationId = address.ActivationId.ToParsableString(),
                    MembershipVersion = address.MembershipVersion.Value,
                };
            }

            internal static string GrainIdToRowKey(GrainId grainId) => HttpUtility.UrlEncode(grainId.ToString(), Encoding.UTF8);

            internal static GrainId RowKeyToGrainId(string rowKey) => GrainId.Parse(HttpUtility.UrlDecode(rowKey, Encoding.UTF8));
        }

        /// <summary>
        /// 初始化 <see cref="AzureTableGrainDirectory"/> 类的新实例。
        /// </summary>
        /// <param name="directoryOptions">Azure Table Storage Grain 目录选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The Orleans dependency injection container supplies the cluster options instance.")]
        public AzureTableGrainDirectory(
            AzureTableGrainDirectoryOptions directoryOptions,
            IOptions<ClusterOptions> clusterOptions,
            ILoggerFactory loggerFactory)
        {
            this.tableDataManager = new AzureTableDataManager<GrainDirectoryEntity>(
                directoryOptions,
                loggerFactory.CreateLogger<AzureTableDataManager<GrainDirectoryEntity>>());
            this.clusterId = clusterOptions.Value.ClusterId;
        }

        /// <summary>
        /// 查找 Grain 已注册的激活实例。
        /// </summary>
        /// <param name="grainId">Grain 标识符。</param>
        /// <returns>已注册的 Grain 地址；如果没有注册，则为 <see langword="null"/>。</returns>
        public async Task<GrainAddress?> Lookup(GrainId grainId)
        {
            var result = await this.tableDataManager.ReadSingleTableEntryAsync(this.clusterId, GrainDirectoryEntity.GrainIdToRowKey(grainId));

            if (result.Entity is null)
            {
                return null;
            }

            return result.Item1.ToGrainAddress();
        }

        /// <summary>
        /// 如果该 Grain 不存在注册记录，则注册 Grain 激活实例。
        /// </summary>
        /// <param name="address">要注册的 Grain 地址。</param>
        /// <returns>在目录中注册的 Grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public Task<GrainAddress?> Register(GrainAddress address) => Register(address, null);

        /// <summary>
        /// 注册 Grain 激活实例；如果指定的旧注册仍是当前注册，则替换它。
        /// </summary>
        /// <param name="address">要注册的 Grain 地址。</param>
        /// <param name="previousAddress">要替换的先前注册；若要求条目为空，则为 <see langword="null"/>。</param>
        /// <returns>在目录中注册的 Grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public async Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress)
        {
            ArgumentNullException.ThrowIfNull(address);

            (bool isSuccess, string? eTag) result;
            if (previousAddress is not null)
            {
                var entry = GrainDirectoryEntity.FromGrainAddress(this.clusterId, address);
                var previousEntry = GrainDirectoryEntity.FromGrainAddress(this.clusterId, previousAddress);
                var (storedEntry, eTag) = await tableDataManager.ReadSingleTableEntryAsync(entry.PartitionKey, entry.RowKey);
                if (storedEntry is null)
                {
                    result = await this.tableDataManager.InsertTableEntryAsync(entry);
                }
                else if (storedEntry.ActivationId != previousEntry.ActivationId || storedEntry.SiloAddress != previousEntry.SiloAddress)
                {
                    return await Lookup(address.GrainId);
                }
                else
                {
                    _ = await tableDataManager.UpdateTableEntryAsync(entry, eTag!);
                    return address;
                }
            }
            else
            {
                var entry = GrainDirectoryEntity.FromGrainAddress(this.clusterId, address);
                result = await this.tableDataManager.InsertTableEntryAsync(entry);
            }

            // 可能存在竞态条件？
            return result.isSuccess ? address : await Lookup(address.GrainId);
        }

        /// <summary>
        /// 当存储的激活实例标识符与提供的地址匹配时，移除 Grain 激活实例。
        /// </summary>
        /// <param name="address">要移除的 Grain 地址。</param>
        /// <returns>表示该操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public async Task Unregister(GrainAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            var result = await this.tableDataManager.ReadSingleTableEntryAsync(this.clusterId, GrainDirectoryEntity.GrainIdToRowKey(address.GrainId));

            // 未找到记录。
            if (result.Entity is null)
            {
                return;
            }

            // 检查存储中的记录是否与要求删除的记录匹配。
            var entity = result.Item1;
            if (entity.ActivationId == address.ActivationId.ToParsableString())
                await this.tableDataManager.DeleteTableEntryAsync(GrainDirectoryEntity.FromGrainAddress(this.clusterId, address), entity.ETag.ToString());
        }

        /// <summary>
        /// 移除存储的激活实例标识符与提供的地址匹配的 Grain 激活实例。
        /// </summary>
        /// <param name="addresses">要移除的 Grain 地址。</param>
        /// <returns>表示该操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="addresses"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException"><paramref name="addresses"/> 包含 <see langword="null"/> 元素。</exception>
        public async Task UnregisterMany(List<GrainAddress> addresses)
        {
            ArgumentNullException.ThrowIfNull(addresses);

            foreach (var address in addresses)
            {
                if (address is null)
                {
                    throw new ArgumentException("The collection cannot contain null elements.", nameof(addresses));
                }
            }

            if (addresses.Count <= this.tableDataManager.StoragePolicyOptions.MaxBulkUpdateRows)
            {
                await UnregisterManyBlock(addresses);
            }
            else
            {
                var tasks = new List<Task>();
                foreach (var subList in addresses.BatchIEnumerable(this.tableDataManager.StoragePolicyOptions.MaxBulkUpdateRows))
                {
                    tasks.Add(UnregisterManyBlock(subList));
                }
                await Task.WhenAll(tasks);
            }
        }

        /// <summary>
        /// 完成 Silo 清理回调。Azure Table 记录通过针对具体激活实例的取消注册操作进行清理。
        /// </summary>
        /// <param name="siloAddresses">与清理回调关联的 Silo 地址。</param>
        /// <returns>已完成的任务。</returns>
        public Task UnregisterSilos(List<SiloAddress> siloAddresses)
        {
            // 使用 Azure Table 实现此操作的成本太高。
            return Task.CompletedTask;
        }

        private async Task UnregisterManyBlock(List<GrainAddress> addresses)
        {
            var queryBuilder = new StringBuilder();
            queryBuilder.Append(TableClient.CreateQueryFilter($"(PartitionKey eq {clusterId}) and ("));
            var first = true;
            foreach (var addr in addresses)
            {
                if (!first)
                {
                    queryBuilder.Append(" or ");
                }
                else
                {
                    first = false;
                }

                var rowKey = GrainDirectoryEntity.GrainIdToRowKey(addr.GrainId);
                var queryClause = TableClient.CreateQueryFilter($"((RowKey eq {rowKey}) and (ActivationId eq {addr.ActivationId.ToString()}))");
                queryBuilder.Append(queryClause);
            }

            queryBuilder.Append(')');
            var entities = await this.tableDataManager.ReadTableEntriesAndEtagsAsync(queryBuilder.ToString());
            await this.tableDataManager.DeleteTableEntriesAsync(entities);
        }

        /// <summary>
        /// 在需要时初始化 Azure Table Storage 表。
        /// </summary>
        /// <param name="ct">Silo 生命周期提供的取消令牌。</param>
        /// <returns>表示初始化操作的任务。</returns>
        /// <remarks>此方法由 Silo 生命周期调用，也可用于测试。</remarks>
        public async Task InitializeIfNeeded(CancellationToken ct = default)
        {
            await this.tableDataManager.InitTableAsync(ct);
        }

        /// <summary>
        /// 让 Grain 目录订阅 Silo 生命周期。
        /// </summary>
        /// <param name="lifecycle">Silo 生命周期。</param>
        public void Participate(ISiloLifecycle lifecycle)
        {

            lifecycle.Subscribe(nameof(AzureTableGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, InitializeIfNeeded);
        }
    }
}
