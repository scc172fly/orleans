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
    /// A 成员表使用阿帕奇动物园管理员3.4.6 https://zookeeper.apache.org/doc/r3.4.6/的执行情况
    /// </summary>
    /// <remarks>
    /// 简要概述所使用的 ZK 特征:数据由一棵节点树(类似于文件系统)代表。 
    /// 每个节点都由路径处理,并且可以将数据作为字节数组持有并有一个版本。当节点创建时, 
    /// 更新后,该版本将原子递增。 
    /// 交易可以维持几个操作, 无论是从原子上成功还是失败 。
    /// 当创建动物园管理员客户端时, 就可以设置一个所有操作都相对的基线路径 。
    /// 
    /// 在实施过程中:
    /// 每次 Orleans 部署都有节点/UniqueDeploymentId
    /// 每个 Silo状态都在 /UniqueDeploymentId/IP: Port@ Gen 中保存
    /// 每一 Silo's IAmAlive 保存在 /UniqueDeploymentId/IP:Port@Gen/IAmAlive
    /// IAmAlive 被保存在一个单独的节点中, 所以所有者心跳写着保存会籍行的版本。
    /// 成员身份从原子上更新了对行和部署节点的修改,保留了单独的心跳节点。
    /// 
    /// 节点的 ZK 版本是它的ETag:
    /// 表格版本是 /UniqueDeploymentId的版本
    /// silo 条目版本是 /UniqueDeploymentId/IP:Port@Gen的版本
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
        /// “192.168.1.1、192.168.1.2/ClusterId”等的部署连接字符串。
        /// </summary>
        private readonly string deploymentConnectionString;

        /// <summary>
        /// 此部署的节点名称。 例如 /ClusterId
        /// </summary>
        private readonly string clusterPath;

        /// <summary>
        /// 根连接字符串。 例如“192.168.1.1, 192.168.81.2”
        /// </summary>
        private readonly string rootConnectionString;

        /// <summary>
        /// 初始化 <see cref="ZooKeeperBasedMembershipTable"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志器。</param>
        /// <param name="membershipTableOptions">下 ZooKeeper 组合组合选项。</param>
        /// <param name="clusterOptions">集群识别选项。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="logger"/>、 <paramref name="membershipTableOptions"/>或 <paramref name="clusterOptions"/> 是
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
        /// 初始化基于 ZooKeeper 的会籍表。
        /// </summary>
        /// <param name="tryInitPath">如果设定为真实, 我们将尝试创建一个名为 " /ClusterId"</param>的节点
        /// <returns></returns>
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitPath) => InitializeMembershipTableAsync(tryInitPath, CancellationToken.None);

        /// <inheritdoc />
        public async Task InitializeMembershipTableAsync(bool tryInitPath, CancellationToken cancellationToken = default)
        {
            // 假若不是我创造道路, 
            // 尝试插入初始路径,如果它尚未存在的话;
            // 所以我们总是有这条路, 在此之前, silo 开始工作。
            // 注意,当一个管理员连接向连接字符串添加 /ClusterId 时,节点是相对的
            await UsingZookeeper(rootConnectionString, async zk =>
            {
                try
                {
                    await zk.createAsync(this.clusterPath, null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
                    //如果我们到了这里 我们知道我们刚刚 创建了版本=0的部署路径
                    LogInformationCreatedNewDeploymentPath(this.clusterPath);
                }
                catch (KeeperException.NodeExistsException)
                {
                    LogDebugDeploymentPathAlreadyExists(this.clusterPath);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 点不支持读取。 读取完整快照并从该快照中选择行 。
        /// </summary>
        /// <param name="siloAddress">大会主席的地址 silo 需要阅读其成员资格信息。</param>
        /// <returns>一项错误的任务,即指出该点为文字,但没有得到支持。</returns>
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
        /// 原子读取成员表的全部内容。
        /// 返回的 MembershipTableData 包括所有 MembershipEntry 全部条目 silos 表中和表格中的 
        /// 本表格的TableVersion 。 MembershipEntries 和 TableVersion 必须从原子角度阅读。
        /// </summary>
        /// <returns>某一表格的成员信息: MembershipTableData ,包括多个 MembershipEntry 条目和
        /// TableVersion全部从原子上读取</returns>
        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 运行自有连接时,行会按顺序阅读。
        /// 每一成员记录在心跳之前读取。
        /// 表格和儿童换代检查将完整的快照围起来。
        /// 更改整次连续传动通道的修改,直到围栏稳定或调用者取消
        /// 运行中。
        /// 本地读物连接损失失败在同一期最多可再作四次。
        /// 每次重试之前,操作会等到会议超时后再进行, 以等待该会的下一个连接事件。
        /// 当等待期结束或会话结束时,重新研究将产生并保存本地结果。
        /// 取消呼叫者停止进一步请求,同时接受请求和客户接近完成。
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
            // 每一个不匹配都证明在通行证期间 承诺了一种卡风更新。
            // 而不是拒绝有限的前进进展。
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
        /// 原子试图插入(添加)一个新的 MembershipEntry ,一个 silo ,并更新 TableVersion。
        /// 如果操作成功,将对表格作如下修改:
        /// (1) 表格中将增加新的 MembershipEntry 。
        /// 2) 新增的 MembershipEntry 也将加上新的独有自动生成的 eTag。
        /// 3) TableVersion.Version 表格中的表格将更新到新的 TableVersion.Version.
        /// 4) TableVersion 表中的牌将更新到新的独有自动生成 eTag。
        /// 所有对表格的修改,插入新行和更新表格版本及相关的标签,应原生化或原子衰落,无副作用。
        /// 操作在下列每一条件下均应失败:
        /// 1) A MembershipEntry 给定 silo 表格中已经存在
        /// (2) TableVersion 更新失败,因为给定 TableVersion etag(按 TableVersion.VersionEtag 属性指定)与表中的 TableVersion etag不匹配。
        /// </summary>
        /// <param name="entry">MembershipEntry 插入。</param>
        /// <param name="tableVersion">本表格的新 TableVersion 及其表单。</param>
        /// <returns>如果插入操作成功且以其他方式虚假, 则正确。</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entry"/> 或 <paramref name="tableVersion"/> 是 <see langword="null"/>。
        /// </exception>
        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 有条件交易提交一次。
        /// 承诺结果未知,保留了打电话者解决这一结果的能力。
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
        /// 原子试图更新 MembershipEntry 一对一 silo 更新 TableVersion.
        /// 如果操作成功,将对表格作如下修改:
        /// (1) 与 MembershipEntry 为此 silo 将更新到新的 MembershipEntry (旧条目将被新条目完全替换) 
        /// (2) 国家 eTag 更新的《公约》和《公约》 MembershipEntry 也将 eTag 与新独自动生成 eTag.
        /// 3) TableVersion.Version 表格中的表格将更新到新的 TableVersion.Version.
        /// 4) TableVersion 表中的牌将更新到新的独有自动生成 eTag。
        /// 所有对表格的修改、新行更新以及表格版本和相关星格的更新,都应原生化地进行,否则原子衰落,无副作用。
        /// 操作在下列每一条件下均应失败:
        /// 1) A MembershipEntry 给定 silo 表格中不存在
        /// 2) A MembershipEntry 给定 silo 表格中存在该表,但其表中的列表与所提供的标签不符。
        /// 3) TableVersion 更新失败,因为给定 TableVersion etag(按 TableVersion.VersionEtag 属性指定)与表中的 TableVersion 标签不符。
        /// </summary>
        /// <param name="entry">MembershipEntry 待更新。</param>
        /// <param name="etag">给定 MembershipEntry的标签。</param>
        /// <param name="tableVersion">本表格的新 TableVersion 及其表单。</param>
        /// <returns>如果更新操作成功且以其他方式虚假, 则正确。</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entry"/>、 <paramref name="etag"/>或 <paramref name="tableVersion"/> 是
        /// <see langword="null"/>.
        /// </exception>
        [Obsolete("Use UpdateRowAsync instead.")]
        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

        /// <inheritdoc />
        /// <remarks>
        /// 有条件交易提交一次。
        /// 承诺结果未知,保留了打电话者解决这一结果的能力。
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
        /// 使用无条件本地更新,将自有 silo IAmAlive 的时戳写入心跳节点。
        /// 保存成份行和表格版本。本地存储失败会传播给呼叫者。
        /// </summary>
        /// <param name="entry">拥有 silo加入时含有心跳时间戳。</param>
        /// <returns>代表成功执行此操作的任务。 </returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> 是 <see langword="null"/>.</exception>
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
        /// 删除给定 clusterId的所有表格条目
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
        /// 读取节点/IP:Port@Gen和/IP:Port@Gen/IAmAlive (一行)
        /// </summary>
        /// <param name="zk">用于读</param>的动物园看守实例
        /// <param name="siloAddress"> silo 地址。</param>
        /// <param name="allowMissing">缺失的行是否由空点表示, 读取。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
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

        // 这些代表揭露了本地要求的边界,以对实际操作环进行确定性测试。
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
            // ZooKeeperNetEx 没有任何意义。 在客户提出请求之前,保持其生命,
            // 处理完成时, 观察失败 即使呼叫器停止等待。
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
            // 异 ZKUtil.deleteRecursiveAsync,检查请求之间的取消。
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
        /// ZooKeeper返回的顺序依顺序对行进行排顺序评估。
        /// 有条件删除冲突之前经过五次尝试的重新评价
        /// 作业报告争议。
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
                    // 只重新评估这行: 其版本和心跳版本的守护资格。
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

                    // 原子退休消除了两个节点;幸存的一排人心跳缺失。
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
    /// 出版 ZooKeeper 相同相片的连接转换
    /// 并记录观察诊断活动。
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
                // 请求在连接执行时被接受, 并在下一个连接中被连接 。
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
