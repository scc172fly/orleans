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
        /// 提供程序类型的名称（MySQL、SQLServer、Oracle、PostgreSQL 等）。
        /// </summary>
        protected abstract string ProviderMoniker { get; }

        /// <summary>
        /// 一个延迟执行的查询，随后在测试中取消该查询，以验证取消是否有效。
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
        /// 用于在数据库中创建 Orleans 架构的脚本，通常为 CreateOrleansTables_xxxx.sql
        /// </summary>
        // 全新的集群和持久化脚本安装当前例程；历史迁移脚本可以替换这些例程。
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
        /// 用于创建指定名称的数据库的查询模板。
        /// </summary>
        protected abstract string CreateDatabaseTemplate { get; }

        /// <summary>
        /// 用于删除指定名称的数据库的查询模板。
        /// </summary>
        protected abstract string DropDatabaseTemplate { get; }

        /// <summary>
        /// 用于检查指定名称的数据库是否存在的查询模板。
        /// </summary>
        protected abstract string ExistsDatabaseTemplate { get; }

        /// <summary>
        /// 将给定脚本拆分为依次执行的批次
        /// </summary>
        /// <param name="setupScript">脚本，通常为 CreateOrleansTables_xxxx.sql</param>
        /// <param name="databaseName">数据库名称</param>
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

            //旧存储实例使用先前的连接字符串，现在需要使用新连接字符串的新句柄……
            testStorage = testStorage.CopyInstance(testDatabaseName);
            await testStorage.WaitForDatabaseReadyAsync(cancellationToken);

            Console.WriteLine("Creating database tables...");

            var setupScript = string.Empty;

            // 拼接脚本
            foreach (var fileName in setupSqlScriptFileNames ?? testStorage.SetupSqlScriptFileNames)
            {
                setupScript += await File.ReadAllTextAsync(fileName, cancellationToken);

                // 为保险起见，在文件之间添加 CRLF，不过这些文件本应以换行符结尾。
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
        /// 构造函数。
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
        /// 在测试上下文中执行给定脚本。
        /// </summary>
        /// <param name="setupScript">脚本，通常为 CreateOrleansTables_xxxx.sql</param>
        /// <param name="dataBaseName">要填充数据的目标数据库</param>
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
        /// 使用给定的 <see paramref="storage"/> 存储对象检查数据库是否存在。
        /// </summary>
        /// <param name="databaseName">要检查是否存在的数据库名称。</param>
        /// <returns>如果指定数据库存在，则为 <em>TRUE</em>；否则为 <em>FALSE</em>。</returns>
        private async Task<bool> ExistsDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            var ret = await Storage.ReadAsync(string.Format(ExistsDatabaseTemplate, databaseName), command =>
            { }, (selector, resultSetCount, _) => { return Task.FromResult(selector.GetBoolean(0)); }, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ret.First();
        }

        /// <summary>
        /// 创建指定名称的数据库。
        /// </summary>
        /// <param name="databaseName">要创建的数据库名称。</param>
        /// <returns>如果 DDL 查询成功，则调用成功；否则将引发异常。</returns>
        private async Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await Storage.ExecuteAsync(
                string.Format(CreateDatabaseTemplate, databaseName),
                command => { },
                cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        /// <summary>
        /// 删除指定名称的数据库。
        /// </summary>
        /// <param name="databaseName">要删除的数据库名称。</param>
        /// <returns>如果 DDL 查询成功，则调用成功；否则将引发异常。</returns>
        protected virtual async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await Storage.ExecuteAsync(
                string.Format(DropDatabaseTemplate, databaseName),
                command => { },
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 在旧连接字符串的基础上更改数据库名称，以创建新的存储实例。
        /// </summary>
        /// <param name="newDatabaseName">连接字符串中数据库的实例名称。</param>
        /// <returns>一个新的 <see cref="RelationalStorageForTesting"/> 实例，其连接字符串除 databaseName 更新外保持不变。</returns>
        private RelationalStorageForTesting CopyInstance(string newDatabaseName)
        {
            var csb = new DbConnectionStringBuilder();
            csb.ConnectionString = Storage.ConnectionString;
            csb["Database"] = newDatabaseName;
            return CreateTestInstance(Storage.InvariantName, csb.ConnectionString);
        }
    }
}
