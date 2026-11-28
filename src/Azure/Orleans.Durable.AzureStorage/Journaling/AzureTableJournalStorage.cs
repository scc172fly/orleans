using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Buffers;
using Orleans.Storage;

namespace Orleans.Durable.Journaling;

internal sealed partial class AzureTableJournalStorage : IJournalStorage
{
    // 每份日志对应一个表分区。头部行保存恢复清单，同时作为
    // 乐观并发控制屏障：每次修改都在其 ETag 的保护下，在发布数据行的同一个实体组
    // 事务中更新头部行。
    internal const string HeaderRowKey = "$header";

    // 头部行使用此属性声明数据行采用的日志格式。
    internal const string FormatPropertyName = "Format";

    // 头部行保存规范日志 ID，使目录列表查询不依赖分区键映射。
    internal const string JournalIdPropertyName = "JournalId";

    // 头部行使用此属性将恢复过程指向当前世代的数据行。
    // 世代标识随机生成，因此替换失败产生的孤立行绝不会与后续世代的行发生冲突，
    // 也不会被误认为后续世代的行。
    internal const string GenerationPropertyName = "Generation";

    // 头部行使用此属性记录当前世代包含的数据行数。
    internal const string RowCountPropertyName = "RowCount";

    // 头部行使用此属性记录当前世代包含的日志字节数。
    internal const string LengthPropertyName = "Length";

    // 头部行独立于当前世代清单跟踪追加活动，避免大型
    // 替换快照立即触发下一次压实请求。
    internal const string AppendRowCountPropertyName = "AppendRowCount";
    internal const string AppendLengthPropertyName = "AppendLength";

    // 头部行将调用方所有的元数据作为 JSON 对象存储在此属性中，使调用方的键
    // 永远不会与提供程序所有的头部行属性冲突。
    internal const string MetadataPropertyName = "Metadata";

    // AppendAsync 将全部数据行和头部行屏障写入同一个实体组事务，
    // 因此在对二进制属性进行 Base64 编码之后，有效载荷必须显著低于
    // Azure 的 4 MiB 事务上限。
    internal const long MaxAppendBytes = 2L * 1024 * 1024;

    // Azure Table 的每个二进制属性最多容纳 64 KiB。
    private const int ChunkBytes = 64 * 1024;

    // 使用 15 个分块，使每个数据行低于 Azure 的 1 MiB 实体上限，并为键和其他开销预留空间。
    private const int MaxChunksPerEntity = 15;

    // Azure 实体组事务最多接受 100 个实体。
    private const int MaxEntitiesPerTransaction = 100;

    private static readonly string[] ChunkPropertyNames = CreateChunkPropertyNames();
    private static readonly string[] RowKeySelect = [nameof(ITableEntity.RowKey)];

    private readonly AzureTableJournalStorageShared _shared;
    private readonly JournalId _journalId;
    private readonly string _partitionKey;
    private TableClient? _tableClient;
    // 读取或创建之前为 null；日志不存在时为 default(ETag)。
    private ETag? _headerETag;
    private HeaderProviderState _headerProviderState;

    private bool HeaderExists => _headerETag is { } eTag && eTag != default;

    public bool IsCompactionRequested
        => _headerProviderState.AppendRowCount >= _shared.Options.CompactionRowCountThreshold
            || _headerProviderState.AppendLength >= _shared.Options.CompactionSizeThreshold;

    internal AzureTableJournalStorage(
        AzureTableJournalStorageShared shared,
        JournalId journalId)
    {
        ArgumentNullException.ThrowIfNull(shared);
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        _shared = shared;
        _journalId = journalId;
        _partitionKey = shared.Options.GetPartitionKeyForJournal(journalId);
    }

    private TableClient Table => _tableClient ??= GetTableClient();

    public async ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        var callerMetadata = CopyAndValidateCallerMetadata(metadata);
        try
        {
            var created = await CreateHeaderAsync(callerMetadata, cancellationToken).ConfigureAwait(false);
            SetHeader(created.ETag, created.ProviderState);
            succeeded = true;
            return true;
        }
        catch (RequestFailedException exception) when (IsEntityAlreadyExists(exception))
        {
            var existing = await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            ValidateHeaderJournalId(existing);
            succeeded = true;
            return false;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationCreate,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes: 0,
                succeeded);
        }
    }

    public async ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var entity = await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false);
            succeeded = true;
            return entity is null ? null : CreateJournalMetadata(entity.ETag, entity);
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationGetMetadata,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes: 0,
                succeeded);
        }
    }

    public async ValueTask<IJournalMetadata?> UpdateMetadataAsync(
        IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        var setValues = CopyAndValidateCallerMetadata(set);
        var removeValues = CopyRemove(remove, setValues);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    _shared.Instruments.OnRetry("metadata_conflict");
                }

                var entity = await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false);
                if (entity is null)
                {
                    succeeded = true;
                    return null;
                }

                if (expectedETag is not null && ToAzureETag(expectedETag) != entity.ETag)
                {
                    succeeded = true;
                    return null;
                }

                var headerState = CreateHeaderState(entity);
                var metadata = headerState.CallerMetadata;
                if (!ApplyCallerMetadataUpdate(metadata, setValues, removeValues))
                {
                    RefreshHeaderETag(headerState.ETag, headerState.ProviderState);
                    succeeded = true;
                    return CreateJournalMetadata(entity.ETag, entity);
                }

                var patch = new TableEntity(_partitionKey, HeaderRowKey)
                {
                    [JournalIdPropertyName] = _journalId.Value,
                    [AppendRowCountPropertyName] = headerState.ProviderState.AppendRowCount,
                    [AppendLengthPropertyName] = headerState.ProviderState.AppendLength,
                    [MetadataPropertyName] = SerializeCallerMetadata(metadata),
                };

                try
                {
                    var response = await Table.UpdateEntityAsync(
                        patch,
                        expectedETag is null ? entity.ETag : ToAzureETag(expectedETag),
                        TableUpdateMode.Merge,
                        cancellationToken).ConfigureAwait(false);
                    var updatedETag = GetResponseETag(response);
                    RefreshHeaderETag(updatedETag, headerState.ProviderState);
                    succeeded = true;
                    return new JournalMetadata(
                        headerState.ProviderState.Format,
                        updatedETag == default ? null : updatedETag.ToString(),
                        metadata);
                }
                catch (RequestFailedException exception) when (exception.Status is 412)
                {
                    if (expectedETag is not null)
                    {
                        succeeded = true;
                        return null;
                    }
                }
            }

            succeeded = true;
            return null;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationUpdateMetadata,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes: 0,
                succeeded);
        }
    }

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value.Length, nameof(value));
        // 追加操作作为一个实体组事务写入，因此先验证其限制，再访问存储。
        ThrowIfBatchTooLarge(value.Length);
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnsureHeaderAsync(nameof(AppendAsync), cancellationToken).ConfigureAwait(false);

                var expectedETag = _headerETag!.Value;
                var expectedProviderState = _headerProviderState;
                var generation = expectedProviderState.Generation
                    ?? throw new InvalidOperationException("Azure Table journal header state does not include a generation.");
                var entities = CreateDataEntities(value, generation, firstSequence: expectedProviderState.RowCount);
                var newProviderState = expectedProviderState with
                {
                    RowCount = checked(expectedProviderState.RowCount + entities.Count),
                    Length = checked(expectedProviderState.Length + value.Length),
                    AppendRowCount = checked(expectedProviderState.AppendRowCount + entities.Count),
                    AppendLength = checked(expectedProviderState.AppendLength + value.Length),
                };

                // 使用最近观测到的头部行 ETag 保护整个批次，如果日志自本实例恢复以来
                // 发生变化，追加操作便会失败。
                var actions = new List<TableTransactionAction>(entities.Count + 1)
                {
                    new(TableTransactionActionType.UpdateMerge, CreateHeaderCountsPatch(newProviderState), expectedETag),
                };
                foreach (var entity in entities)
                {
                    actions.Add(new(TableTransactionActionType.Add, entity));
                }

                try
                {
                    var result = await Table.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);

                    LogAppend(_shared.Logger, value.Length, entities.Count, Table.Name, _partitionKey);

                    // 缓存追加后的状态，使下一次修改受到新头部行 ETag 的保护。
                    SetHeaderFromResponse(result.Value[0], newProviderState);
                    succeeded = true;
                    return;
                }
                catch (RequestFailedException exception) when (IsHeaderMutationConflict(exception))
                {
                    var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                        ? await RetryAfterMetadataOnlyConflictAsync(attempt, expectedProviderState, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (refreshed is not null)
                    {
                        _shared.Instruments.OnRetry("metadata_only_conflict");
                        continue;
                    }

                    throw CreateInconsistentHeaderStateException(
                        "Azure Table journal header changed while appending; recovery is required.",
                        expectedETag,
                        exception);
                }
            }
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationAppend,
                Stopwatch.GetElapsedTime(startTimestamp),
                value.Length,
                succeeded);
        }
    }

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            if (_headerETag is null)
            {
                throw new InvalidOperationException($"{nameof(DeleteAsync)} requires reading or creating the journal first.");
            }

            var expectedETag = _headerETag.Value;
            var expectedProviderState = _headerProviderState;
            if (!HeaderExists)
            {
                // 此前观测到日志不存在的实例不删除任何内容，除非此后其他写入方已创建该日志。
                if (await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false) is not null)
                {
                    throw CreateInconsistentHeaderStateException(
                        "Azure Table journal header was created by another writer after this instance observed it absent; recovery is required.",
                        expectedETag);
                }

                succeeded = true;
                return;
            }

            // 加载头部行，使删除操作受到此实例所持有的清单保护。
            var headerState = await TryLoadHeaderStateForMutationAsync(expectedETag, expectedProviderState, cancellationToken).ConfigureAwait(false);
            if (headerState is null)
            {
                throw CreateInconsistentHeaderStateException(
                    "Azure Table journal header changed while deleting the journal; recovery is required.",
                    expectedETag);
            }

            // 删除头部行之前捕获行键，以确保清理绝不会触及由并发重建日志的实例写入的行：
            // 重建的日志使用新的
            // 随机世代标识，因此其数据行绝不会出现在此快照中。
            var rowKeys = await TryCollectRowKeysAsync(CreateAllRowsFilter(), cancellationToken).ConfigureAwait(false);

            for (var attempt = 0; ; attempt++)
            {
                var deleteState = headerState.Value;
                try
                {
                    // 先在头部行的 ETag 保护下删除头部行，再清理数据行，避免并发日志更新导致数据丢失。
                    await Table.DeleteEntityAsync(_partitionKey, HeaderRowKey, deleteState.ETag, cancellationToken).ConfigureAwait(false);
                    SetHeader(eTag: default, providerState: default);
                    break;
                }
                catch (RequestFailedException exception) when (IsHeaderMutationConflict(exception))
                {
                    var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                        ? await RetryAfterMetadataOnlyConflictAsync(attempt, deleteState.ProviderState, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (refreshed is { } refreshedState)
                    {
                        headerState = refreshedState;
                        _shared.Instruments.OnRetry("metadata_only_conflict");
                        continue;
                    }

                    throw CreateInconsistentHeaderStateException(
                        "Azure Table journal header changed while deleting the journal; recovery is required.",
                        deleteState.ETag,
                        exception);
                }
            }

            // 行清理发生在头部行删除之后，因为没有头部行便无法通过清单访问这些行。
            await TryDeleteRowsAsync(rowKeys, cancellationToken).ConfigureAwait(false);
            succeeded = true;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationDelete,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes: 0,
                succeeded);
        }
    }

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        var bytes = 0L;

        try
        {
            // 首先读取头部行，因为它是需要重放的世代的清单。
            var entity = await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false);
            if (entity is null)
            {
                if (HeaderExists)
                {
                    throw CreateInconsistentHeaderStateException(
                        "Azure Table journal changed while reading; recovery through a fresh storage instance is required.",
                        _headerETag!.Value);
                }

                consumer.Complete(metadata: null);
                SetHeader(eTag: default, providerState: default);
                succeeded = true;
                return;
            }

            var headerState = CreateHeaderState(entity);

            if (_headerETag is { } expectedETag
                && (!HeaderExists || headerState.ProviderState != _headerProviderState))
            {
                throw CreateInconsistentHeaderStateException(
                    "Azure Table journal changed while reading; recovery through a fresh storage instance is required.",
                    expectedETag);
            }

            var rowCount = 0L;
            var metadata = new JournalMetadata(headerState.ProviderState.Format, eTag: null, headerState.CallerMetadata);
            using var buffer = new ArcBufferWriter();
            if (headerState.ProviderState is { Generation: { Length: > 0 } generation, RowCount: > 0 and var expectedRowCount })
            {
                // 单次范围查询按序列顺序返回当前世代。查询上限
                // 来自头部行，因此不会读取另一实例并发追加的数据行。
                var filter = TableClient.CreateQueryFilter(
                    $"PartitionKey eq {_partitionKey} and RowKey ge {FormatRowKey(generation, 0)} and RowKey le {FormatRowKey(generation, expectedRowCount - 1)}");
                await foreach (var row in Table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    var expectedRowKey = FormatRowKey(generation, rowCount);
                    if (!string.Equals(row.RowKey, expectedRowKey, StringComparison.Ordinal))
                    {
                        throw CreateInconsistentHeaderStateException(
                            $"Azure Table journal row sequence is invalid: expected \"{expectedRowKey}\", found \"{row.RowKey}\"; recovery is required.",
                            headerState.ETag);
                    }

                    rowCount++;
                    bytes += ReadChunks(row, buffer, consumer, metadata);
                }
            }

            if (rowCount != headerState.ProviderState.RowCount || bytes != headerState.ProviderState.Length)
            {
                // 并发替换可能删除正在读取的世代中的数据行；调用方必须执行恢复。
                throw CreateInconsistentHeaderStateException(
                    "Azure Table journal changed while reading; recovery is required.",
                    headerState.ETag);
            }

            consumer.Read(new JournalBufferReader(buffer.Reader, isCompleted: true), metadata);
            if (buffer.Length > 0)
            {
                throw new InvalidOperationException("The journal storage consumer did not read all supplied journal data.");
            }

            LogRead(_shared.Logger, bytes, Table.Name, _partitionKey);
            SetHeader(headerState.ETag, headerState.ProviderState);
            succeeded = true;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationRead,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes,
                succeeded);
        }
    }

    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            // 压实通过头部行发布，因此首先恢复或创建将接受 ETag 检查的头部行。
            await EnsureHeaderAsync(nameof(ReplaceAsync), cancellationToken).ConfigureAwait(false);

            var expectedETag = _headerETag!.Value;
            var expectedProviderState = _headerProviderState;
            var headerState = await TryLoadHeaderStateForMutationAsync(expectedETag, expectedProviderState, cancellationToken).ConfigureAwait(false);
            if (headerState is null)
            {
                throw CreateInconsistentHeaderStateException(
                    "Azure Table journal header changed while replacing the journal; recovery is required.",
                    expectedETag);
            }

            var previousGeneration = _shared.Options.DeleteOldGenerations ? headerState.Value.ProviderState.Generation : null;

            // 在下面的头部行切换操作发布新世代之前，恢复过程无法看到它，因此其
            // 数据行可以跨越多个事务写入。
            var newGeneration = Guid.NewGuid().ToString("N");
            var publicationAttempted = false;
            try
            {
                var rowCount = await SubmitDataEntitiesAsync(value, newGeneration, cancellationToken).ConfigureAwait(false);

                for (var attempt = 0; ; attempt++)
                {
                    var publishState = headerState.Value;
                    try
                    {
                        // 在头部行的 ETag 保护下切换头部行，仅当日志未发生变化时发布新世代。
                        publicationAttempted = true;
                        var response = await Table.UpdateEntityAsync(
                            CreateHeaderFlipPatch(newGeneration, rowCount, value.Length),
                            publishState.ETag,
                            TableUpdateMode.Merge,
                            cancellationToken).ConfigureAwait(false);
                        SetHeaderFromResponse(
                            response,
                            new HeaderProviderState(
                                NormalizeFormat(_shared.JournalFormatKey),
                                newGeneration,
                                rowCount,
                                value.Length,
                                AppendRowCount: 0,
                                AppendLength: 0));
                        break;
                    }
                    catch (RequestFailedException exception) when (IsHeaderMutationConflict(exception))
                    {
                        var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                            ? await RetryAfterMetadataOnlyConflictAsync(attempt, publishState.ProviderState, cancellationToken).ConfigureAwait(false)
                            : null;
                        if (refreshed is { } refreshedState)
                        {
                            headerState = refreshedState;
                            _shared.Instruments.OnRetry("metadata_only_conflict");
                            continue;
                        }

                        throw CreateInconsistentHeaderStateException(
                            "Azure Table journal header changed while replacing the journal; recovery is required.",
                            publishState.ETag,
                            exception);
                    }
                }
            }
            catch
            {
                // 发布请求一旦发出，即使客户端发生故障，也可能仍然提交。必须保留其数据行。
                if (!publicationAttempted)
                {
                    await TryDeleteUnpublishedGenerationAsync(newGeneration).ConfigureAwait(false);
                }

                throw;
            }

            if (previousGeneration is not null && !string.Equals(previousGeneration, newGeneration, StringComparison.Ordinal))
            {
                // 在头部行切换发布之前保留上一世代，避免恢复指向缺失的数据行。
                var previousRowKeys = await TryCollectRowKeysAsync(CreateGenerationRangeFilter(previousGeneration), cancellationToken).ConfigureAwait(false);
                await TryDeleteRowsAsync(previousRowKeys, cancellationToken).ConfigureAwait(false);
            }

            LogReplace(_shared.Logger, newGeneration, value.Length, Table.Name, _partitionKey);
            succeeded = true;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureTableJournalStorageInstruments.OperationReplace,
                Stopwatch.GetElapsedTime(startTimestamp),
                value.Length,
                succeeded);
        }
    }

    private static void ThrowIfBatchTooLarge(long length)
    {
        // Azure 会拒绝过大的实体组事务，因此在本地提前失败，并给出日志存储专用的指导信息。
        if (length <= MaxAppendBytes)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Azure Table journal batch of {length:N0} bytes exceeds the per-transaction limit of {MaxAppendBytes:N0} bytes (2 MiB). " +
            "Reduce the operation size or compact more aggressively.");
    }

    private async ValueTask EnsureHeaderAsync(string operation, CancellationToken cancellationToken)
    {
        if (_headerETag is null)
        {
            throw new InvalidOperationException($"{operation} requires reading or creating the journal first.");
        }

        if (HeaderExists)
        {
            return;
        }

        try
        {
            // 此实例此前观测到日志不存在，因此仅在头部行仍不存在时创建它。
            var created = await CreateHeaderAsync(callerMetadata: null, cancellationToken).ConfigureAwait(false);
            SetHeader(created.ETag, created.ProviderState);
        }
        catch (RequestFailedException exception) when (IsEntityAlreadyExists(exception))
        {
            throw CreateInconsistentHeaderStateException(
                "Azure Table journal header was created by another writer after this instance observed it absent; recovery is required.",
                _headerETag.Value,
                exception);
        }
    }

    private async ValueTask<(ETag ETag, HeaderProviderState ProviderState)> CreateHeaderAsync(
        Dictionary<string, string>? callerMetadata,
        CancellationToken cancellationToken)
    {
        var generation = Guid.NewGuid().ToString("N");
        var entity = new TableEntity(_partitionKey, HeaderRowKey)
        {
            [FormatPropertyName] = _shared.JournalFormatKey ?? string.Empty,
            [JournalIdPropertyName] = _journalId.Value,
            [GenerationPropertyName] = generation,
            [RowCountPropertyName] = 0L,
            [LengthPropertyName] = 0L,
            [AppendRowCountPropertyName] = 0L,
            [AppendLengthPropertyName] = 0L,
            [MetadataPropertyName] = SerializeCallerMetadata(callerMetadata),
        };

        var response = await Table.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
        return (
            GetResponseETag(response),
            new HeaderProviderState(
                NormalizeFormat(_shared.JournalFormatKey),
                generation,
                RowCount: 0,
                Length: 0,
                AppendRowCount: 0,
                AppendLength: 0));
    }

    private async ValueTask<HeaderState?> TryLoadHeaderStateAsync(CancellationToken cancellationToken)
    {
        // 只读取头部行；确定修改状态不需要读取日志字节。
        var entity = await GetHeaderEntityAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : CreateHeaderState(entity);
    }

    private async ValueTask<HeaderState?> TryLoadHeaderStateForMutationAsync(
        ETag expectedETag,
        HeaderProviderState expectedProviderState,
        CancellationToken cancellationToken)
    {
        var headerState = await TryLoadHeaderStateAsync(cancellationToken).ConfigureAwait(false);
        if (headerState is null)
        {
            return null;
        }

        // 仅当变化局限于元数据时才就地接受新 ETag；内容变化要求执行恢复。
        if (headerState.Value.ETag != expectedETag && headerState.Value.ProviderState != expectedProviderState)
        {
            return null;
        }

        SetHeader(headerState.Value.ETag, headerState.Value.ProviderState);
        return headerState;
    }

    private async ValueTask<HeaderState?> RetryAfterMetadataOnlyConflictAsync(
        int attempt,
        HeaderProviderState expectedProviderState,
        CancellationToken cancellationToken)
    {
        var initial = _shared.Options.MetadataOnlyConflictInitialBackoff;
        if (initial > TimeSpan.Zero)
        {
            var max = _shared.Options.MetadataOnlyConflictMaxBackoff;
            if (max < initial)
            {
                max = initial;
            }

            var multiplier = 1L << Math.Min(attempt, 16);
            var scaledTicks = initial.Ticks > max.Ticks / multiplier
                ? max.Ticks
                : initial.Ticks * multiplier;
            var cappedTicks = Math.Min(scaledTicks, max.Ticks);
            await Task.Delay(TimeSpan.FromTicks(cappedTicks), cancellationToken).ConfigureAwait(false);
        }

        return await TryRefreshHeaderStateAfterMetadataOnlyConflictAsync(expectedProviderState, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<HeaderState?> TryRefreshHeaderStateAfterMetadataOnlyConflictAsync(
        HeaderProviderState expectedProviderState,
        CancellationToken cancellationToken)
    {
        if (expectedProviderState.Generation is null)
        {
            return null;
        }

        var headerState = await TryLoadHeaderStateAsync(cancellationToken).ConfigureAwait(false);
        if (headerState is null || headerState.Value.ProviderState != expectedProviderState)
        {
            return null;
        }

        SetHeader(headerState.Value.ETag, headerState.Value.ProviderState);
        return headerState;
    }

    private async ValueTask<TableEntity?> GetHeaderEntityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await Table.GetEntityAsync<TableEntity>(
                _partitionKey,
                HeaderRowKey,
                select: null,
                cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status is 404)
        {
            return null;
        }
    }

    private async ValueTask<long> SubmitDataEntitiesAsync(
        ReadOnlySequence<byte> value,
        string generation,
        CancellationToken cancellationToken)
    {
        var actions = new List<TableTransactionAction>(MaxEntitiesPerTransaction);
        var actionBytes = 0L;
        var remaining = value;
        var sequence = 0L;
        while (!remaining.IsEmpty)
        {
            var entity = CreateDataEntity(ref remaining, generation, sequence++);
            var entityBytes = GetEntityPayloadLength(entity);
            if (actions.Count > 0 && (actions.Count == MaxEntitiesPerTransaction || actionBytes + entityBytes > MaxAppendBytes))
            {
                await Table.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);
                actions.Clear();
                actionBytes = 0;
            }

            actions.Add(new(TableTransactionActionType.Add, entity));
            actionBytes += entityBytes;
        }

        if (actions.Count > 0)
        {
            await Table.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);
        }

        return sequence;
    }

    private async ValueTask TryDeleteUnpublishedGenerationAsync(string generation)
    {
        try
        {
            var rowKeys = await TryCollectRowKeysAsync(
                CreateGenerationRangeFilter(generation),
                CancellationToken.None).ConfigureAwait(false);
            await TryDeleteRowsAsync(rowKeys, CancellationToken.None).ConfigureAwait(false);
        }
        catch (RequestFailedException exception)
        {
            LogRowCleanupFailure(_shared.Logger, Table.Name, _partitionKey, exception);
        }
    }

    private async ValueTask<List<string>> TryCollectRowKeysAsync(string filter, CancellationToken cancellationToken)
    {
        var rowKeys = new List<string>();
        try
        {
            await foreach (var row in Table.QueryAsync<TableEntity>(filter, select: RowKeySelect, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (!string.Equals(row.RowKey, HeaderRowKey, StringComparison.Ordinal))
                {
                    rowKeys.Add(row.RowKey);
                }
            }
        }
        catch (RequestFailedException exception)
        {
            LogRowCleanupFailure(_shared.Logger, Table.Name, _partitionKey, exception);
        }

        return rowKeys;
    }

    private async ValueTask TryDeleteRowsAsync(List<string> rowKeys, CancellationToken cancellationToken)
    {
        try
        {
            // 过时数据行采用尽力而为的方式清理，因为已发布的头部行不再引用这些行。
            var actions = new List<TableTransactionAction>(Math.Min(rowKeys.Count, MaxEntitiesPerTransaction));
            foreach (var rowKey in rowKeys)
            {
                actions.Add(new(TableTransactionActionType.Delete, new TableEntity(_partitionKey, rowKey), ETag.All));
                if (actions.Count == MaxEntitiesPerTransaction)
                {
                    await Table.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);
                    actions.Clear();
                }
            }

            if (actions.Count > 0)
            {
                await Table.SubmitTransactionAsync(actions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (RequestFailedException exception)
        {
            LogRowCleanupFailure(_shared.Logger, Table.Name, _partitionKey, exception);
        }
    }

    private string CreateAllRowsFilter()
        => TableClient.CreateQueryFilter($"PartitionKey eq {_partitionKey}");

    private string CreateGenerationRangeFilter(string generation)
    {
        // 行键采用 "{generation}-{sequence}" 格式，因此排他上界为 '-' 的下一个字符。
        var lowerBound = generation + "-";
        var upperBound = generation + ".";
        return TableClient.CreateQueryFilter($"PartitionKey eq {_partitionKey} and RowKey ge {lowerBound} and RowKey lt {upperBound}");
    }

    private List<TableEntity> CreateDataEntities(ReadOnlySequence<byte> value, string generation, long firstSequence)
    {
        var entities = new List<TableEntity>();
        var remaining = value;
        var sequence = firstSequence;
        while (!remaining.IsEmpty)
        {
            entities.Add(CreateDataEntity(ref remaining, generation, sequence++));
        }

        return entities;
    }

    private TableEntity CreateDataEntity(ref ReadOnlySequence<byte> remaining, string generation, long sequence)
    {
        var entity = new TableEntity(_partitionKey, FormatRowKey(generation, sequence));
        for (var chunk = 0; chunk < MaxChunksPerEntity && !remaining.IsEmpty; chunk++)
        {
            var chunkLength = (int)Math.Min(ChunkBytes, remaining.Length);
            entity[ChunkPropertyNames[chunk]] = remaining.Slice(0, chunkLength).ToArray();
            remaining = remaining.Slice(chunkLength);
        }

        return entity;
    }

    private static long GetEntityPayloadLength(TableEntity entity)
    {
        var length = 0L;
        foreach (var propertyName in ChunkPropertyNames)
        {
            if (entity.TryGetValue(propertyName, out var value) && value is byte[] chunk)
            {
                length += chunk.Length;
            }
        }

        return length;
    }

    private static long ReadChunks(
        TableEntity entity,
        ArcBufferWriter buffer,
        IJournalStorageConsumer consumer,
        IJournalMetadata metadata)
    {
        var length = 0L;
        var foundChunk = false;
        var foundGap = false;
        foreach (var propertyName in ChunkPropertyNames)
        {
            if (!entity.TryGetValue(propertyName, out var value))
            {
                foundGap = true;
                continue;
            }

            if (foundGap)
            {
                throw new InvalidOperationException(
                    $"Azure Table journal row \"{entity.RowKey}\" contains a non-contiguous binary property \"{propertyName}\".");
            }

            ReadOnlyMemory<byte> chunk = value switch
            {
                byte[] bytes => bytes,
                BinaryData binaryData => binaryData.ToMemory(),
                _ => throw new InvalidOperationException(
                    $"Azure Table journal row \"{entity.RowKey}\" property \"{propertyName}\" is not a binary value."),
            };

            if (chunk.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Azure Table journal row \"{entity.RowKey}\" property \"{propertyName}\" is empty.");
            }

            foundChunk = true;
            buffer.Write(chunk.Span);
            length += chunk.Length;
            consumer.Read(new JournalBufferReader(buffer.Reader, isCompleted: false), metadata);
        }

        if (!foundChunk)
        {
            throw new InvalidOperationException($"Azure Table journal row \"{entity.RowKey}\" does not contain journal data.");
        }

        return length;
    }

    private TableEntity CreateHeaderCountsPatch(HeaderProviderState providerState)
        => new(_partitionKey, HeaderRowKey)
        {
            [JournalIdPropertyName] = _journalId.Value,
            [RowCountPropertyName] = providerState.RowCount,
            [LengthPropertyName] = providerState.Length,
            [AppendRowCountPropertyName] = providerState.AppendRowCount,
            [AppendLengthPropertyName] = providerState.AppendLength,
        };

    private TableEntity CreateHeaderFlipPatch(string generation, long rowCount, long length)
        => new(_partitionKey, HeaderRowKey)
        {
            [FormatPropertyName] = _shared.JournalFormatKey ?? string.Empty,
            [JournalIdPropertyName] = _journalId.Value,
            [GenerationPropertyName] = generation,
            [RowCountPropertyName] = rowCount,
            [LengthPropertyName] = length,
            [AppendRowCountPropertyName] = 0L,
            [AppendLengthPropertyName] = 0L,
        };

    private void SetHeader(ETag eTag, HeaderProviderState providerState)
    {
        // 保持缓存的头部行修改前置条件与压实计数器同步。
        _headerETag = eTag;
        _headerProviderState = providerState;
    }

    // 元数据操作绝不会建立或改变内容前置条件：它们只刷新此实例所持有的头部行
    // 版本的 ETag。
    private void RefreshHeaderETag(ETag eTag, HeaderProviderState providerState)
    {
        if (HeaderExists && providerState == _headerProviderState)
        {
            SetHeader(eTag, providerState);
        }
    }

    private void SetHeaderFromResponse(Response response, HeaderProviderState providerState)
        => SetHeader(GetResponseETag(response), providerState);

    // Azure Table 会返回写入的每个实体的新 ETag。
    private static ETag GetResponseETag(Response response)
        => response.Headers.ETag ?? throw new InvalidOperationException("Azure Table did not return the ETag of the written journal header.");

    private TableClient GetTableClient()
    {
        var client = _shared.TableClientProvider.GetTableClient();
        return client ?? throw new InvalidOperationException("The configured Azure Table journal client provider returned null.");
    }

    private static string FormatRowKey(string generation, long sequence)
        => string.Create(CultureInfo.InvariantCulture, $"{generation}-{sequence:D12}");

    private static string[] CreateChunkPropertyNames()
    {
        var names = new string[MaxChunksPerEntity];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = string.Create(CultureInfo.InvariantCulture, $"Data{i:D2}");
        }

        return names;
    }

    private HeaderState CreateHeaderState(TableEntity entity)
    {
        ValidateHeaderJournalId(entity);
        var generation = entity.GetString(GenerationPropertyName);
        if (generation is not { Length: > 0 })
        {
            throw new InvalidOperationException("Azure Table journal header is missing its generation property.");
        }

        var rowCount = entity.GetInt64(RowCountPropertyName);
        var length = entity.GetInt64(LengthPropertyName);
        if (rowCount is not >= 0 || length is not >= 0)
        {
            throw new InvalidOperationException("Azure Table journal header count or length properties are invalid.");
        }

        // 采用原始提供程序架构写入的头部行没有独立的压实计数器。
        // 在下一次替换重置计数器之前，保守地将当前世代视为尚未压实。
        var appendRowCount = entity.GetInt64(AppendRowCountPropertyName) ?? rowCount.Value;
        var appendLength = entity.GetInt64(AppendLengthPropertyName) ?? length.Value;
        if (appendRowCount < 0 || appendLength < 0)
        {
            throw new InvalidOperationException("Azure Table journal header count or length properties are invalid.");
        }

        return new HeaderState(
            entity.ETag,
            new HeaderProviderState(
                NormalizeFormat(entity.GetString(FormatPropertyName)),
                generation,
                rowCount.Value,
                length.Value,
                appendRowCount,
                appendLength),
            DeserializeCallerMetadata(entity.GetString(MetadataPropertyName)));
    }

    private IJournalMetadata CreateJournalMetadata(ETag eTag, TableEntity entity)
    {
        ValidateHeaderJournalId(entity);
        return CreateJournalMetadataSnapshot(eTag, entity);
    }

    internal static IJournalMetadata CreateJournalMetadataSnapshot(ETag eTag, TableEntity entity)
        => new JournalMetadata(
            NormalizeFormat(entity.GetString(FormatPropertyName)),
            eTag == default ? null : eTag.ToString(),
            DeserializeCallerMetadata(entity.GetString(MetadataPropertyName)));

    private void ValidateHeaderJournalId(TableEntity entity)
    {
        var journalId = entity.GetString(JournalIdPropertyName);
        // 旧版头部行不保存规范日志 ID。仍可通过配置的
        // 分区映射访问这些日志；下一次追加、替换或元数据更新时会补填日志 ID。
        if (journalId is not null && !string.Equals(journalId, _journalId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Azure Table journal header belongs to journal id \"{journalId}\", not \"{_journalId.Value}\". " +
                "Ensure that configured partition keys are unique.");
        }
    }

    private static string? NormalizeFormat(string? format) => format is { Length: > 0 } ? format : null;

    private static string SerializeCallerMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata is { Count: > 0 } ? JsonSerializer.Serialize(metadata, CallerMetadataJsonContext.Default.IReadOnlyDictionaryStringString) : "{}";

    private static Dictionary<string, string> DeserializeCallerMetadata(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, CallerMetadataJsonContext.Default.DictionaryStringString);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Azure Table journal header metadata property is invalid.", exception);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parsed is not null)
        {
            foreach (var (key, value) in parsed)
            {
                result[key] = value;
            }
        }

        return result;
    }

    [JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    private sealed partial class CallerMetadataJsonContext : JsonSerializerContext;

    private static Dictionary<string, string> CopyAndValidateCallerMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return result;
        }

        foreach (var (key, value) in metadata)
        {
            ValidateCallerMetadataProperty(key, value);
            result.Add(key, value);
        }

        return result;
    }

    private static IReadOnlySet<string> CopyRemove(IEnumerable<string>? remove, IReadOnlyDictionary<string, string> set)
    {
        if (remove is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyName in remove)
        {
            ValidateCallerMetadataPropertyName(propertyName);
            if (set.ContainsKey(propertyName))
            {
                throw new ArgumentException($"Journal metadata property '{propertyName}' cannot be both set and removed.", nameof(remove));
            }

            result.Add(propertyName);
        }

        return result;
    }

    private static bool ApplyCallerMetadataUpdate(
        Dictionary<string, string> metadata,
        IReadOnlyDictionary<string, string> set,
        IReadOnlySet<string> remove)
    {
        var changed = false;
        foreach (var propertyName in remove)
        {
            changed |= metadata.Remove(propertyName);
        }

        foreach (var (propertyName, value) in set)
        {
            if (!metadata.TryGetValue(propertyName, out var currentValue)
                || !string.Equals(currentValue, value, StringComparison.Ordinal))
            {
                metadata[propertyName] = value;
                changed = true;
            }
        }

        return changed;
    }

    private static void ValidateCallerMetadataProperty(string key, string value)
    {
        ValidateCallerMetadataPropertyName(key);
        ArgumentNullException.ThrowIfNull(value);
    }

    private static void ValidateCallerMetadataPropertyName(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.IndexOf('\0', StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("Journal metadata property names must not contain null characters.", nameof(key));
        }

        if (key.StartsWith("$", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Journal metadata property '{key}' is provider-owned.", nameof(key));
        }
    }

    private static ETag ToAzureETag(string eTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eTag);
        return new ETag(eTag);
    }

    private static bool IsEntityAlreadyExists(RequestFailedException exception)
        => exception.Status == 409
            && (string.Equals(exception.ErrorCode, "EntityAlreadyExists", StringComparison.Ordinal)
                || exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 当 Azure 响应表明日志头部行自缓存 ETag 获取以来已被修改时返回 true，
    /// 包括 HTTP 404（头部行已删除）、HTTP 412（前置条件失败或 IfMatch
    /// 被拒绝），或带有 <c>EntityAlreadyExists</c> 的 HTTP 409（竞争写入方已发布
    /// 此事务试图添加的数据行）。此方法返回 true 时，调用方应尝试
    /// <see cref="RetryAfterMetadataOnlyConflictAsync"/>，以便在
    /// 仅元数据发生变化时就地刷新缓存的 ETag；否则应传播
    /// <see cref="Orleans.Storage.InconsistentStateException"/>，触发日志层恢复。
    /// 瞬时传输故障（HTTP 5xx、网络错误、超时）由 Azure
    /// SDK 的内置重试策略处理，不会传递到此分类器。
    /// </summary>
    private static bool IsHeaderMutationConflict(RequestFailedException exception)
    {
        // 这些失败意味着缓存的头部行视图已过时或不复存在，因此调用方必须在重试前执行恢复。
        return exception.Status is 404 or 412
            || exception.Status == 409 && string.Equals(exception.ErrorCode, "EntityAlreadyExists", StringComparison.Ordinal);
    }

    private static InconsistentStateException CreateInconsistentHeaderStateException(string message, ETag expectedETag, Exception? exception = null)
    {
        var currentETag = expectedETag == default ? "Unknown" : expectedETag.ToString();
        return exception is null
            ? new InconsistentStateException(message, storedEtag: "Unknown", currentEtag: currentETag)
            : new InconsistentStateException(message, storedEtag: "Unknown", currentEtag: currentETag, exception);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Appended {Length} bytes as {RowCount} rows to table \"{TableName}\" partition \"{PartitionKey}\"")]
    private static partial void LogAppend(ILogger logger, long length, int rowCount, string tableName, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Read {Length} bytes from table \"{TableName}\" partition \"{PartitionKey}\"")]
    private static partial void LogRead(ILogger logger, long length, string tableName, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Wrote journal generation \"{Generation}\" containing {Length} bytes to table \"{TableName}\" partition \"{PartitionKey}\"")]
    private static partial void LogReplace(ILogger logger, string generation, long length, string tableName, string partitionKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to delete obsolete Azure Table journal rows from table \"{TableName}\" partition \"{PartitionKey}\"")]
    private static partial void LogRowCleanupFailure(ILogger logger, string tableName, string partitionKey, Exception exception);

    private readonly record struct HeaderProviderState(
        string? Format,
        string? Generation,
        long RowCount,
        long Length,
        long AppendRowCount,
        long AppendLength);

    private readonly record struct HeaderState(
        ETag ETag,
        HeaderProviderState ProviderState,
        Dictionary<string, string> CallerMetadata);

    internal sealed class AzureTableJournalStorageShared
    {
        public AzureTableJournalStorageShared(
            ILogger<AzureTableJournalStorage> logger,
            IOptions<AzureTableJournalStorageOptions> options,
            TableClientProvider tableClientProvider,
            AzureTableJournalStorageInstruments instruments,
            string? journalFormatKey = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(tableClientProvider);

            Logger = logger;
            Options = options.Value;
            ArgumentNullException.ThrowIfNull(Options);
            AzureTableJournalStorageOptions.ValidateTableName(Options.TableName);
            if (Options.CompactionRowCountThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureTableJournalStorageOptions.CompactionRowCountThreshold)} must be positive.");
            }

            if (Options.CompactionSizeThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureTableJournalStorageOptions.CompactionSizeThreshold)} must be positive.");
            }

            if (Options.MaxMetadataOnlyConflictRetries < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureTableJournalStorageOptions.MaxMetadataOnlyConflictRetries)} must be non-negative.");
            }

            if (Options.MetadataOnlyConflictInitialBackoff < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureTableJournalStorageOptions.MetadataOnlyConflictInitialBackoff)} must be non-negative.");
            }

            if (Options.MetadataOnlyConflictMaxBackoff < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureTableJournalStorageOptions.MetadataOnlyConflictMaxBackoff)} must be non-negative.");
            }

            JournalFormatKey = journalFormatKey;
            TableClientProvider = tableClientProvider;
            Instruments = instruments;
        }

        public ILogger<AzureTableJournalStorage> Logger { get; }

        public AzureTableJournalStorageOptions Options { get; }

        public string? JournalFormatKey { get; }

        public TableClientProvider TableClientProvider { get; }

        public AzureTableJournalStorageInstruments Instruments { get; }
    }

    internal abstract class TableClientProvider
    {
        public abstract TableClient GetTableClient();
    }

    internal sealed class InitializedTableClientProvider : TableClientProvider
    {
        private TableClient? _tableClient;

        public override TableClient GetTableClient()
            => _tableClient ?? throw new InvalidOperationException(
                $"{nameof(AzureTableJournalStorageProvider)} has not been initialized. Ensure the silo lifecycle has started before using journal storage.");

        public void SetTableClient(TableClient tableClient)
        {
            ArgumentNullException.ThrowIfNull(tableClient);
            _tableClient = tableClient;
        }
    }
}
