using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Grpc.Core;
using Orleans.Runtime;
using Orleans.Configuration;

namespace Orleans.GrainDirectory.Firestore;

/// <summary>
/// 提供由 Google Cloud Firestore 支持的 Grain 目录。
/// </summary>
public partial class FirestoreGrainDirectory : IGrainDirectory, ILifecycleParticipant<ISiloLifecycle>
{
    private const int MAX_IN_FILTER = 10;
    private const string DIRECTORY_GROUP = "GrainDirectory";
    private readonly string _clusterId;
    private readonly ILogger _logger;
    private readonly FirestoreDataManager _dataManager;

    /// <summary>
    /// 初始化 <see cref="FirestoreGrainDirectory"/> 类的新实例。
    /// </summary>
    /// <param name="clusterOptions">集群选项。</param>
    /// <param name="firestoreOptions">Google Cloud Firestore 选项。</param>
    /// <param name="loggerFactory">日志记录器工厂。</param>
    public FirestoreGrainDirectory(
        IOptions<ClusterOptions> clusterOptions,
        IOptions<FirestoreOptions> firestoreOptions,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(clusterOptions);
        ArgumentNullException.ThrowIfNull(firestoreOptions);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this._clusterId = clusterOptions.Value.ClusterId;
        this._logger = loggerFactory.CreateLogger<FirestoreGrainDirectory>();

        this._dataManager = new FirestoreDataManager(
            DIRECTORY_GROUP,
            Utils.SanitizeId(this._clusterId),
            firestoreOptions.Value,
            loggerFactory.CreateLogger<FirestoreDataManager>());
    }

    /// <summary>
    /// 查找指定 Grain 已注册的激活实例。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <returns>已注册的 Grain 地址；如果没有注册，则为 <see langword="null"/>。</returns>
    public Task<GrainAddress?> Lookup(GrainId grainId) => Lookup(grainId, CancellationToken.None);

    Task<GrainAddress?> IGrainDirectory.Lookup(GrainId grainId, CancellationToken cancellationToken) =>
        Lookup(grainId, cancellationToken);

    private async Task<GrainAddress?> Lookup(GrainId grainId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await this._dataManager
                .ReadEntity<GrainDirectoryEntity>(Utils.SanitizeGrainId(grainId), cancellationToken)
                .ConfigureAwait(false);

            return result is null ? null : GetGrainAddress(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLookupError(ex, grainId);
            throw;
        }
    }

    /// <summary>
    /// 如果指定 Grain 尚无注册记录，则注册其激活实例。
    /// </summary>
    /// <param name="address">要注册的 Grain 地址。</param>
    /// <returns>在目录中注册的 Grain 地址。</returns>
    public Task<GrainAddress?> Register(GrainAddress address) =>
        Register(address, previousAddress: null, cancellationToken: CancellationToken.None);

    Task<GrainAddress?> IGrainDirectory.Register(GrainAddress address, CancellationToken cancellationToken) =>
        Register(address, previousAddress: null, cancellationToken: cancellationToken);

    Task<GrainAddress?> IGrainDirectory.Register(GrainAddress address, GrainAddress? previousAddress) =>
        Register(address, previousAddress, CancellationToken.None);

    Task<GrainAddress?> IGrainDirectory.Register(
        GrainAddress address,
        GrainAddress? previousAddress,
        CancellationToken cancellationToken) =>
        Register(address, previousAddress, cancellationToken);

    private async Task<GrainAddress?> Register(
        GrainAddress address,
        GrainAddress? previousAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (previousAddress is not null)
        {
            await Unregister(previousAddress, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var entry = ConvertToEntity(address);
            await this._dataManager.CreateEntity(entry, cancellationToken).ConfigureAwait(false);
            return address;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            var result = await Lookup(address.GrainId, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRegisterError(ex, address.ActivationId, address.GrainId);
            throw;
        }
    }

    /// <summary>
    /// 当存储中的激活实例标识与给定地址匹配时，移除该 Grain 激活实例的注册。
    /// </summary>
    /// <param name="address">要移除的 Grain 地址。</param>
    /// <returns>表示该操作的任务。</returns>
    public Task Unregister(GrainAddress address) => Unregister(address, CancellationToken.None);

    Task IGrainDirectory.Unregister(GrainAddress address, CancellationToken cancellationToken) =>
        Unregister(address, cancellationToken);

    private async Task Unregister(GrainAddress address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            var found = await this._dataManager
                .ReadEntity<GrainDirectoryEntity>(Utils.SanitizeGrainId(address.GrainId), cancellationToken)
                .ConfigureAwait(false);

            if (found is null) return;

            if (found.ActivationId == address.ActivationId.ToParsableString())
            {
                await this._dataManager
                    .DeleteEntity(found.Id, Utils.FormatTimestamp(found.ETag!.Value), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnregisterError(ex, address.ActivationId, address.GrainId);
            throw;
        }
    }

    /// <summary>
    /// 移除注册到指定 Silo 的所有 Grain 激活实例的注册。
    /// </summary>
    /// <param name="siloAddresses">要移除其注册记录的 Silo 地址。</param>
    /// <returns>表示该操作的任务。</returns>
    public Task UnregisterSilos(List<SiloAddress> siloAddresses) =>
        UnregisterSilos(siloAddresses, CancellationToken.None);

    Task IGrainDirectory.UnregisterSilos(
        List<SiloAddress> siloAddresses,
        CancellationToken cancellationToken) =>
        UnregisterSilos(siloAddresses, cancellationToken);

    private async Task UnregisterSilos(
        List<SiloAddress> siloAddresses,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = new List<GrainDirectoryEntity>();

            var silos = siloAddresses.Select(s => s.ToParsableString()).ToArray();

            foreach (var chunk in silos.Chunk(MAX_IN_FILTER))
            {
                var found = await this._dataManager.QueryEntities<GrainDirectoryEntity>(
                    entity => entity
                        .WhereIn(nameof(GrainDirectoryEntity.SiloAddress), chunk),
                    cancellationToken).ConfigureAwait(false);

                entities.AddRange(found);
            }

            if (entities.Count > 0)
            {
                foreach (var chunk in entities.Chunk(FirestoreDataManager.MaxBatchSize))
                {
                    await Task.WhenAll(chunk.Select(entity =>
                        this._dataManager.DeleteEntity(
                            entity.Id,
                            Utils.FormatTimestamp(entity.ETag!.Value),
                            cancellationToken))).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnregisterSilosError(ex, string.Join('|', siloAddresses));
            throw;
        }
    }

    private static GrainDirectoryEntity ConvertToEntity(GrainAddress address)
    {
        return new GrainDirectoryEntity
        {
            Id = Utils.SanitizeGrainId(address.GrainId),
            SiloAddress = address.SiloAddress!.ToParsableString(),
            ActivationId = address.ActivationId.ToParsableString(),
            MembershipVersion = address.MembershipVersion.Value,
        };
    }

    private static GrainAddress GetGrainAddress(GrainDirectoryEntity entity)
    {
        return new GrainAddress
        {
            GrainId = Utils.ParseGrainId(entity.Id),
            SiloAddress = SiloAddress.FromParsableString(entity.SiloAddress),
            ActivationId = ActivationId.FromParsableString(entity.ActivationId),
            MembershipVersion = new MembershipVersion(entity.MembershipVersion)
        };
    }

    /// <summary>
    /// 将 Grain 目录订阅到 Silo 生命周期。
    /// </summary>
    /// <param name="lifecycle">Silo 生命周期。</param>
    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(nameof(FirestoreGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, Init);

    private async Task Init(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            LogInitializing();


            await this._dataManager.Initialize(ct);

            LogInitialized(sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogInitializationError(ex, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            sw.Stop();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unable to lookup activation for grain {GrainId} from Firestore")]
    private partial void LogLookupError(Exception exception, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unable to register activation {Activation} for grain {GrainId} in Firestore")]
    private partial void LogRegisterError(Exception exception, ActivationId activation, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unable to unregister activation {Activation} for grain {GrainId} in Firestore")]
    private partial void LogUnregisterError(Exception exception, ActivationId activation, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unable to unregister silos | {SiloAddresses} | in Firestore")]
    private partial void LogUnregisterSilosError(Exception exception, string siloAddresses);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initializing Google Firestore Grain Directory...")]
    private partial void LogInitializing();

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initialized Google Firestore Grain Directory in {ElapsedMilliseconds}ms.")]
    private partial void LogInitialized(long elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error initializing Google Firestore Grain Directory in {ElapsedMilliseconds}ms.")]
    private partial void LogInitializationError(Exception exception, long elapsedMilliseconds);
}
