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
    /// 提供a grain 支持目录的 Azure 桌子储存。
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
        /// <param name="directoryOptions"> Azure 表格存储 grain 目录选项。</param>
        /// <param name="clusterOptions">组合组合选项。</param>
        /// <param name="loggerFactory">伐木厂</param>
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
        /// 查看一个 grain的登记激活。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <returns>登记 grain 地址,或 <see langword="null"/> 不存在登记。</returns>
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
        /// 登记a grain 如果没有注册的激活 grain.
        /// </summary>
        /// <param name="address">下 grain 注册地址。</param>
        /// <returns>注册在目录中的 grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
        public Task<GrainAddress?> Register(GrainAddress address) => Register(address, null);

        /// <summary>
        /// 登记一个 grain 激活,当指定以前的登记为当前时替换该登记。
        /// </summary>
        /// <param name="address">下 grain 注册地址。</param>
        /// <param name="previousAddress">替换先前的登记,或 <see langword="null"/> 需要空条目。</param>
        /// <returns>注册在目录中的 grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
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

            // 可能的种族状况?
            return result.isSuccess ? address : await Lookup(address.GrainId);
        }

        /// <summary>
        /// 当存储的激活标识符与所提供地址匹配时,删除一个 grain 激活。
        /// </summary>
        /// <param name="address">要删除的 grain 地址。</param>
        /// <returns>代表操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
        public async Task Unregister(GrainAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            var result = await this.tableDataManager.ReadSingleTableEntryAsync(this.clusterId, GrainDirectoryEntity.GrainIdToRowKey(address.GrainId));

            // 未找到条目
            if (result.Entity is null)
            {
                return;
            }

            // 检查存储条目是否与我们被要求删除的条目相符
            var entity = result.Item1;
            if (entity.ActivationId == address.ActivationId.ToParsableString())
                await this.tableDataManager.DeleteTableEntryAsync(GrainDirectoryEntity.FromGrainAddress(this.clusterId, address), entity.ETag.ToString());
        }

        /// <summary>
        /// 删除 grain 激活,如果存储的激活识别符与所提供的地址相符。
        /// </summary>
        /// <param name="addresses">下 grain 要删除的地址 。</param>
        /// <returns>代表操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="addresses"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="addresses"/> 包含 a 的 <see langword="null"/> 元素。</exception>
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
        /// 完成 silo 清除回调。 Azure 表条目通过激活专用的未登记操作清理。
        /// </summary>
        /// <param name="siloAddresses">与清理回调相关的 silo 地址。</param>
        /// <returns>一项已完成的任务。</returns>
        public Task UnregisterSilos(List<SiloAddress> siloAddresses)
        {
            // 使用 Azure 表格执行太昂贵
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
        /// 必要时初始化 Azure 表存储表。
        /// </summary>
        /// <param name="ct">取消给发的邮记 silo 生命周期。</param>
        /// <returns>一项代表初始化操作的任务。</returns>
        /// <remarks>使用这种方法的 silo 试验也可以使用。</remarks>
        public async Task InitializeIfNeeded(CancellationToken ct = default)
        {
            await this.tableDataManager.InitTableAsync(ct);
        }

        /// <summary>
        /// 将 grain 目录订阅到 silo 生命周期。
        /// </summary>
        /// <param name="lifecycle">下 silo 生命周期。</param>
        public void Participate(ISiloLifecycle lifecycle)
        {

            lifecycle.Subscribe(nameof(AzureTableGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, InitializeIfNeeded);
        }
    }
}
