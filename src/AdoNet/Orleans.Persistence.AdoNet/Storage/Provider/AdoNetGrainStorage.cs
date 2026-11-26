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
    /// <see cref="AdoNetGrainStorage"/>使用的伐木编码。
    /// </summary>
    /// <remarks> 取自 <em>Orleans.Providers.ProviderErrorCode</em> 和 <em>Orleans.Providers.AzureProviderErrorCode</em>.</remarks>
    internal enum RelationalStorageProviderCodes
    {
        //这些是来自 Orleans.Providers.ProviderErrorCode 和 Orleans.Providers.AzureProviderErrorCode的。
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
    /// 创建来自注册服务和选项的 <see cref="AdoNetGrainStorage"/> 提供者。
    /// </summary>
    public static class AdoNetGrainStorageFactory
    {
        /// <summary>
        /// 创建有指定名称的 ADO.NET grain 存储提供者。
        /// </summary>
        /// <param name="services">用于解决依赖关系和指定选项的服务提供者。</param>
        /// <param name="name">存储提供者的名称。</param>
        /// <returns>配置 ADO.NET grain 储存提供者。</returns>
        public static AdoNetGrainStorage Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<AdoNetGrainStorageOptions>>();
            var clusterOptions = services.GetProviderClusterOptions(name);
            return ActivatorUtilities.CreateInstance<AdoNetGrainStorage>(services, Options.Create(optionsMonitor.Get(name)), name, clusterOptions);
        }
    }

    /// <summary>
    /// 仓库 grain 状态,在关系数据库中使用 ADO.NET.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配置通过 <see cref="AdoNetGrainStorageOptions"/>提供。
    /// </para>
    /// <para>
    /// 配置完全 <see cref="AdoNetGrainStorageOptions.ConnectionString"/> 或
    /// <see cref="AdoNetGrainStorageOptions.DataSource"/>.
    /// </para>
    /// <para>
    /// <see cref="AdoNetGrainStorageOptions.Invariant"/> 确定 ADO.NET 提供者和违约
    /// <c>Microsoft.Data.SqlClient</c>.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Name = {Name}, ConnectionString = {Storage.ConnectionString}")]
    public partial class AdoNetGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        /// <summary>
        /// 获取或设置用于转换 grain 状态的序列器,使其与存储的表示法相连接。
        /// </summary>
        public IGrainStorageSerializer Serializer { get; set; }

        /// <summary>
        /// BinaryFormatSerializer标记
        /// </summary>
        public const string BinaryFormatSerializerTag = "BinaryFormatSerializer";
        /// <summary>
        /// JsonFormatSerializer标记
        /// </summary>
        public const string JsonFormatSerializerTag = "JsonFormatSerializer";
        /// <summary>
        /// XmlFormatSerializer标记
        /// </summary>
        public const string XmlFormatSerializerTag = "XmlFormatSerializer";

        /// <summary>
        /// 使用该关系供应商的服务代号。
        /// </summary>
        private readonly string serviceId;
        private readonly IActivatorProvider _activatorProvider;
        private readonly ILogger logger;

        /// <summary>
        /// 后端操作使用的存储器。
        /// </summary>
        private IRelationalStorage Storage { get; set; } = null!;

        /// <summary>
        /// 这些字符是用来从类中提取类基类型时的分隔符
        /// 或 <see cref="Type.AssemblyQualifiedName"/> ,或 <see cref="Type.FullName"/>。
        /// <see cref="ExtractBaseClass(string)"/>.
        /// </summary>
        private static char[] BaseClassExtractionSplitDelimeters { get; } = new[] { '[', ']' };

        /// <summary>
        /// 从 Orleans 数据库初始化此结构的默认查询 。
        /// </summary>
        public const string DefaultInitializationQuery = "SELECT QueryKey, QueryText FROM OrleansQuery WHERE QueryKey = 'WriteToStorageKey' OR QueryKey = 'ReadFromStorageKey' OR QueryKey = 'ClearStorageKey' OR QueryKey = 'DeleteStorageKey'";

        /// <summary>
        /// 目前使用的查询。 更新后, 新查询将立即生效 。
        /// </summary>
        public RelationalStorageProviderQueries CurrentOperationalQueries { get; set; } = null!;

        /// <summary>
        /// 散列生成器用于按实键、 grain ID和 grain 类型进行小于指数的散列。
        /// </summary>
        public IStorageHasherPicker HashPicker { get; set; }

        private readonly AdoNetGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 初始化 <see cref="AdoNetGrainStorage"/> 类的新实例。
        /// </summary>
        /// <param name="activatorProvider">用于创建空 grain 状态值的激活器。</param>
        /// <param name="logger">供应商日志器。</param>
        /// <param name="options">储存提供者的选项。</param>
        /// <param name="clusterOptions">用于范围已储存的群集选项 grain 状态。</param>
        /// <param name="name">存储提供者的名称。</param>
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
        /// 将供应商的初始化和关闭与 silo 生命周期相连接的回调登记在册。
        /// </summary>
        /// <param name="lifecycle">下 silo 生命周期。</param>
        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(OptionFormattingUtilities.Name<AdoNetGrainStorage>(this.name), this.options.InitStage, Init, Close);
        }

        /// <summary>此存储提供者的清空状态数据函数。</summary>
        /// <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/>.
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
        public Task ClearStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => ClearStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
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
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 储存配置总是提供匹配的散列。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 储存配置总是提供匹配的散列。

                var queryText = options.DeleteStateOnClear ? CurrentOperationalQueries.DeleteState : CurrentOperationalQueries.ClearState;

                // Init的后向兼容性检查应该处理此案,然后在达到这一状态之前抛出。
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

            // 当确定在明确位置删除时,ETag应为无效,因为记录已删除。
            // DB 脚本返回删除的记录版本+1 storageVersion 排排 CheckVersionInconsistency 上文支票。
            //找不到错误, grain 所持有的国家版本可以更新,也可以更新状态。
            grainState.ETag = options.DeleteStateOnClear ? null : storageVersion;
            grainState.RecordExists = false;
            grainState.State = CreateInstance<T>();
            LogTraceClearedGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);
        }


        /// <summary> 为此存储提供者读取国家数据函数。</summary>
        /// <see cref="IGrainStorage.ReadStateAsync{T}(string, GrainId, IGrainState{T})"/>.
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
        public Task ReadStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => ReadStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
        public async Task ReadStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (grainId, baseGrainType) = GetStorageKey(grainType, grainReference);
            ArgumentNullException.ThrowIfNull(grainState);
            LogTraceReadingGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);

            try
            {
                var commandBehavior = CommandBehavior.Default;
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 储存配置总是提供匹配的散列。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 储存配置总是提供匹配的散列。
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


        /// <summary> 为此存储提供者写入状态数据函数 。</summary>
        /// <see cref="IGrainStorage.WriteStateAsync{T}(string, GrainId, IGrainState{T})"/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
        public Task WriteStateAsync<T>(string grainType, GrainId grainReference, IGrainState<T> grainState)
            => WriteStateAsync(grainType, grainReference, grainState, CancellationToken.None);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 无效。</exception>
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
                var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(grainId.GetHashBytes()); // 储存配置总是提供匹配的散列。
                var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState)!.Hash(Encoding.UTF8.GetBytes(baseGrainType)); // 储存配置总是提供匹配的散列。
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

            //找不到错误, grain 所持有的国家版本可以更新 。
            grainState.ETag = storageVersion;
            grainState.RecordExists = true;

            LogTraceWroteGrainState(serviceId, name, baseGrainType, grainId, grainState.ETag);
        }

        /// <summary> 该存储提供者的初始化功能。 </summary>
        private async Task Init(CancellationToken cancellationToken)
        {
            Storage = RelationalStorage.CreateInstance(options.Invariant, options.ConnectionString, options.DataSource);
            var queries = await Storage.ReadAsync(DefaultInitializationQuery, command => { }, (selector, resultSetCount, token) =>
            {
                return Task.FromResult(Tuple.Create(selector.GetValue<string>("QueryKey"), selector.GetValue<string>("QueryText")));
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            // 此检查是用于后向兼容性 :
            // 1. 有些 AdoNet 储存变量可能不支持删除,但可以明确删除。
            // 2. AdoNet 变量支持删除清晰但更新的持久性脚本尚未部署到管理 db。
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
        /// 关闭此提供者
        /// </summary>
        private Task Close(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 检查数据库脚本定义的版本不一致。
        /// </summary>
        /// <param name="serviceId">服务 Id.</param>
        /// <param name="providerName">此存储提供者的名称 。</param>
        /// <param name="operation">操作尝试。</param>
        /// <param name="storageVersion">存储版本。</param>
        /// <param name="grainVersion">下 grain 版本。</param>
        /// <param name="normalizedGrainType">Grain 类型没有通用信息。</param>
        /// <param name="grainId">下 grain ID.</param>
        /// <returns>扔或 <em>无效</em> 如果没有发现任何违规情况。</returns>
        /// <remarks>这意味着数据库中没有更新版本,或者版本存储版本不是空的
        /// 当 grain 版本为无效时,这意味着有效双倍激活并保存。</remarks>
        private static InconsistentStateException? CheckVersionInconsistency(string operation, string serviceId, string providerName, string? storageVersion, string? grainVersion, string normalizedGrainType, string grainId)
        {
            //如果这些是相同的,则表示没有在存储中插入行或更新。
            //实际上,这意味着由于ETag违规,更新或INSTERT的条件没有达到。
            //如果 grainState.ETag storageVersion 无效,而储存又以无效形式返回,
            //意思是两个 grains 被激活 另一个成功写出状态
            //
            //注意:存储器也可以返回新的和旧的ETag(Version),但目前没有。
            if (storageVersion == grainVersion || storageVersion is { Length: 0 } || (storageVersion is null && grainVersion is not null))
            {
                //TODO: 注意, 此错误消息应该跨后端的语法化 。
                return new InconsistentStateException($"Version conflict ({operation}): ServiceId={serviceId} ProviderName={providerName} GrainType={normalizedGrainType} GrainId={grainId} ETag={grainVersion}.");
            }

            return null;
        }

        /// <summary>
        /// 地图a grain 状态至 grain 键和 grain 键栏中存储的数值类型类型,由
        /// <see cref="AdoNetGrainStorageOptions.KeyFormat"/>.
        /// </summary>
        /// <param name="stateName">国名传给 <see cref="IGrainStorage"/> 操作。</param>
        /// <param name="grainId">下 grain ID.</param>
        private (AdoGrainKey GrainKey, string GrainType) GetStorageKey(string stateName, GrainId grainId)
        {
            if (options.KeyFormat == AdoNetGrainStorageKeyFormat.Legacy)
            {
                return (GrainIdAndExtensionAsString(grainId), ExtractBaseClass(stateName));
            }

            Debug.Assert(options.KeyFormat == AdoNetGrainStorageKeyFormat.GrainTypeQualified, "Options validation admits only defined key formats.");

            // 身份组成部分作为精确字节的十六进制文本存储。
            // 可比较文字 例数、口音或后方-对空间不敏感,以及 GrainIdHash/GrainTypeHash
            // 相撞,因此每个不同身份的键列必须有所不同。
            // 在任何核对中保存无效 UTF-8 的字节, 并且从不包含“ /” 分隔符 。
            // 整个键以字符串键存储:整数和图形与数字列不包含任何密钥类型。
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
        /// 时间长度 <c>GrainTypeString</c> 和 <c>GrainIdExtensionString</c> 列中,以耐久脚本的形式。
        /// </summary>
        private const int MaxKeyColumnLength = 512;

        /// <summary>
        /// 编码国名,但不替换无效的 UTF-16, 它将将不同的名称映射为相同的字节。
        /// </summary>
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// 将 grain ID作为字符串提取出来,并附加关键扩展名,其中显示“#” 的字条。
        /// </summary>
        /// <returns> grain ID作为字符串。</returns>
        /// <remarks>这很可能存在于 Orleans 核心中, 以更优化的形式存在。</remarks>
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
        /// 从 <see cref="Type.AssemblyQualifiedName"/> 或
        /// <see cref="Type.FullName"/> 或返回未给定类型时作为参数的参数。
        /// </summary>
        /// <param name="typeName">给定的基类名称。</param>
        /// <returns>提取的基准等级或如果没有通用部分,则作为参数给定的基准等级。</returns>
        private static string ExtractBaseClass(string typeName)
        {
            var genericPosition = typeName.IndexOf('`', StringComparison.Ordinal);
            if (genericPosition != -1)
            {
                //以下依据通用参数表,其格式如下:
                //年 https://msdn.microsoft.com/en-us/library/w3f99sx1.aspx.
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
