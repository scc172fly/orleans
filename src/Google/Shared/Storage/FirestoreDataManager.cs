using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using Google.Api.Gax;
using Google.Cloud.Firestore;


#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.Firestore;
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.Firestore;
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.Firestore;
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.Firestore;
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif

internal partial class FirestoreDataManager
{
    internal const int MaxBatchSize = 500;
    private readonly FirestoreOptions _options;
    private readonly FirestoreDb _db;
    private readonly string _group;
    private readonly string _partition;
    private readonly ILogger _logger;

    public FirestoreDataManager(string group, string partition, FirestoreOptions options, ILogger logger)
    {
        this._group = group ?? throw new ArgumentNullException(nameof(group));
        this._partition = partition ?? throw new ArgumentNullException(nameof(partition));
        this._options = options ?? throw new ArgumentNullException(nameof(options));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this._db = !string.IsNullOrWhiteSpace(this._options.EmulatorHost)
            ? new FirestoreDbBuilder
            {
                ProjectId = this._options.ProjectId,
                Endpoint = GetEmulatorEndpoint(this._options.EmulatorHost),
                ChannelCredentials = ChannelCredentials.Insecure
            }.Build()
            : FirestoreDb.Create(this._options.ProjectId);
    }

    /// <summary>
    /// 初始化数据管理器 。
    /// </summary>
    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        LogInitializing();

        try
        {
            var group = this._db.Collection(this._options.RootCollectionName).Document(this._group);

            var snapshot = await ExecuteWithCancellation(group.GetSnapshotAsync(cancellationToken), cancellationToken);

            if (!snapshot.Exists)
            {
                // 创建页眉文档,以确保子收藏可以在以后创建
                await ExecuteWithCancellation(
                    group.CreateAsync(new { StorageGroup = this._group }, cancellationToken),
                    cancellationToken);
            }
        }
        catch (RpcException ex)
        {
            if (ex.StatusCode != StatusCode.AlreadyExists)
            {
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.Initialize), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 清除收藏。
    /// </summary>
    public async Task<int> ClearCollection(CancellationToken cancellationToken = default)
    {
        var collection = this.GetCollection();
        var colSnapshot = await ExecuteWithCancellation(collection.GetSnapshotAsync(cancellationToken), cancellationToken);

        if (colSnapshot.Count == 0) return 0;

        foreach (var chunk in colSnapshot.Documents.Chunk(MaxBatchSize))
        {
            var batch = this._db.StartBatch();

            foreach (var doc in chunk)
            {
                batch.Delete(doc.Reference);
            }

            await ExecuteWithCancellation(batch.CommitAsync(cancellationToken), cancellationToken);
        }

        return colSnapshot.Count;
    }

    /// <summary>
    /// 如果一个实体不存在, 创建它的实体, 否则它会扔
    /// </summary>
    /// <param name="entity">实体</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>实体的 eTag</returns>
    public async Task<string> CreateEntity<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogEntityOperation("Creating", entity.Id, this._partition);

        try
        {
            ValidateEntity(entity);

            var docRef = collection.Document(entity.Id);
            var result = await ExecuteWithCancellation(docRef.CreateAsync(entity, cancellationToken), cancellationToken);

            return Utils.FormatTimestamp(result.UpdateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.CreateEntity), this._partition);
            throw;
        }
    }

    public async Task<string> UpsertEntity<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogEntityOperation("Upserting", entity.Id, this._partition);

        try
        {
            ValidateEntity(entity);

            var docRef = collection.Document(entity.Id);

            var result = await ExecuteWithCancellation(
                docRef.SetAsync(entity, SetOptions.Overwrite, cancellationToken),
                cancellationToken);

            return Utils.FormatTimestamp(result.UpdateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.UpsertEntity), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 更新实体。
    /// </summary>
    /// <param name="entity">实体</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>实体的 eTag</returns>
    public async Task<string> Update<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogEntityOperation("Updating", entity.Id, this._partition);

        try
        {
            ValidateEntity(entity, true);

            var docRef = collection.Document(entity.Id);

            var result = await ExecuteWithCancellation(
                docRef.UpdateAsync(entity.GetFields(), Precondition.LastUpdated(entity.ETag!.Value), cancellationToken),
                cancellationToken);
            return Utils.FormatTimestamp(result.UpdateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.Update), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 更新现有实体,而不检查其目前的ETag。
    /// </summary>
    /// <param name="entity">实体。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>实体的新ETag。</returns>
    public async Task<string> UpdateUnconditionally<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogUnconditionalEntityUpdate(entity.Id, this._partition);

        try
        {
            ValidateEntity(entity);

            var docRef = collection.Document(entity.Id);
            var result = await ExecuteWithCancellation(
                docRef.UpdateAsync(entity.GetFields(), Precondition.MustExist, cancellationToken),
                cancellationToken);
            return Utils.FormatTimestamp(result.UpdateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.UpdateUnconditionally), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 删除一个实体。
    /// </summary>
    /// <param name="id">实体的识别号</param>
    /// <param name="eTag">实体的 eTag</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>删除是否成功完成 。</returns>
    public async Task<bool> DeleteEntity(string id, string? eTag = null, CancellationToken cancellationToken = default)
    {
        var collection = this.GetCollection();
        LogEntityOperation("Deleting", id, this._partition);

        try
        {
            var docRef = collection.Document(id);

            if (!string.IsNullOrWhiteSpace(eTag) && eTag != "*")
            {
                await ExecuteWithCancellation(
                    docRef.DeleteAsync(Precondition.LastUpdated(Utils.ParseTimestamp(eTag)), cancellationToken),
                    cancellationToken);
            }
            else
            {
                await ExecuteWithCancellation(
                    docRef.DeleteAsync(Precondition.MustExist, cancellationToken),
                    cancellationToken);
            }
            return true;
        }
        catch (RpcException ex)
        {
            if (ex.StatusCode == StatusCode.FailedPrecondition || ex.StatusCode == StatusCode.NotFound)
            {
                return false;
            }
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.DeleteEntity), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 阅读实体。
    /// </summary>
    /// <param name="id">实体的识别号</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>实体或不存在的无效</returns>
    public async Task<TEntity?> ReadEntity<TEntity>(string id, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogEntityOperation("Reading", id, this._partition);

        try
        {
            var docRef = collection.Document(id);

            var snapshot = await ExecuteWithCancellation(docRef.GetSnapshotAsync(cancellationToken), cancellationToken);

            if (!snapshot.Exists)
            {
                LogEntityNotFound(id, this._partition);

                return null;
            }

            var entity = snapshot.ConvertTo<TEntity>();

            return entity;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.ReadEntity), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 阅读所有实体。
    /// </summary>
    /// <returns>实体</returns>
    public async Task<TEntity[]> ReadAllEntities<TEntity>(CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogCollectionOperation("Reading all entities from", this._partition);

        try
        {
            var snapshot = await ExecuteWithCancellation(collection.GetSnapshotAsync(cancellationToken), cancellationToken);

            if (snapshot.Count == 0)
            {
                LogNoEntitiesFound(this._partition);

                return Array.Empty<TEntity>();
            }

            return snapshot.Documents.Select(d => d.ConvertTo<TEntity>()).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.ReadAllEntities), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 删除分区中的实体 。
    /// </summary>
    /// <param name="entities">实体待删除</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>代表操作的任务。</returns>
    public async Task DeleteEntities<TEntity>(TEntity[] entities, CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();

        LogCollectionOperation("Deleting entities from", this._partition);

        if (entities.Length == 0) return;

        try
        {
            if (entities.Length > MaxBatchSize)
                throw new ArgumentOutOfRangeException(nameof(entities), $"Batch operation limit exceeded ({MaxBatchSize}).");

            var batch = this._db.StartBatch();

            foreach (var entity in entities)
            {
                ValidateEntity(entity, true);

                var docRef = collection.Document(entity.Id);

                batch.Delete(docRef, Precondition.LastUpdated(entity.ETag!.Value));
            }

            await ExecuteWithCancellation(batch.CommitAsync(cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.DeleteEntities), this._partition);
            throw;
        }
    }

    /// <summary>
    /// 查询实体。
    /// </summary>
    /// <param name="query">查询过滤器</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>实体阵列</returns>
    public async Task<TEntity[]> QueryEntities<TEntity>(
        Func<CollectionReference, Query> query,
        CancellationToken cancellationToken = default) where TEntity : FirestoreEntity, new()
    {
        var collection = this.GetCollection();
        LogCollectionOperation("Querying entities from", this._partition);

        try
        {
            var snapshot = await ExecuteWithCancellation(
                query(collection).GetSnapshotAsync(cancellationToken),
                cancellationToken);

            if (snapshot.Count == 0)
            {
                LogNoEntitiesFound(this._partition);

                return Array.Empty<TEntity>();
            }

            return snapshot.Documents.Select(d => d.ConvertTo<TEntity>()).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationError(ex, nameof(this.QueryEntities), this._partition);
            throw;
        }
    }

    public Task<TEntity> ExecuteTransaction<TEntity>(
        Func<Transaction, Task<TEntity>> transactionScope,
        CancellationToken cancellationToken = default) =>
        ExecuteWithCancellation(
            this._db.RunTransactionAsync(transactionScope, options: null, cancellationToken: cancellationToken),
            cancellationToken);

    public async Task<bool> EntityExists(string id, CancellationToken cancellationToken = default)
    {
        var document = this.GetCollection().Document(id);
        var snapshot = await ExecuteWithCancellation(document.GetSnapshotAsync(cancellationToken), cancellationToken);
        return snapshot.Exists;
    }

    private static void ValidateEntity<TEntity>(TEntity entity, bool updating = false) where TEntity : FirestoreEntity, new()
    {
        if (string.IsNullOrWhiteSpace(entity.Id)) throw new InvalidOperationException("Id is required to create or update an entity");
        if (updating)
        {
            if (!entity.ETag.HasValue) throw new InvalidOperationException("ETag is required to update an entity");
            if (entity.ETag.Value.ToDateTimeOffset() < DateTimeOffset.UnixEpoch) throw new InvalidOperationException("ETag must be greater than 1970-01-01T00:00:00Z");
        }
    }

    private static async Task ExecuteWithCancellation(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task;
        }
        catch (RpcException exception) when (
            cancellationToken.IsCancellationRequested && exception.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException("The Firestore operation was canceled.", exception, cancellationToken);
        }
    }

    internal static async Task<TResult> ExecuteWithCancellation<TResult>(
        Task<TResult> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task;
        }
        catch (RpcException exception) when (
            cancellationToken.IsCancellationRequested && exception.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException("The Firestore operation was canceled.", exception, cancellationToken);
        }
    }

    public CollectionReference GetCollection() =>
        this._db.Collection($"{this._options.RootCollectionName}").Document(this._group).Collection(this._partition);

    private static string GetEmulatorEndpoint(string endpoint) =>
        endpoint.Contains("://", StringComparison.Ordinal)
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && !string.IsNullOrEmpty(uri.Authority)
            ? uri.Authority
            : endpoint;

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initializing FirestoreDataManager")]
    private partial void LogInitializing();

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{Operation} entity {Id} on collection {Collection}")]
    private partial void LogEntityOperation(string operation, string id, string collection);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Updating entity {Id} on collection {Collection} without an ETag")]
    private partial void LogUnconditionalEntityUpdate(string id, string collection);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Entity {Id} not found on collection {Collection}")]
    private partial void LogEntityNotFound(string id, string collection);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{Operation} collection {Collection}")]
    private partial void LogCollectionOperation(string operation, string collection);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No entities found on collection {Collection}")]
    private partial void LogNoEntitiesFound(string collection);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error on {Operation} on collection {Collection}")]
    private partial void LogOperationError(Exception exception, string operation, string collection);
}
