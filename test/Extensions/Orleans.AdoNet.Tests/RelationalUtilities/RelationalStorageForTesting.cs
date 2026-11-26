using System.Data.Common;
using Orleans.Tests.SqlUtils;
using Tester.RelationalUtilities;
using TestExtensions;

namespace UnitTests.General
{
    public abstract class RelationalStorageForTesting
    {
        private static readonly Dictionary<string, Func<string, RelationalStorageForTesting>> instanceFactory =
            new Dictionary<string, Func<string, RelationalStorageForTesting>>
            {
                {AdoNetInvariants.InvariantNameSqlServer, cs => new SqlServerStorageForTesting(cs)},
                {AdoNetInvariants.InvariantNameMySql, cs => new MySqlStorageForTesting(cs)},
                {AdoNetInvariants.InvariantNamePostgreSql, cs => new PostgreSqlStorageForTesting(cs)}
            };

        private static readonly Dictionary<string, string> ConnectionStringsByInvariant =
            new()
            {
                {AdoNetInvariants.InvariantNameSqlServer, TestDefaultConfiguration.MsSqlConnectionString!},
                {AdoNetInvariants.InvariantNameMySql, TestDefaultConfiguration.MySqlConnectionString!},
                {AdoNetInvariants.InvariantNamePostgreSql, TestDefaultConfiguration.PostgresConnectionString!}
            };

        public IRelationalStorage Storage { get; private set; }

        public string CurrentConnectionString
        {
            get { return Storage.ConnectionString; }
        }

        /// <summary>
        /// 提供者类型(MySQL、SQL服务员、甲骨文、 PostgreSQL等)的名称。
        /// </summary>
        protected abstract string ProviderMoniker { get; }

        /// <summary>
        /// 延迟查询, 然后在测试中取消该查询, 以查看取消是否有效 。
        /// </summary>
        public abstract string CancellationTestQuery { get; }

        public abstract string CreateStreamTestTable { get; }

        public virtual string DeleteStreamTestTable
        { get { return "DELETE StreamingTest;"; } }

        public virtual string StreamTestSelect
        { get { return "SELECT Id, StreamData FROM StreamingTest WHERE Id = @streamId;"; } }

        public virtual string StreamTestInsert
        { get { return "INSERT INTO StreamingTest(Id, StreamData) VALUES(@id, @streamData);"; } }

        /// <summary>
        /// 在数据库中创建 Orleans smama 的脚本,通常为 CreateOrleansTables_xxxx.sql
        /// </summary>
        // 新的集群和持久性脚本可以安装当前常规;历史迁移可以取代这些例行程序。
        protected string[] SetupSqlScriptFileNames => new[] {
                                $"{this.ProviderMoniker}-Main.sql",
                                $"{this.ProviderMoniker}-Clustering.sql",
                                $"{this.ProviderMoniker}-Persistence.sql",
                                $"{this.ProviderMoniker}-Reminders.sql",
                                $"{this.ProviderMoniker}-Streaming.sql",
                                $"{this.ProviderMoniker}-GrainDirectory.sql"
                                }.Concat(Directory.GetFiles(Environment.CurrentDirectory, $"{this.ProviderMoniker}-Reminders-*.sql")
                                .Concat(Directory.GetFiles(Environment.CurrentDirectory, $"{this.ProviderMoniker}-Streaming-*.sql"))
                                .Concat(Directory.GetFiles(Environment.CurrentDirectory, $"{this.ProviderMoniker}-GrainDirectory-*.sql"))
                                .Select(f => Path.GetFileName(f))
                                .OrderBy(f => f)).ToArray();

        /// <summary>
        /// 创建数据库时使用指定名称的查询模板。
        /// </summary>
        protected abstract string CreateDatabaseTemplate { get; }

        /// <summary>
        /// 用于用给定名称丢弃数据库的查询模板 。
        /// </summary>
        protected abstract string DropDatabaseTemplate { get; }

        /// <summary>
        /// 如果存在带有指定名称的数据库, 询问模板 。
        /// </summary>
        protected abstract string ExistsDatabaseTemplate { get; }

        /// <summary>
        /// 将给定脚本转换成分批执行的分批
        /// </summary>
        /// <param name="setupScript">脚本。通常 CreateOrleansTables_xxxx.sql</param>
        /// <param name="databaseName">数据库的名称</param>
        protected abstract IEnumerable<string> ConvertToExecutableBatches(string setupScript, string databaseName);

        public static void CheckPreconditionsOrThrow(string invariantName, string? connectionString = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.SkipUnless(ConnectionStringsByInvariant.TryGetValue(invariantName, out connectionString), $"Unknown ADO.NET invariant, '{invariantName}'");
            }

            Assert.SkipWhen(string.IsNullOrEmpty(connectionString), "Connection string not provided.");
        }

        internal static bool IsConnectionStringConfigured(string invariantName)
            => ConnectionStringsByInvariant.TryGetValue(invariantName, out var connectionString)
                && !string.IsNullOrEmpty(connectionString);

        public static async Task<RelationalStorageForTesting> SetupInstance(
            string invariantName,
            string testDatabaseName,
            string? connectionString = null,
            CancellationToken cancellationToken = default,
            IEnumerable<string>? setupSqlScriptFileNames = null)
        {
            CheckPreconditionsOrThrow(invariantName, connectionString);
            if (string.IsNullOrWhiteSpace(invariantName))
            {
                throw new ArgumentException("The name of invariant must contain characters", nameof(invariantName));
            }

            if (string.IsNullOrWhiteSpace(testDatabaseName))
            {
                throw new ArgumentException("database string must contain characters", nameof(testDatabaseName));
            }

            Console.WriteLine("Initializing relational databases...");

            RelationalStorageForTesting testStorage;
            testStorage = CreateTestInstance(invariantName, connectionString);

            if (string.IsNullOrEmpty(testStorage.CurrentConnectionString))
            {
                Console.WriteLine("No storage configured");
                return testStorage;
            }

            Console.WriteLine("Dropping and recreating database '{0}' with ConnectionString '{1}'", testDatabaseName, testStorage.CurrentConnectionString);
            testStorage.PrepareForDatabaseReset(testDatabaseName);

            if (await testStorage.ExistsDatabaseAsync(testDatabaseName, cancellationToken))
            {
                await testStorage.DropDatabaseAsync(testDatabaseName, cancellationToken);
            }

            await testStorage.CreateDatabaseAsync(testDatabaseName, cancellationToken);

            //旧的存储实例有前一个连接字符串, 时间有一个新控点, 并有新的连接字符串...
            testStorage = testStorage.CopyInstance(testDatabaseName);
            await testStorage.WaitForDatabaseReadyAsync(cancellationToken);

            Console.WriteLine("Creating database tables...");

            var setupScript = string.Empty;

            // 相连接脚本
            foreach (var fileName in setupSqlScriptFileNames ?? testStorage.SetupSqlScriptFileNames)
            {
                setupScript += await File.ReadAllTextAsync(fileName, cancellationToken);

                // 以防在文件之间添加 CRLF, 但结尾应该是一条新行。
                setupScript += "\r\n";
            }

            await testStorage.ExecuteSetupScript(setupScript, testDatabaseName, cancellationToken);

            Console.WriteLine("Initializing relational databases done.");

            return testStorage;
        }

        private static RelationalStorageForTesting CreateTestInstance(string invariantName, string? connectionString)
        {
            return instanceFactory[invariantName](connectionString!);
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="invariantName"></param>
        /// <param name="connectionString"></param>
        protected RelationalStorageForTesting(string invariantName, string? connectionString)
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                Storage = RelationalStorage.CreateInstance(invariantName, connectionString);
            }
            else
            {
                throw Xunit.Sdk.SkipException.ForSkip("ConnectionString not provided.");
            }
        }

        protected virtual void PrepareForDatabaseReset(string databaseName)
        {
        }

        protected virtual Task WaitForDatabaseReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// 在测试上下文中执行给定脚本 。
        /// </summary>
        /// <param name="setupScript">脚本。通常 CreateOrleansTables_xxxx.sql</param>
        /// <param name="dataBaseName">目标数据库要被覆盖</param>
        /// <returns></returns>
        protected virtual async Task ExecuteSetupScript(
            string setupScript,
            string dataBaseName,
            CancellationToken cancellationToken)
        {
            var splitScripts = ConvertToExecutableBatches(setupScript, dataBaseName);
            foreach (var script in splitScripts)
            {
                await ExecuteSetupScriptBatchAsync(script, cancellationToken);
            }
        }

        protected virtual async Task ExecuteSetupScriptBatchAsync(string script, CancellationToken cancellationToken)
        {
            _ = await Storage.ExecuteAsync(script, cancellationToken);
        }

        /// <summary>
        /// 使用给定的 <see paramref="storage"/> 存储对象检查数据库的存在。
        /// </summary>
        /// <param name="databaseName">要检查的数据库名称 。</param>
        /// <returns><em>真实</em> 如果存在指定数据库。 <em>平</em> 否则。</returns>
        private async Task<bool> ExistsDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            var ret = await Storage.ReadAsync(string.Format(ExistsDatabaseTemplate, databaseName), command =>
            { }, (selector, resultSetCount, _) => { return Task.FromResult(selector.GetBoolean(0)); }, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ret.First();
        }

        /// <summary>
        /// 创建带有指定名称的数据库。
        /// </summary>
        /// <param name="databaseName">要创建的数据库的名称。</param>
        /// <returns>如果 DDL 查询成功, 电话将成功。 否则将会丢弃例外 。</returns>
        private async Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await Storage.ExecuteAsync(
                string.Format(CreateDatabaseTemplate, databaseName),
                command => { },
                cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        /// <summary>
        /// 丢弃带有指定名称的数据库 。
        /// </summary>
        /// <param name="databaseName">要丢弃的数据库名称。</param>
        /// <returns>如果 DDL 查询成功, 电话将成功。 否则将会丢弃例外 。</returns>
        protected virtual async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await Storage.ExecuteAsync(
                string.Format(DropDatabaseTemplate, databaseName),
                command => { },
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 通过修改数据库名称,根据旧的连接字符串创建存储的新实例。
        /// </summary>
        /// <param name="newDatabaseName">数据库的连接字符串实例名称。</param>
        /// <returns>一个新的 <see cref="RelationalStorageForTesting"/> 例,该例具有相同的连接字符串,但有一个新的 databaseName。</returns>
        private RelationalStorageForTesting CopyInstance(string newDatabaseName)
        {
            var csb = new DbConnectionStringBuilder();
            csb.ConnectionString = Storage.ConnectionString;
            csb["Database"] = newDatabaseName;
            return CreateTestInstance(Storage.InvariantName, csb.ConnectionString);
        }
    }
}
