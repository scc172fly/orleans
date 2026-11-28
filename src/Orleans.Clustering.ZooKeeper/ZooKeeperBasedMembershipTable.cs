using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using org.apache.zookeeper;
using org.apache.zookeeper.data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Host;
using Polly;

namespace Orleans.Runtime.Membership
{
    /// <summary>
    /// 使用 Apache ZooKeeper 3.4.6 的成员资格表实现，https://zookeeper.apache.org/doc/r3.4.6/
    /// </summary>
    /// <remarks>
    /// 所用 ZooKeeper 功能概述：数据表示为节点树（类似文件系统）。
    /// 每个节点由路径寻址，可以字节数组形式保存数据，并具有版本。创建节点时，
    /// 其版本为 0。更新时，版本会原子递增。更新也可依赖
    /// 预期的当前版本作为条件。一个事务可以包含多个操作，这些操作原子地全部成功或全部失败。
    /// 创建 ZooKeeper 客户端时，可以设置基础路径，所有操作均相对于此路径执行。
    /// 
    /// 在此实现中：
    /// 每个 Orleans 部署对应一个节点 /UniqueDeploymentId。
    /// 每个 Silo 的状态保存在 /UniqueDeploymentId/IP:Port@Gen。
    /// 每个 Silo 的 IAmAlive 保存在 /UniqueDeploymentId/IP:Port@Gen/IAmAlive。
    /// IAmAlive 保存在单独的节点，因此所有者的心跳写入会保留成员资格记录的版本。
    /// 成员资格更新原子修改记录节点和部署节点，并保留单独的心跳节点。
    /// 
    /// 节点的 ZooKeeper 版本即其 ETag：
    /// 表版本为 /UniqueDeploymentId 的版本。
    /// Silo 条目版本为 /UniqueDeploymentId/IP:Port@Gen 的版本。
    /// </remarks>
    public partial class ZooKeeperBasedMembershipTable : IMembershipTable
    {
        private readonly ILogger logger;

        internal const int ZOOKEEPER_SESSION_TIMEOUT = 10_000;
        internal const int MAX_CLEANUP_ROW_ATTEMPTS = 5;

        private readonly ZooKeeperWatcher watcher;
        private readonly Func<bool, ZooKeeperSession> _createSession;
        private readonly ResiliencePipeline _readRetryPipeline;
        private readonly Action<Task>? _observeOperation;

        /// <summary>
        /// 部署连接字符串，例如 "192.168.1.1,192.168.1.2/ClusterId"。
        /// </summary>
        private readonly string deploymentConnectionString;

        /// <summary>
        /// 此部署的节点名称，例如 /ClusterId。
        /// </summary>
        private readonly string clusterPath;

        /// <summary>
        /// 根连接字符串，例如 "192.168.1.1,192.168.1.2"。
        /// </summary>
        private readonly string rootConnectionString;

        /// <summary>
        /// 初始化 <see cref="ZooKeeperBasedMembershipTable"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="membershipTableOptions">ZooKeeper 集群成员管理选项。</param>
        /// <param name="clusterOptions">集群标识选项。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="logger"/>、<paramref name="membershipTableOptions"/> 或 <paramref name="clusterOptions"/> 为
        /// <see langword="null"/>.
        /// </exception>
        public ZooKeeperBasedMembershipTable(
            ILogger<ZooKeeperBasedMembershipTable> logger,
            IOptions<ZooKeeperClusteringSiloOptions> membershipTableOptions,
            IOptions<ClusterOptions> clusterOptions)
            : this(logger, membershipTableOptions, clusterOptions, null, null)
        {
        }

        internal ZooKeeperBasedMembershipTable(
            ILogger<ZooKeeperBasedMembershipTable> logger,
            IOptions<ZooKeeperClusteringSiloOptions> membershipTableOptions,
            IOptions<ClusterOptions> clusterOptions,
            Func<bool, ZooKeeperSession>? createSession,
            ResiliencePipeline? readRetryPipeline,
            Action<Task>? observeOperation = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(membershipTableOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);

            this.logger = logger;
            var options = membershipTableOptions.Value;
            watcher = new ZooKeeperWatcher(logger);
            this.clusterPath = "/" + clusterOptions.Value.ClusterId;
            rootConnectionString = options.ConnectionString;
            deploymentConnectionString = options.ConnectionString + this.clusterPath;
            _createSession = createSession ?? (readOnly => CreateSession(deploymentConnectionString, watcher, readOnly));
            _readRetryPipeline = readRetryPipeline ?? ZooKeeperReadRetryPolicy.CreatePipeline(logger, TimeProvider.System);
            _observeOperation = observeOperation;
        }

        /// <summary>
        /// 初始化基于 ZooKeeper 的成员资格表。
        /// </summary>
        /// <param name="tryInitPath">若设为 true，将尝试创建名为 "/ClusterId" 的节点。</param>
        /// <returns></returns>
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitPath) => InitializeMembershipTableAsync(tryInitPath, CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeMembershipTableAsync(bool tryInitPath, CancellationToken cancellationToken = default)
        {
            // 即使不是当前实例创建了此路径，
            // 也尝试在初始路径不存在时创建它，
            // 确保当前 Silo 开始运行前始终存在此路径。
            // 注意：当 ZooKeeper 连接字符串附加 /ClusterId 时，节点采用相对路径。
            await UsingZookeeper(rootConnectionString, async zk =>
            {
                try
                {
                    await zk.createAsync(this.clusterPath, null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
                    // 执行到此处表示刚刚创建了版本为 0 的部署路径。
                    LogInformationCreatedNewDeploymentPath(this.clusterPath);
                }
                catch (KeeperException.NodeExistsException)
                {
                    LogDebugDeploymentPathAlreadyExists(this.clusterPath);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 不支持点读取。读取完整快照，并从该快照选择所需记录。
        /// </summary>
        /// <param name="siloAddress">需要读取其成员资格信息的 Silo 地址。</param>
        /// <returns>表示不支持点读取的故障任务。</returns>
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRow(SiloAddress siloAddress) => ReadRowAsync(siloAddress, CancellationToken.None);

        /// <inheritdoc />
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        public Task<MembershipTableData> ReadRowAsync(SiloAddress siloAddress, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<MembershipTableData>(new NotSupportedException("Use ReadAllAsync and MembershipTableData.TryGet instead."));
        }

        /// <summary>
        /// 原子读取成员资格表的完整内容。
        /// 返回的 MembershipTableData 包含表中所有 Silo 的 MembershipEntry 条目及
        /// 此表的 TableVersion。MembershipEntry 与 TableVersion 必须以原子方式读取。
        /// </summary>
        /// <returns>指定表的成员资格信息：由多个 MembershipEntry 条目及
        /// TableVersion 组成的 MembershipTableData，所有数据均以原子方式读取。</returns>
        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 在由当前操作拥有的连接上顺序读取记录。
        /// 先读取每条成员资格记录，再读取其心跳。
        /// 表版本和子节点版本检查为整个快照提供隔离保护。规范成员资格数据发生并发
        /// 修改时，重新执行完整的顺序读取，直至版本屏障稳定或调用方取消
        /// 操作。
        /// 原生读取中的连接丢失故障最多在同一会话上重试四次。
        /// 每次重试前，操作等待该会话的下一次已连接事件，最长等待时间为会话超时时间。
        /// 当等待超时或会话进入终止状态时，继续执行重试并保留原生操作的结果。
        /// 调用方取消会停止提交更多请求，同时等待已准入请求及客户端关闭完成。
        /// </remarks>
        public Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            return ReadAsync(() => _createSession(true), _readRetryPipeline, null, cancellationToken);
        }

        internal static Task<MembershipTableData> ReadAsync(
            Func<ZooKeeperSession> createSession,
            ResiliencePipeline pipeline,
            SiloAddress? siloAddress,
            CancellationToken cancellationToken)
        {
            return ZooKeeperSession.ExecuteSessionAsync(createSession,
                session => ReadCoreAsync(
                    ZooKeeperReadRetryPolicy.Wrap(
                        session.Operations,
                        pipeline,
                        cancellationToken,
                        session.ConnectionMonitor),
                    siloAddress,
                    cancellationToken),
                cancellationToken);
        }

        internal static ZooKeeperSession CreateSession(string connectionString, ZooKeeperWatcher watcher, bool readOnly)
        {
            var sessionWatcher = watcher.CreateSessionWatcher();
            var client = new ZooKeeper(connectionString, ZOOKEEPER_SESSION_TIMEOUT, sessionWatcher, readOnly);
            return new ZooKeeperSession(
                new NativeOperations(path => client.getDataAsync(path), path => client.getChildrenAsync(path),
                    client.sync, operations => client.multiAsync(operations), client.setDataAsync),
                client.closeAsync,
                sessionWatcher);
        }

        internal static async Task<MembershipTableData> ReadCoreAsync(
            NativeOperations zk, SiloAddress? siloAddress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await zk.Sync("/");
            // 每次版本不匹配均表明本轮读取期间有正式成员资格更新提交。保留此会话的有序
            // 视图，使用调用方取消限定收敛过程，而不是拒绝有限的向前进展。
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stat before;
                IEnumerable<SiloAddress> addresses;
                if (siloAddress is null)
                {
                    var children = await zk.GetChildren("/");
                    before = children.Stat;
                    addresses = children.Children.Select(SiloAddress.FromParsableString);
                }
                else
                {
                    before = (await zk.GetData("/")).Stat;
                    addresses = [siloAddress];
                }

                var rows = new List<Tuple<MembershipEntry, string>>();
                KeeperException.NoNodeException? missingRow = null;
                foreach (var address in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (await GetRow(zk, address, siloAddress is not null, cancellationToken) is { } row)
                        {
                            rows.Add(row);
                        }
                    }
                    catch (KeeperException.NoNodeException exception)
                    {
                        missingRow = exception;
                        break;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var after = await zk.GetData("/");
                if (SameVersion(before, after.Stat))
                {
                    if (missingRow is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(missingRow).Throw();
                    }

                    return new MembershipTableData(rows, ConvertToTableVersion(after.Stat));
                }
            }
        }

        private static bool SameVersion(Stat before, Stat after) =>
            before.getVersion() == after.getVersion() && before.getCversion() == after.getCversion();

        /// <summary>
        /// 原子地尝试为一个 Silo 插入（添加）新的 MembershipEntry，并更新 TableVersion。
        /// 如果操作成功，则将对表进行以下更改：
        /// 1) 将新的 MembershipEntry 添加到表中。
        /// 2) 新增 MembershipEntry 还会附带新生成的唯一 ETag。
        /// 3) 表中的 TableVersion.Version 将更新为新的 TableVersion.Version。
        /// 4) 表中的 TableVersion ETag 将更新为自动生成的新的唯一 ETag。
        /// 表的全部变更（插入新记录、更新表版本及相关 ETag）应原子完成，或原子失败且不产生副作用。
        /// 操作应在以下每种条件下失败：
        /// 1) 表中已经存在指定 Silo 的 MembershipEntry。
        /// 2) 给定 TableVersion 的 ETag（由 TableVersion.VersionEtag 属性指定）与表中的 TableVersion ETag 不匹配，导致更新失败。
        /// </summary>
        /// <param name="entry">要插入的 MembershipEntry。</param>
        /// <param name="tableVersion">此表的新 TableVersion 及其 ETag。</param>
        /// <returns>插入操作成功时为 true，否则为 false。</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entry"/> 或 <paramref name="tableVersion"/> 为 <see langword="null"/>。
        /// </exception>
        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 条件事务仅提交一次。当提交结果未知时，连接丢失异常会向上传播，
        /// 保留调用方自行查明该结果的能力。
        /// </remarks>
        public Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(tableVersion);
            cancellationToken.ThrowIfCancellationRequested();

            return ZooKeeperSession.ExecuteAsync(() => _createSession(false),
                zk => InsertRowCoreAsync(zk, entry, tableVersion, cancellationToken), cancellationToken);
        }

        internal static async Task<bool> InsertRowCoreAsync(
            NativeOperations zk, MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rowPath = ConvertToRowPath(entry.SiloAddress);
            string rowIAmAlivePath = ConvertToRowIAmAlivePath(entry.SiloAddress);
            byte[] newRowData = Serialize(entry);
            byte[] newRowIAmAliveData = Serialize(entry.IAmAliveTime);

            int expectedTableVersion = int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture);

            try
            {
                await zk.Multi(
                [
                    Op.setData("/", null, expectedTableVersion),
                    Op.create(rowPath, newRowData, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT),
                    Op.create(rowIAmAlivePath, newRowIAmAliveData, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT)
                ]);
                return true;
            }
            catch (KeeperException e) when (e is KeeperException.NodeExistsException or KeeperException.BadVersionException)
            {
                return false;
            }
        }

        /// <summary>
        /// 原子地尝试更新一个 Silo 的 MembershipEntry，并更新 TableVersion。
        /// 如果操作成功，则将对表进行以下更改：
        /// 1) 将此 Silo 的 MembershipEntry 更新为新的 MembershipEntry（新条目完全替换旧条目）。
        /// 2) 更新后的 MembershipEntry 也会获得新生成的唯一 ETag。
        /// 3) 表中的 TableVersion.Version 将更新为新的 TableVersion.Version。
        /// 4) 表中的 TableVersion ETag 将更新为自动生成的新的唯一 ETag。
        /// 表的全部变更（更新记录、更新表版本及相关 ETag）应原子完成，或原子失败且不产生副作用。
        /// 操作应在以下每种条件下失败：
        /// 1) 表中不存在指定 Silo 的 MembershipEntry。
        /// 2) 表中存在指定 Silo 的 MembershipEntry，但其 ETag 与提供的 ETag 不匹配。
        /// 3) 给定 TableVersion 的 ETag（由 TableVersion.VersionEtag 属性指定）与表中的 TableVersion ETag 不匹配，导致更新失败。
        /// </summary>
        /// <param name="entry">要更新的 MembershipEntry。</param>
        /// <param name="etag">指定 MembershipEntry 的 ETag。</param>
        /// <param name="tableVersion">此表的新 TableVersion 及其 ETag。</param>
        /// <returns>更新操作成功时为 true，否则为 false。</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entry"/>、<paramref name="etag"/> 或 <paramref name="tableVersion"/> 为
        /// <see langword="null"/>.
        /// </exception>
        [Obsolete("Use UpdateRowAsync instead.")]
        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 条件事务仅提交一次。当提交结果未知时，连接丢失异常会向上传播，
        /// 保留调用方自行查明该结果的能力。
        /// </remarks>
        public Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(etag);
            ArgumentNullException.ThrowIfNull(tableVersion);
            cancellationToken.ThrowIfCancellationRequested();

            return ZooKeeperSession.ExecuteAsync(() => _createSession(false),
                zk => UpdateRowCoreAsync(zk, entry, etag, tableVersion, cancellationToken), cancellationToken);
        }

        internal static async Task<bool> UpdateRowCoreAsync(
            NativeOperations zk, MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rowPath = ConvertToRowPath(entry.SiloAddress);
            var newRowData = Serialize(entry);
            int expectedTableVersion = int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture);
            int expectedRowVersion = int.Parse(etag, CultureInfo.InvariantCulture);

            try
            {
                await zk.Multi(
                [
                    Op.setData("/", null, expectedTableVersion),
                    Op.setData(rowPath, newRowData, expectedRowVersion)
                ]);
                return true;
            }
            catch (KeeperException.BadVersionException)
            {
                return false;
            }
            catch (KeeperException.NoNodeException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await zk.GetData("/");
                return false;
            }
        }

        /// <summary>
        /// 通过一次无条件原生更新将所属 Silo 的 IAmAlive 时间戳写入其心跳节点。
        /// 成员资格记录及表版本保持不变。原生存储故障会传递给调用方。
        /// </summary>
        /// <param name="entry">包含心跳时间戳的所属 Silo 成员资格条目。</param>
        /// <returns>表示此操作成功执行的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

        /// <inheritdoc />
        public Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            cancellationToken.ThrowIfCancellationRequested();

            return UsingZookeeper(zk => UpdateIAmAliveCoreAsync(entry, zk.SetData, cancellationToken),
                this.deploymentConnectionString, this.watcher, cancellationToken);
        }

        internal static Task<Stat> UpdateIAmAliveCoreAsync(
            MembershipEntry entry,
            Func<string, byte[], int, Task<Stat>> setData,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return setData(ConvertToRowIAmAlivePath(entry.SiloAddress), Serialize(entry.IAmAliveTime), -1);
        }

        /// <summary>
        /// 删除指定 clusterId 的所有表记录。
        /// </summary>
        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        public Task DeleteMembershipTableEntries(string clusterId) => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

        /// <inheritdoc />
        public Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            string pathToDelete = "/" + clusterId;
            return UsingZookeeper(rootConnectionString, zk => DeleteRecursive(zk, pathToDelete, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 读取 /IP:Port@Gen 和 /IP:Port@Gen/IAmAlive 两个节点（共同构成一条记录）。
        /// </summary>
        /// <param name="zk">用于读取的 ZooKeeper 实例。</param>
        /// <param name="siloAddress">Silo 地址。</param>
        /// <param name="allowMissing">是否使用空的点读取结果表示缺失记录。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        private static async Task<Tuple<MembershipEntry, string>?> GetRow(NativeOperations zk, SiloAddress siloAddress, bool allowMissing, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rowPath = ConvertToRowPath(siloAddress);
            string rowIAmAlivePath = ConvertToRowIAmAlivePath(siloAddress);

            DataResult row;
            try
            {
                row = await zk.GetData(rowPath);
            }
            catch (KeeperException.NoNodeException) when (allowMissing)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var heartbeat = await zk.GetData(rowIAmAlivePath);
            MembershipEntry me = Deserialize<MembershipEntry>(row.Data);
            me.IAmAliveTime = Deserialize<DateTime>(heartbeat.Data);

            int rowVersion = row.Stat.getVersion();

            return new Tuple<MembershipEntry, string>(me, rowVersion.ToString(CultureInfo.InvariantCulture));
        }

        // 这些委托公开原生请求边界，以便对实际操作循环执行确定性测试。
        internal sealed class NativeOperations(
            Func<string, Task<DataResult>> getData,
            Func<string, Task<ChildrenResult>> getChildren,
            Func<string, Task> sync,
            Func<List<Op>, Task> multi,
            Func<string, byte[], int, Task<Stat>> setData)
        {
            internal Func<string, Task<DataResult>> GetData { get; } = getData;
            internal Func<string, Task<ChildrenResult>> GetChildren { get; } = getChildren;
            internal Func<string, Task> Sync { get; } = sync;
            internal Func<List<Op>, Task> Multi { get; } = multi;
            internal Func<string, byte[], int, Task<Stat>> SetData { get; } = setData;
        }

        private async Task<T> UsingZookeeper<T>(Func<NativeOperations, Task<T>> zkMethod, string deploymentConnectionString, ZooKeeperWatcher watcher, CancellationToken cancellationToken, bool canBeReadOnly = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = ZooKeeper.Using(deploymentConnectionString, ZOOKEEPER_SESSION_TIMEOUT, watcher, zk =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return zkMethod(new NativeOperations(
                    path => zk.getDataAsync(path), path => zk.getChildrenAsync(path), zk.sync,
                    operations => zk.multiAsync(operations), zk.setDataAsync));
            }, canBeReadOnly);

            return await AwaitOperationAsync(operation, cancellationToken, _observeOperation);
        }

        internal static async Task<T> AwaitOperationAsync<T>(
            Task<T> operation, CancellationToken cancellationToken, Action<Task>? observeOperation = null)
        {
            // ZooKeeperNetEx 不接受取消令牌。在挂起请求与
            // 异步释放完成前保持客户端存活；即使调用方停止等待，也要观察操作失败。
            observeOperation?.Invoke(operation);
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        private async Task UsingZookeeper(string connectString, Func<ZooKeeper, Task> zkMethod, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = ZooKeeper.Using(connectString, ZOOKEEPER_SESSION_TIMEOUT, watcher, zk =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return zkMethod(zk);
            });

            _observeOperation?.Invoke(operation);
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        private static async Task DeleteRecursive(ZooKeeper zk, string path, CancellationToken cancellationToken)
        {
            // 不同于 ZKUtil.deleteRecursiveAsync，此处在每次请求之间检查取消。
            var paths = new List<string> { path };
            for (var i = 0; i < paths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var children = await zk.getChildrenAsync(paths[i]);
                foreach (var child in children.Children)
                {
                    paths.Add(paths[i].TrimEnd('/') + "/" + child);
                }
            }

            for (var i = paths.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await zk.deleteAsync(paths[i], -1);
            }
        }

        private static string ConvertToRowPath(SiloAddress siloAddress)
        {
            return "/" + siloAddress.ToParsableString();
        }

        private static string ConvertToRowIAmAlivePath(SiloAddress siloAddress)
        {
            return ConvertToRowPath(siloAddress) + "/IAmAlive";
        }

        private static TableVersion ConvertToTableVersion(Stat stat)
        {
            int version = stat.getVersion();
            return new TableVersion(version, version.ToString(CultureInfo.InvariantCulture));
        }

        internal static byte[] Serialize(object obj)
        {
            return
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, Formatting.None,
                    MembershipSerializerSettings.Instance));
        }

        internal static T Deserialize<T>(byte[] data)
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data), MembershipSerializerSettings.Instance)!;
        }

        /// <inheritdoc />
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 按 ZooKeeper 返回的顺序依次评估各记录。如果某条记录的
        /// 条件删除发生冲突，最多进行五次尝试并重新评估该记录，然后
        /// 报告竞争。
        /// </remarks>
        public Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            return UsingZookeeper(zk => CleanupCoreAsync(zk, beforeDate, cancellationToken),
                this.deploymentConnectionString, this.watcher, cancellationToken);
        }

        internal static async Task<bool> CleanupCoreAsync(NativeOperations zk, DateTimeOffset beforeDate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var children = await zk.GetChildren("/");
            var cutoff = beforeDate.UtcDateTime;
            foreach (var child in children.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CleanupRowAsync(zk, "/" + child, cutoff, cancellationToken);
            }

            return true;
        }

        private static async Task CleanupRowAsync(NativeOperations zk, string rowPath, DateTime cutoff, CancellationToken cancellationToken)
        {
            var heartbeatPath = rowPath + "/IAmAlive";
            for (var attempt = 0; attempt < MAX_CLEANUP_ROW_ATTEMPTS; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DataResult row;
                try
                {
                    row = await zk.GetData(rowPath);
                }
                catch (KeeperException.NoNodeException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await zk.GetData("/");
                    return;
                }

                try
                {
                    var entry = Deserialize<MembershipEntry>(row.Data);
                    if (entry.Status != SiloStatus.Dead || entry.StartTime >= cutoff
                        || entry.SuspectTimes?.Any(vote => vote.Item2 >= cutoff) == true)
                    {
                        return;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var heartbeat = await zk.GetData(heartbeatPath);
                    if (Deserialize<DateTime>(heartbeat.Data) >= cutoff)
                    {
                        return;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await zk.Multi(
                    [
                        Op.delete(heartbeatPath, heartbeat.Stat.getVersion()),
                        Op.delete(rowPath, row.Stat.getVersion())
                    ]);
                    return;
                }
                catch (KeeperException.BadVersionException)
                {
                    // 仅重新评估此记录：记录版本及心跳版本用于保护删除资格。
                }
                catch (KeeperException.NoNodeException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await zk.GetData(rowPath);
                    }
                    catch (KeeperException.NoNodeException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await zk.GetData("/");
                        return;
                    }

                    // 原子退役会移除两个节点；仍存在的记录缺少心跳节点。
                    throw;
                }
            }

            throw new OrleansException(
                $"Unable to clean ZooKeeper membership row '{rowPath}' after {MAX_CLEANUP_ROW_ATTEMPTS} concurrent modifications.");
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Created new deployment path: {DeploymentPath}"
        )]
        private partial void LogInformationCreatedNewDeploymentPath(string deploymentPath);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Deployment path already exists: {DeploymentPath}"
        )]
        private partial void LogDebugDeploymentPathAlreadyExists(string deploymentPath);
    }

    /// <summary>
    /// 发布 ZooKeeper 连接状态转换，以便协调同一会话内的读取重试，
    /// 并记录 watcher 事件以用于诊断。
    /// </summary>
    internal partial class ZooKeeperWatcher : Watcher, IZooKeeperConnectionMonitor
    {
        private readonly ILogger logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _reconnectTimeout;
        private readonly object _connectionLock = new();
        private TaskCompletionSource _connectionChanged = NewConnectionChangedSource();
        private long _connectedGeneration;
        private bool _connected;
        private bool _terminal;

        public ZooKeeperWatcher(ILogger logger)
            : this(logger, TimeProvider.System, TimeSpan.FromMilliseconds(ZooKeeperBasedMembershipTable.ZOOKEEPER_SESSION_TIMEOUT))
        {
        }

        internal ZooKeeperWatcher(ILogger logger, TimeProvider timeProvider, TimeSpan reconnectTimeout)
        {
            this.logger = logger;
            _timeProvider = timeProvider;
            _reconnectTimeout = reconnectTimeout;
        }

        internal ZooKeeperWatcher CreateSessionWatcher() => new(logger, _timeProvider, _reconnectTimeout);

        public override Task process(WatchedEvent @event)
        {
            ProcessConnectionState(@event.getState());
            LogDebugWatchedEvent(@event);
            return Task.CompletedTask;
        }

        internal void ProcessConnectionState(Event.KeeperState state)
        {
            TaskCompletionSource? changed = null;
            lock (_connectionLock)
            {
                switch (state)
                {
                    case Event.KeeperState.SyncConnected:
                    case Event.KeeperState.ConnectedReadOnly:
                        _connected = true;
                        _connectedGeneration++;
                        changed = _connectionChanged;
                        _connectionChanged = NewConnectionChangedSource();
                        break;
                    case Event.KeeperState.AuthFailed:
                    case Event.KeeperState.Expired:
                        _connected = false;
                        _terminal = true;
                        changed = _connectionChanged;
                        _connectionChanged = NewConnectionChangedSource();
                        break;
                    case Event.KeeperState.Disconnected:
                        _connected = false;
                        changed = _connectionChanged;
                        _connectionChanged = NewConnectionChangedSource();
                        break;
                }
            }

            changed?.TrySetResult();
        }

        long IZooKeeperConnectionMonitor.CaptureAttemptGeneration()
        {
            lock (_connectionLock)
            {
                // 连接建立期间准入的请求在下一次连接成功时执行。
                return _connected ? _connectedGeneration : _connectedGeneration + 1;
            }
        }

        void IZooKeeperConnectionMonitor.ReportConnectionLoss(long connectedGeneration)
        {
            TaskCompletionSource? changed = null;
            lock (_connectionLock)
            {
                if (_connected && _connectedGeneration <= connectedGeneration)
                {
                    _connected = false;
                    changed = _connectionChanged;
                    _connectionChanged = NewConnectionChangedSource();
                }
            }

            changed?.TrySetResult();
        }

        async ValueTask<bool> IZooKeeperConnectionMonitor.WaitForConnectionAfterAsync(
            long connectedGeneration,
            CancellationToken cancellationToken)
        {
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = Task.Delay(_reconnectTimeout, _timeProvider, timeoutCancellation.Token);
            try
            {
                while (true)
                {
                    Task changed;
                    lock (_connectionLock)
                    {
                        if (_connected && _connectedGeneration > connectedGeneration)
                        {
                            return true;
                        }

                        if (_terminal)
                        {
                            return false;
                        }

                        changed = _connectionChanged.Task;
                    }

                    var completed = await Task.WhenAny(changed, timeout);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (completed == timeout)
                    {
                        return false;
                    }

                    await changed;
                }
            }
            finally
            {
                timeoutCancellation.Cancel();
            }
        }

        private static TaskCompletionSource NewConnectionChangedSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "{EventString}"
        )]
        private partial void LogDebugWatchedEvent(WatchedEvent eventString);
    }
}
