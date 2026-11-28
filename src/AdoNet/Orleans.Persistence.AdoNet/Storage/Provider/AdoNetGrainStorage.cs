using Orleans.Persistence.AdoNet.Storage;
using Orleans.Providers;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration.Overrides;
using Orleans.Configuration;
using Orleans.Runtime.Configuration;
using Orleans.Serialization.Serializers;

namespace Orleans.Storage
{
    /// <summary>
    /// <see cref="AdoNetGrainStorage"/> 使用的日志记录代码。
    /// </summary>
    /// <remarks>这些代码来自 <em>Orleans.Providers.ProviderErrorCode</em> 和 <em>Orleans.Providers.AzureProviderErrorCode</em>。</remarks>
    internal enum RelationalStorageProviderCodes
    {
        // 这些代码来自 Orleans.Providers.ProviderErrorCode 和 Orleans.Providers.AzureProviderErrorCode。
        ProvidersBase = 200000,

        RelationalProviderBase = ProvidersBase + 400,
        RelationalProviderDeleteError = RelationalProviderBase + 8,
        RelationalProviderInitProvider = RelationalProviderBase + 9,
        RelationalProviderNoDeserializer = RelationalProviderBase + 10,
        RelationalProviderNoStateFound = RelationalProviderBase + 11,
        RelationalProviderClearing = RelationalProviderBase + 12,
        RelationalProviderCleared = RelationalProviderBase + 13,
        RelationalProviderReading = RelationalProviderBase + 14,
        RelationalProviderRead = RelationalProviderBase + 15,
        RelationalProviderReadError = RelationalProviderBase + 16,
        RelationalProviderWriting = RelationalProviderBase + 17,
        RelationalProviderWrote = RelationalProviderBase + 18,
        RelationalProviderWriteError = RelationalProviderBase + 19
    }

    /// <summary>
    /// 从已注册的服务及选项创建命名的 <see cref="AdoNetGrainStorage"/> 提供程序。
    /// </summary>
    public static class AdoNetGrainStorageFactory
    {
        /// <summary>
        /// 使用指定名称创建 ADO.NET Grain 存储提供程序。
        /// </summary>
        /// <param name="services">用于解析依赖项及命名选项的服务提供程序。</param>
        /// <param name="name">存储提供程序名称。</param>
        /// <returns>已配置的 ADO.NET Grain 存储提供程序。</returns>
        public static AdoNetGrainStorage Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<AdoNetGrainStorageOptions>>();
            var clusterOptions = services.GetProviderClusterOptions(name);
            return ActivatorUtilities.CreateInstance<AdoNetGrainStorage>(services, Options.Create(optionsMonitor.Get(name)), name, clusterOptions);
        }
    }

    /// <summary>
    /// 使用 ADO.NET 将 Grain 状态存储到关系数据库中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过 <see cref="AdoNetGrainStorageOptions"/> 提供配置。
    /// </para>
    /// <para>
    /// 仅配置 <see cref="AdoNetGrainStorageOptions.ConnectionString"/> 或
    /// <see cref="AdoNetGrainStorageOptions.DataSource"/>.
    /// </para>
    /// <para>
    /// <see cref="AdoNetGrainStorageOptions.Invariant"/> 标识 ADO.NET 提供程序，默认值为
    /// <c>Microsoft.Data.SqlClient</c>。
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Name = {Name}, ConnectionString = {Storage.ConnectionString}")]
    public partial class AdoNetGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        /// <summary>
        /// 获取或设置用于将 Grain 状态转换为存储表示及从存储表示还原状态的序列化器。
        /// </summary>
        public IGrainStorageSerializer Serializer { get; set; }

        /// <summary>
        /// BinaryFormatSerializer 的标记。
        /// </summary>
        public const string BinaryFormatSerializerTag = "BinaryFormatSerializer";
        /// <summary>
        /// JsonFormatSerializer 的标记。
        /// </summary>
        public const string JsonFormatSerializerTag = "JsonFormatSerializer";
        /// <summary>
        /// XmlFormatSerializer 的标记。
        /// </summary>
        public const string XmlFormatSerializerTag = "XmlFormatSerializer";

        /// <summary>
        /// 使用此关系数据库提供程序的服务 ID。
        /// </summary>
        private readonly string serviceId;
        private readonly IActivatorProvider _activatorProvider;
        private readonly ILogger logger;

        /// <summary>
        /// 用于后端操作的存储。
        /// </summary>
        private IRelationalStorage Storage { get; set; } = null!;

        /// <summary>
        /// 从类名中提取基类型时使用的分隔字符，适用于类名为
        /// <see cref="Type.AssemblyQualifiedName"/> 或 <see cref="Type.FullName"/> 的情形。
        /// <see cref="ExtractBaseClass(string)"/>.
        /// </summary>
        private static char[] BaseClassExtractionSplitDelimeters { get; } = new[] { '[', ']' };

        /// <summary>
        /// 从 Orleans 数据库初始化此结构的默认查询。
        /// </summary>
        public const string DefaultInitializationQuery = "SELECT QueryKey, QueryText FROM OrleansQuery WHERE QueryKey = 'WriteToStorageKey' OR QueryKey = 'ReadFromStorageKey' OR QueryKey = 'ClearStorageKey' OR QueryKey = 'DeleteStorageKey'";

        /// <summary>
        /// 当前使用的查询。更新此值时，新查询将立即生效。
        /// </summary>
        public RelationalStorageProviderQueries CurrentOperationalQueries { get; set; } = null!;

        /// <summary>
        /// 将自然键、Grain ID 及 Grain 类型哈希为更紧凑的索引的哈希生成器。
        /// </summary>
        public IStorageHasherPicker HashPicker { get; set; }

        private readonly AdoNetGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 初始化 <see cref="AdoNetGrainStorage"/> 类的新实例。
        /// </summary>
        /// <param name="activatorProvider">用于创建空 Grain 状态值的激活器。</param>
        /// <param name="logger">提供程序的日志记录器。</param>
        /// <param name="options">存储提供程序选项。</param>
        /// <param name="clusterOptions">用于限定已存储 Grain 状态范围的集群选项。</param>
        /// <param name="name">存储提供程序名称。</param>
        public AdoNetGrainStorage(
            IActivatorProvider activatorProvider,
            ILogger<AdoNetGrainStorage> logger,
            IOptions<AdoNetGrainStorageOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            string name)
        {
            this.options = options.Value;
            this.name = name;
            _activatorProvider = activatorProvider;
            this.logger = logger;
            this.serviceId = clusterOptions.Value.ServiceId;
            this.Serializer = options.Value.GrainStorageSerializer;
            this.HashPicker = options.Value.HashPicker ?? new StorageHasherPicker(new[] { new OrleansDefaultHasher() });
        }

        /// <summary>
        /// 将提供程序的初始化及关闭回调注册到 Silo 生命周期。
        /// </summary>
        /// <param name="lifecycle">Silo 生命周期。</param>
        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(OptionFormattingUtilities.Name<AdoNetGrainStorage>(this.name), this.options.InitStage, Init, Close);
        }

        /// <summary>此存储提供程序的状态数据清除函数。</summary>
        /// <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/>.
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public Task ClearStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => ClearStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public async Task ClearStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (grainId, baseGrainType) = GetStorageKey(grainType, grainReference);
            ArgumentNullException.ThrowIfNull(grainState);
            LogTraceClearingGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);

            if (!grainState.RecordExists && string.IsNullOrWhiteSpace(grainState.ETag))
            {
                await ReadStateAsync(grainType, grainReference, grainState, cancellationToken).ConfigureAwait(false);
                if (!grainState.RecordExists)
                {
                    return;
                }
            }

            string? storageVersion = null;
            try
            {
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 存储配置始终提供匹配的哈希算法。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 存储配置始终提供匹配的哈希算法。

                var queryText = options.DeleteStateOnClear ? CurrentOperationalQueries.DeleteState : CurrentOperationalQueries.ClearState;

                // Init 中的向后兼容性检查应处理此情况，并在到达此状态前抛出异常。
                if (queryText is null)
                {
                    throw new UnreachableException($"QueryText is null for {nameof(options.DeleteStateOnClear)}={options.DeleteStateOnClear}");
                }
                var clearRecord = (await Storage.ReadAsync(queryText, command =>
                {
                    command.AddParameter("GrainIdHash", grainIdHash);
                    command.AddParameter("GrainIdN0", grainId.N0Key);
                    command.AddParameter("GrainIdN1", grainId.N1Key);
                    command.AddParameter("GrainTypeHash", grainTypeHash);
                    command.AddParameter("GrainTypeString", baseGrainType);
                    command.AddParameter("GrainIdExtensionString", grainId.StringKey);
                    command.AddParameter("ServiceId", serviceId);
                    command.AddParameter("GrainStateVersion", !string.IsNullOrWhiteSpace(grainState.ETag) ? int.Parse(grainState.ETag, CultureInfo.InvariantCulture) : default(int?));
                }, (selector, resultSetCount, token) => Task.FromResult(selector.GetValue(0).ToString()), cancellationToken: cancellationToken).ConfigureAwait(false));
                storageVersion = clearRecord.SingleOrDefault();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogErrorClearingGrainState(ex, serviceId, name, baseGrainType, grainId, grainState.ETag);
                throw;
            }

            const string OperationString = "ClearState";
            var inconsistentStateException = CheckVersionInconsistency(OperationString, serviceId, this.name, storageVersion, grainState.ETag, baseGrainType, grainId.ToString());
            if (inconsistentStateException != null)
            {
                throw inconsistentStateException;
            }

            // 如果设置了清除时删除，由于记录已被删除，ETag 应为 null。
            // 数据库脚本返回已删除记录的版本加 1 作为 storageVersion，用于上方的 CheckVersionInconsistency 检查。
            // 未发现错误，因此可以更新 Grain 持有的状态及其版本。
            grainState.ETag = options.DeleteStateOnClear ? null : storageVersion;
            grainState.RecordExists = false;
            grainState.State = CreateInstance<T>();
            LogTraceClearedGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);
        }


        /// <summary>此存储提供程序的状态数据读取函数。</summary>
        /// <see cref="IGrainStorage.ReadStateAsync{T}(string, GrainId, IGrainState{T})"/>.
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public Task ReadStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => ReadStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public async Task ReadStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (grainId, baseGrainType) = GetStorageKey(grainType, grainReference);
            ArgumentNullException.ThrowIfNull(grainState);
            LogTraceReadingGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);

            try
            {
                var commandBehavior = CommandBehavior.Default;
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 存储配置始终提供匹配的哈希算法。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 存储配置始终提供匹配的哈希算法。
                var readRecords = (await Storage.ReadAsync(
                    CurrentOperationalQueries.ReadFromStorage,
                    command =>
                    {
                        command.AddParameter("GrainIdHash", grainIdHash);
                        command.AddParameter("GrainIdN0", grainId.N0Key);
                        command.AddParameter("GrainIdN1", grainId.N1Key);
                        command.AddParameter("GrainTypeHash", grainTypeHash);
                        command.AddParameter("GrainTypeString", baseGrainType);
                        command.AddParameter("GrainIdExtensionString", grainId.StringKey);
                        command.AddParameter("ServiceId", serviceId);
                    },
                    (selector, resultSetCount, token) =>
                    {
                        object? storageState = null;
                        int? version;
                        byte[]? payload;
                        payload = selector.GetValueOrDefault<byte[]>("PayloadBinary");
                        if (payload != null)
                        {
                            storageState = Serializer.Deserialize<T>(new BinaryData(payload));
                        }
                        version = selector.GetNullableInt32("Version");
                        var result = Tuple.Create(storageState, version?.ToString(CultureInfo.InvariantCulture), payload is not null);
                        return Task.FromResult(result);
                    },
                    commandBehavior, cancellationToken).ConfigureAwait(false)).SingleOrDefault();

                T? state = readRecords != null ? (T)readRecords.Item1! : default;
                string? etag = readRecords != null ? readRecords.Item2 : null;
                bool recordExists = readRecords?.Item3 == true;
                if (state == null)
                {
                    LogTraceNullGrainStateRead(serviceId, name, baseGrainType, grainId, grainState.ETag);
                    state = CreateInstance<T>();
                }

                grainState.State = state;
                grainState.ETag = etag;
                grainState.RecordExists = recordExists;
                LogTraceReadGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogErrorReadingGrainState(ex, serviceId, name, baseGrainType, grainId, grainState.ETag);
                throw;
            }
        }


        /// <summary>此存储提供程序的状态数据写入函数。</summary>
        /// <see cref="IGrainStorage.WriteStateAsync{T}(string, GrainId, IGrainState{T})"/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public Task WriteStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => WriteStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 null。</exception>
        public async Task WriteStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ArgumentNullException.ThrowIfNull(grainState);
            var data = grainState.State;
            var (grainId, baseGrainType) = GetStorageKey(grainType, grainReference);
            LogTraceWritingGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);

            string? storageVersion = null;
            try
            {
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 存储配置始终提供匹配的哈希算法。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 存储配置始终提供匹配的哈希算法。
                var writeRecord = await Storage.ReadAsync(CurrentOperationalQueries.WriteToStorage, command =>
                {
                    var serialized = this.Serializer.Serialize<T>(grainState.State);

                    command.AddParameter("GrainIdHash", grainIdHash);
                    command.AddParameter("GrainIdN0", grainId.N0Key);
                    command.AddParameter("GrainIdN1", grainId.N1Key);
                    command.AddParameter("GrainTypeHash", grainTypeHash);
                    command.AddParameter("GrainTypeString", baseGrainType);
                    command.AddParameter("GrainIdExtensionString", grainId.StringKey);
                    command.AddParameter("ServiceId", serviceId);
                    command.AddParameter("GrainStateVersion", !string.IsNullOrWhiteSpace(grainState.ETag) ? int.Parse(grainState.ETag, CultureInfo.InvariantCulture) : default(int?));
                    command.AddParameter("PayloadBinary", serialized.ToArray());
                }, (selector, resultSetCount, token) =>
                { return Task.FromResult(selector.GetNullableInt32("NewGrainStateVersion").ToString()); }, cancellationToken: cancellationToken).ConfigureAwait(false);
                storageVersion = writeRecord.SingleOrDefault();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogErrorWritingGrainState(ex, serviceId, name, baseGrainType, grainId, grainState.ETag);
                throw;
            }

            const string OperationString = "WriteState";
            var inconsistentStateException = CheckVersionInconsistency(OperationString, serviceId, this.name, storageVersion, grainState.ETag, baseGrainType, grainId.ToString());
            if (inconsistentStateException != null)
            {
                throw inconsistentStateException;
            }

            // 未发现错误，因此可以更新 Grain 持有的状态的版本。
            grainState.ETag = storageVersion;
            grainState.RecordExists = true;

            LogTraceWroteGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);
        }

        /// <summary>此存储提供程序的初始化函数。</summary>
        private async Task Init(CancellationToken cancellationToken)
        {
            Storage = RelationalStorage.CreateInstance(options.Invariant, options.ConnectionString, options.DataSource);
            var queries = await Storage.ReadAsync(DefaultInitializationQuery, command => { }, (selector, resultSetCount, token) =>
            {
                return Task.FromResult(Tuple.Create(selector.GetValue<string>("QueryKey"), selector.GetValue<string>("QueryText")));
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            // 此检查用于向后兼容性：
            // 1. 某些 ADO.NET 固定名称对应的提供程序可能不支持清除时删除。
            // 2. ADO.NET 固定名称对应的提供程序支持清除时删除，但更新后的持久化脚本尚未部署到管理数据库。
            var deleteStateQuery = queries.SingleOrDefault(i => i.Item1 == "DeleteStorageKey")?.Item2;
            if (options.DeleteStateOnClear && deleteStateQuery is null)
            {
                throw new ArgumentException($"{nameof(options.DeleteStateOnClear)}=true is not supported. Use {nameof(options.DeleteStateOnClear)}=false instead or check persistence scripts.");
            }
            CurrentOperationalQueries = new RelationalStorageProviderQueries(
                queries.Single(i => i.Item1 == "WriteToStorageKey").Item2,
                queries.Single(i => i.Item1 == "ReadFromStorageKey").Item2,
                queries.Single(i => i.Item1 == "ClearStorageKey").Item2,
                deleteStateQuery);

            LogInfoInitializedStorageProvider(
                serviceId,
                name,
                Storage.InvariantName,
                new(Storage.ConnectionString));
        }

        /// <summary>
        /// 关闭此提供程序。
        /// </summary>
        private Task Close(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 按数据库脚本中的定义检查版本不一致问题。
        /// </summary>
        /// <param name="serviceId">服务 ID。</param>
        /// <param name="providerName">此存储提供程序的名称。</param>
        /// <param name="operation">尝试执行的操作。</param>
        /// <param name="storageVersion">存储中的版本。</param>
        /// <param name="grainVersion">Grain 的版本。</param>
        /// <param name="normalizedGrainType">不包含泛型信息的 Grain 类型。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <returns>要抛出的异常；如果未检测到冲突，则为 <em>null</em>。</returns>
        /// <remarks>这表示数据库中的版本未更新，或者当 Grain 版本为 null 时，存储版本却不为 null，
        /// 实质上意味着出现了重复激活及保存。</remarks>
        private static InconsistentStateException? CheckVersionInconsistency(string operation, string serviceId, string providerName, string? storageVersion, string? grainVersion, string normalizedGrainType, string grainId)
        {
            // 如果两者相同，则表示没有向存储插入或更新任何行。
            // 实质上意味着 UPDATE 或 INSERT 的条件因 ETag 冲突而失败。
            // 此外，如果 Grain 状态中的 ETag 和返回的存储版本均为 null，
            // 则意味着有两个 Grain 激活实例，另一个已成功写入其状态。
            //
            // 注意：存储也可以返回新的及旧的 ETag（Version），但目前没有这样做。
            if (storageVersion == grainVersion || storageVersion is { Length: 0 } || (storageVersion is null && grainVersion is not null))
            {
                // TODO：此错误消息应在不同后端之间保持统一。
                return new InconsistentStateException($"Version conflict ({operation}): ServiceId={serviceId} ProviderName={providerName} GrainType={normalizedGrainType} GrainId={grainId} ETag={grainVersion}.");
            }

            return null;
        }

        /// <summary>
        /// 将 Grain 状态映射为存储在键列中的 Grain 键及 Grain 类型值，映射方式由以下选项确定：
        /// <see cref="AdoNetGrainStorageOptions.KeyFormat"/>.
        /// </summary>
        /// <param name="stateName">传入 <see cref="IGrainStorage"/> 操作的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        private (AdoGrainKey GrainKey, string GrainType) GetStorageKey(string stateName, GrainId grainId)
        {
            if (options.KeyFormat == AdoNetGrainStorageKeyFormat.Legacy)
            {
                return (GrainIdAndExtensionAsString(grainId), ExtractBaseClass(stateName));
            }

            Debug.Assert(options.KeyFormat == AdoNetGrainStorageKeyFormat.GrainTypeQualified, "Options validation admits only defined key formats.");

            // 标识的各组成部分以其确切字节的十六进制文本形式存储。数据库排序规则可能
            // 忽略文本的大小写、重音符号或尾随空格，且 GrainIdHash/GrainTypeHash 可能发生
            // 哈希碰撞，因此每个不同标识的键列值都必须不同。十六进制数字在任何排序规则下均能精确比较，
            // 能保留无效 UTF-8 字节，并且绝不会包含分隔符“/”。
            // 完整键作为字符串键存储：整数列及 GUID 列不表示键种类。
            var grainType = $"{Convert.ToHexString(grainId.Type.AsSpan())}/{Convert.ToHexString(StrictUtf8.GetBytes(stateName))}";
            var grainKey = Convert.ToHexString(grainId.Key.AsSpan());
            if (grainType.Length > MaxKeyColumnLength || grainKey.Length > MaxKeyColumnLength)
            {
                throw new ArgumentException(
                    $"Grain {grainId} with state name \"{stateName}\" exceeds the {MaxKeyColumnLength}-character key columns of the {nameof(AdoNetGrainStorageKeyFormat.GrainTypeQualified)} storage key format, which stores grain types, state names, and grain keys as hexadecimal text.",
                    nameof(grainId));
            }

            return (new AdoGrainKey(grainKey), grainType);
        }

        /// <summary>
        /// 持久化脚本中 <c>GrainTypeString</c> 和 <c>GrainIdExtensionString</c> 列的长度。
        /// </summary>
        private const int MaxKeyColumnLength = 512;

        /// <summary>
        /// 编码状态名称时不替换无效 UTF-16 序列，因为替换会将不同名称映射为相同字节。
        /// </summary>
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// 提取字符串形式的 Grain ID；如果存在键扩展，则使用“#”作为中间分隔符追加键扩展。
        /// </summary>
        /// <returns>字符串形式的 Grain ID。</returns>
        /// <remarks>此功能可能应以更优化的形式存在于 Orleans 核心中。</remarks>
        private static AdoGrainKey GrainIdAndExtensionAsString(GrainId grainId)
        {
            string? keyExt;
            if (grainId.TryGetGuidKey(out var guid, out keyExt))
            {
                return new AdoGrainKey(guid, keyExt);
            }

            if (grainId.TryGetIntegerKey(out var integer, out keyExt))
            {
                return new AdoGrainKey(integer, keyExt);
            }

            return new AdoGrainKey(grainId.Key.ToString());
        }

        /// <summary>
        /// 从表示 <see cref="Type.AssemblyQualifiedName"/> 或
        /// <see cref="Type.FullName"/> 的字符串中提取基类名；如果未给出类型，则返回作为参数传入的类名。
        /// </summary>
        /// <param name="typeName">要传入的基类名称。</param>
        /// <returns>提取的基类名；如果不包含泛型部分，则返回作为参数传入的类名。</returns>
        private static string ExtractBaseClass(string typeName)
        {
            var genericPosition = typeName.IndexOf('`', StringComparison.Ordinal);
            if (genericPosition != -1)
            {
                // 以下实现要求泛型实参列表采用下述页面所描述的形式：
                // https://msdn.microsoft.com/en-us/library/w3f99sx1.aspx。
                var split = typeName.Split(BaseClassExtractionSplitDelimeters, StringSplitOptions.RemoveEmptyEntries);
                var stripped = new Queue<string>(split.Where(i => i.Length > 1 && i[0] != ',').Select(WithoutAssemblyVersion));

                return ReformatClassName(stripped);
            }

            return typeName;

            string WithoutAssemblyVersion(string input)
            {
                var asmNameIndex = input.IndexOf(',', StringComparison.Ordinal);
                if (asmNameIndex >= 0)
                {
                    var asmVersionIndex = input.IndexOf(',', asmNameIndex + 1);
                    if (asmVersionIndex >= 0) return input[..asmVersionIndex];
                    return input[..asmNameIndex];
                }

                return input;
            }

            string ReformatClassName(Queue<string> segments)
            {
                var simpleTypeName = segments.Dequeue();
                var arity = GetGenericArity(simpleTypeName);
                if (arity <= 0) return simpleTypeName;

                var args = new List<string>(arity);
                for (var i = 0; i < arity; i++)
                {
                    args.Add(ReformatClassName(segments));
                }

                return $"{simpleTypeName}[{string.Join(",", args.Select(arg => $"[{arg}]"))}]";
            }

            int GetGenericArity(string input)
            {
                var arityIndex = input.IndexOf('`', StringComparison.Ordinal);
                if (arityIndex != -1)
                {
                    return int.Parse(input.AsSpan()[(arityIndex + 1)..]);
                }

                return 0;
            }
        }

        private T CreateInstance<T>() => _activatorProvider.GetActivator<T>().Create();

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderClearing,
            Level = LogLevel.Trace,
            Message = "Clearing grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceClearingGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderDeleteError,
            Level = LogLevel.Error,
            Message = "Error clearing grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogErrorClearingGrainState(Exception exception, string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderCleared,
            Level = LogLevel.Trace,
            Message = "Cleared grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceClearedGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderReading,
            Level = LogLevel.Trace,
            Message = "Reading grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceReadingGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderNoStateFound,
            Level = LogLevel.Trace,
            Message = "Null grain state read (default will be instantiated): ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceNullGrainStateRead(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderRead,
            Level = LogLevel.Trace,
            Message = "Read grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceReadGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderReadError,
            Level = LogLevel.Error,
            Message = "Error reading grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogErrorReadingGrainState(Exception exception, string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderWriting,
            Level = LogLevel.Trace,
            Message = "Writing grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceWritingGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderWriteError,
            Level = LogLevel.Error,
            Message = "Error writing grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogErrorWritingGrainState(Exception exception, string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderWrote,
            Level = LogLevel.Trace,
            Message = "Wrote grain state: ServiceId={ServiceId} ProviderName={Name} GrainType={BaseGrainType} GrainId={GrainId} ETag={ETag}."
        )]
        private partial void LogTraceWroteGrainState(string serviceId, string name, string baseGrainType, AdoGrainKey grainId, string? etag);

        private readonly struct ConnectionStringLogRecord(string connectionString)
        {
            public override string ToString() => ConfigUtilities.RedactConnectionStringInfo(connectionString);
        }

        [LoggerMessage(
            EventId = (int)RelationalStorageProviderCodes.RelationalProviderInitProvider,
            Level = LogLevel.Information,
            Message = "Initialized storage provider: ServiceId={ServiceId} ProviderName={Name} Invariant={InvariantName} ConnectionString={ConnectionString}."
        )]
        private partial void LogInfoInitializedStorageProvider(string serviceId, string name, string invariantName, ConnectionStringLogRecord connectionString);
    }
}
