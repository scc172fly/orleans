using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Storage;

namespace Orleans.Durable.Journaling;

internal sealed partial class AzureBlobJournalStorage : IJournalStorage
{
    // WAL 和检查点 Blob 使用此元数据键声明其字节所采用的日志格式。
    internal const string FormatMetadataKey = "format";

    // WAL 元数据使用此键将恢复过程指向保存压实后日志前缀的检查点 Blob。
    internal const string CheckpointMetadataKey = "checkpoint";

    // WAL 元数据使用此键记录检查点已包含的 WAL 字节数；恢复时先跳过此前缀，再重放 WAL 尾部。
    internal const string CheckpointOffsetMetadataKey = "checkpoint_offset";

    // WAL 元数据使用此提供程序所有的键区分仅元数据发生变化导致的 ETag 变更与 WAL 重建。
    internal const string WalGenerationMetadataKey = "wal_generation";

    // AppendAsync 在发送追加请求之前拒绝超过 Azure 文档规定的单块大小上限的批次。
    internal const long MaxAppendBlockBytes = 100L * 1024 * 1024;

    // ThrowIfCompactionRequired 利用 Azure 已提交块数的硬性上限，在 Azure 拒绝追加前明确报错。
    private const int MaxBlocksPerBlob = 50_000;

    // ThrowIfCompactionRequired 在压实成为强制要求后仍预留此数量的可追加块容量。
    private const int HeadroomBlockCount = 100;

    // IsCompactionRequested 在达到此块数时返回触发状态，使调用方在达到追加 Blob 的硬性上限前执行压实。
    private const int RequestCompactionBlockCount = 49_000;

    private readonly AzureBlobJournalStorageShared _shared;
    private readonly JournalId _journalId;
    private readonly AppendBlobClient _walClient;
    // 读取或创建之前为 null；WAL 不存在时为 default(ETag)。
    private ETag? _walETag;
    private WalProviderState _walProviderState;

    private bool WalExists => _walETag is { } eTag && eTag != default;

    public bool IsCompactionRequested => _walProviderState.CommittedBlockCount > RequestCompactionBlockCount;

    internal AzureBlobJournalStorage(
        AzureBlobJournalStorageShared shared,
        JournalId journalId)
    {
        ArgumentNullException.ThrowIfNull(shared);
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        _shared = shared;
        _journalId = journalId;
        _walClient = GetWalClient();
    }

    public async ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;
        var callerMetadata = CopyAndValidateCallerMetadata(metadata);
        try
        {
            var created = await CreateWalAsync(
                checkpointName: null,
                new AppendBlobRequestConditions { IfNoneMatch = ETag.All },
                cancellationToken,
                callerMetadata).ConfigureAwait(false);
            SetWal(created.Response.Value.ETag, created.ProviderState);
            succeeded = true;
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            succeeded = true;
            return false;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationCreate,
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
            var properties = await GetPropertiesCoreAsync(_walClient, conditions: null, cancellationToken).ConfigureAwait(false);
            succeeded = true;
            return properties is null || properties.BlobType != BlobType.Append
                ? null
                : CreateJournalMetadata(properties.ETag, properties.Metadata);
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationGetMetadata,
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

                BlobProperties? properties;
                try
                {
                    properties = await GetPropertiesCoreAsync(
                        _walClient,
                        expectedETag is null ? null : new BlobRequestConditions { IfMatch = ToAzureETag(expectedETag) },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (RequestFailedException exception) when (exception.Status is 412)
                {
                    succeeded = true;
                    return null;
                }

                if (properties is null || properties.BlobType != BlobType.Append)
                {
                    succeeded = true;
                    return null;
                }

                var walState = CreateWalState(properties);
                var metadata = CopyMetadata(properties.Metadata);
                if (!ApplyCallerMetadataUpdate(metadata, setValues, removeValues))
                {
                    RefreshWalETag(walState.ETag, walState.ETag, walState.ProviderState);
                    succeeded = true;
                    return CreateJournalMetadata(properties.ETag, metadata);
                }

                var conditions = new BlobRequestConditions
                {
                    IfMatch = expectedETag is null ? properties.ETag : ToAzureETag(expectedETag),
                };

                try
                {
                    var response = await _walClient.SetMetadataAsync(metadata, conditions, cancellationToken).ConfigureAwait(false);
                    RefreshWalETag(walState.ETag, response.Value.ETag, walState.ProviderState);
                    succeeded = true;
                    return CreateJournalMetadata(response.Value.ETag, metadata);
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
                AzureBlobJournalStorageInstruments.OperationUpdateMetadata,
                Stopwatch.GetElapsedTime(startTimestamp),
                bytes: 0,
                succeeded);
        }
    }

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value.Length, nameof(value));
        // 每次追加写入一个 Azure 追加块，因此先验证 Blob 限制，再访问存储。
        ThrowIfBatchTooLarge(value.Length);
        var startTimestamp = Stopwatch.GetTimestamp();
        var succeeded = false;

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnsureWalAsync(nameof(AppendAsync), cancellationToken).ConfigureAwait(false);

                ThrowIfCompactionRequired();

                var expectedETag = _walETag!.Value;
                var expectedProviderState = _walProviderState;
                using var stream = new ReadOnlySequenceStream(value);
                try
                {
                    // 使用最近观测到的 WAL ETag，若 WAL 自本实例恢复以来发生变化，追加便会失败。
                    var result = await _walClient.AppendBlockAsync(
                        stream,
                        new AppendBlobAppendBlockOptions
                        {
                            Conditions = new AppendBlobRequestConditions
                            {
                                IfMatch = expectedETag,
                            }
                        },
                        cancellationToken).ConfigureAwait(false);

                    LogAppend(_shared.Logger, stream.Length, _walClient.BlobContainerName, _walClient.Name);

                    // 缓存 Azure 追加后的状态，使下一次修改受到新 ETag 和块数的保护。
                    SetWal(
                        result.Value.ETag,
                        expectedProviderState with
                        {
                            ContentLength = expectedProviderState.ContentLength + value.Length,
                            CommittedBlockCount = result.Value.BlobCommittedBlockCount
                        });
                    succeeded = true;
                    return;
                }
                catch (RequestFailedException exception) when (IsBlobSealed(exception))
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL is sealed; recovery is required before appending.",
                        expectedETag,
                        exception);
                }
                catch (RequestFailedException exception) when (IsWalMutationConflict(exception))
                {
                    var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                        ? await RetryAfterMetadataOnlyConflictAsync(attempt, expectedProviderState, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (refreshed is not null)
                    {
                        _shared.Instruments.OnRetry("metadata_only_conflict");
                        continue;
                    }

                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while appending; recovery is required.",
                        expectedETag,
                        exception);
                }
            }
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationAppend,
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
            if (_walETag is null)
            {
                throw new InvalidOperationException($"{nameof(DeleteAsync)} requires reading or creating the journal first.");
            }

            WalState? walState;
            var expectedETag = _walETag.Value;
            var expectedProviderState = _walProviderState;
            var observedAbsent = !WalExists;
            var conditions = observedAbsent ? null : new BlobRequestConditions { IfMatch = expectedETag };
            try
            {
                // 在前置条件保护下加载 WAL 清单，因为删除会使其指定的检查点无法再通过清单访问。
                walState = await TryLoadWalStateAsync(conditions, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (IsWalMutationConflict(exception))
            {
                walState = conditions is not null
                    ? await TryRefreshWalStateAfterMetadataOnlyConflictAsync(expectedProviderState, cancellationToken).ConfigureAwait(false)
                    : null;
                if (walState is null)
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while deleting the journal; recovery is required.",
                        expectedETag,
                        exception);
                }
            }

            if (walState is null)
            {
                if (conditions is not null)
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while deleting the journal; recovery is required.",
                        _walETag.Value);
                }

                succeeded = true;
                return;
            }

            if (observedAbsent)
            {
                throw CreateInconsistentWalStateException(
                    "Azure Blob journal WAL was created by another writer after this instance observed it absent; recovery is required.",
                    expectedETag);
            }

            // 清除本地 WAL 状态前记住检查点，以便仍能清理过时检查点。
            var checkpointName = walState.Value.Manifest.Checkpoint?.Name;
            for (var attempt = 0; ; attempt++)
            {
                var deleteWalState = walState.Value;
                try
                {
                    // 先在 WAL 的 ETag 保护下删除 WAL，再清理检查点，以免并发 WAL 更新失去对应的检查点。
                    await _walClient.DeleteIfExistsAsync(
                        DeleteSnapshotsOption.None,
                        new BlobRequestConditions { IfMatch = deleteWalState.ETag },
                        cancellationToken).ConfigureAwait(false);
                    SetWal(eTag: default, providerState: default);
                    break;
                }
                catch (RequestFailedException exception) when (IsWalMutationConflict(exception))
                {
                    var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                        ? await RetryAfterMetadataOnlyConflictAsync(attempt, deleteWalState.ProviderState, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (refreshed is { } refreshedState)
                    {
                        walState = refreshedState;
                        checkpointName = refreshedState.Manifest.Checkpoint?.Name;
                        _shared.Instruments.OnRetry("metadata_only_conflict");
                        continue;
                    }

                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while deleting the journal; recovery is required.",
                        deleteWalState.ETag,
                        exception);
                }
            }

            if (checkpointName is not null)
            {
                // 检查点清理发生在 WAL 删除之后，因为没有 WAL 清单便无法通过清单访问它。
                await DeleteCheckpointIfExistsAsync(checkpointName, cancellationToken).ConfigureAwait(false);
            }

            succeeded = true;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationDelete,
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
            Response<BlobDownloadStreamingResult> walResult;
            try
            {
                // 首先下载 WAL，因为它的元数据是需要重放的检查点的清单。
                walResult = await _walClient.DownloadStreamingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (exception.Status is 404)
            {
                if (WalExists)
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while reading; recovery through a fresh storage instance is required.",
                        _walETag!.Value, exception);
                }

                consumer.Complete(metadata: null);
                SetWal(eTag: default, providerState: default);
                succeeded = true;
                return;
            }

            var walDetails = walResult.Value.Details;
            var manifest = CreateWalManifest(walDetails.Metadata);
            var walProviderState = CreateWalProviderState(manifest, walDetails.ContentLength, walDetails.BlobCommittedBlockCount);

            await using var walStream = walResult.Value.Content;
            if (_walETag is { } expectedETag
                && (!WalExists || walProviderState != _walProviderState
                    || walProviderState.Generation is null && walDetails.ETag != expectedETag))
            {
                throw CreateInconsistentWalStateException(
                    "Azure Blob journal WAL changed while reading; recovery through a fresh storage instance is required.",
                    expectedETag);
            }

            var expectedFormat = manifest.Metadata.FormatKey;
            if (manifest.Checkpoint is { } checkpoint)
            {
                var checkpointClient = GetCheckpointClient(checkpoint.Name);
                var checkpointResult = await checkpointClient.DownloadStreamingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await using var checkpointStream = checkpointResult.Value.Content;

                // 首先重放不可变检查点，因为它表示 WAL 条目之前已经压实的前缀。
                var checkpointMetadata = ValidateCheckpointMetadata(checkpoint, checkpointResult.Value.Details, expectedFormat);
                var totalCheckpointBytes = await consumer.ReadAsync(
                    checkpointStream,
                    checkpointMetadata,
                    complete: false,
                    cancellationToken).ConfigureAwait(false);
                LogRead(_shared.Logger, totalCheckpointBytes, checkpointClient.BlobContainerName, checkpointClient.Name);
                bytes += totalCheckpointBytes;
                expectedFormat = checkpointMetadata.FormatKey;
            }

            if (manifest.Checkpoint is { WalOffset: > 0 } checkpointOffset)
            {
                if (checkpointOffset.WalOffset > walDetails.ContentLength)
                {
                    throw new InvalidOperationException(
                        $"Azure Blob journal checkpoint offset {checkpointOffset.WalOffset:N0} exceeds WAL length {walDetails.ContentLength:N0}.");
                }

                // 检查点已包含直到此偏移量的 WAL 字节，因此只重放 WAL 尾部。
                await AzureBlobJournalStorageStreamHelpers.SkipStreamAsync(walStream, checkpointOffset.WalOffset, cancellationToken).ConfigureAwait(false);
            }

            // 优先使用 WAL 格式元数据；若压实重建了空 WAL，则回退到检查点。
            var walMetadata = manifest.Metadata.FormatKey is { Length: > 0 }
                ? manifest.Metadata
                : expectedFormat is { Length: > 0 }
                    ? new JournalMetadata(expectedFormat)
                    : JournalMetadata.Empty;
            var totalWalBytes = await consumer.ReadAsync(
                walStream,
                walMetadata,
                complete: false,
                cancellationToken).ConfigureAwait(false);
            LogRead(_shared.Logger, totalWalBytes, _walClient.BlobContainerName, _walClient.Name);
            bytes += totalWalBytes;

            // 仅在检查点和 WAL 尾部作为同一逻辑日志完成流式读取后才标记完成。
            consumer.Complete(walMetadata);
            SetWal(walDetails.ETag, walProviderState);
            succeeded = true;
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationRead,
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
            // 压实通过 WAL 元数据发布；因此如果此实例此前观测到 WAL 不存在，先创建将接受 ETag 检查的 WAL。
            await EnsureWalAsync(nameof(ReplaceAsync), cancellationToken).ConfigureAwait(false);

            var expectedWalETag = _walETag!.Value;
            var expectedProviderState = _walProviderState;
            WalState? walState;
            try
            {
                // 读取 WAL 清单，使压实在替换提供程序所有的检查点元数据时保留调用方所有的元数据。
                walState = await TryLoadWalStateAsync(new BlobRequestConditions { IfMatch = expectedWalETag }, cancellationToken).ConfigureAwait(false);
                if (walState is null)
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while publishing a checkpoint; recovery is required.",
                        expectedWalETag);
                }
            }
            catch (RequestFailedException exception) when (IsWalMutationConflict(exception))
            {
                walState = await TryRefreshWalStateAfterMetadataOnlyConflictAsync(expectedProviderState, cancellationToken).ConfigureAwait(false);
                if (walState is null)
                {
                    throw CreateInconsistentWalStateException(
                        "Azure Blob journal WAL changed while publishing a checkpoint; recovery is required.",
                        expectedWalETag,
                        exception);
                }
            }

            var previousCheckpointName = _shared.Options.DeleteOldCheckpoints ? walState.Value.Manifest.Checkpoint?.Name : null;

            using var checkpointStream = new ReadOnlySequenceStream(value);
            while (true)
            {
                // 检查点 Blob 不可变，并使用随机快照 ID 作为内容地址，以便发生冲突时安全重试。
                var checkpointName = GetCheckpointName(Guid.NewGuid().ToString("N"));
                var checkpointClient = GetCheckpointClient(checkpointName);
                try
                {
                    // 先上传检查点，再通过 WAL 元数据发布它，使上传失败不会改变恢复结果。
                    checkpointStream.Position = 0;
                    await checkpointClient.UploadAsync(
                        checkpointStream,
                        new BlobUploadOptions
                        {
                            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                            HttpHeaders = CreateHttpHeaders(_shared.MimeType),
                            Metadata = CreateCheckpointBlobMetadata(),
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (RequestFailedException exception) when (IsBlobAlreadyExists(exception))
                {
                    // 快照 ID 是随机生成的，因此这种情况极少发生。使用新 ID 重试。
                    _shared.Instruments.OnRetry("checkpoint_collision");
                    continue;
                }

                for (var attempt = 0; ; attempt++)
                {
                    var publishWalState = walState.Value;
                    try
                    {
                        // 在 WAL 的 ETag 保护下重新创建 WAL，仅当现有 WAL 未发生变化时发布检查点。
                        var created = await CreateWalAsync(
                            checkpointName,
                            new AppendBlobRequestConditions { IfMatch = publishWalState.ETag },
                            cancellationToken,
                            publishWalState.Manifest.Metadata.Properties).ConfigureAwait(false);
                        SetWal(created.Response.Value.ETag, created.ProviderState);
                        break;
                    }
                    catch (RequestFailedException exception) when (IsWalMutationConflict(exception))
                    {
                        var refreshed = attempt < _shared.Options.MaxMetadataOnlyConflictRetries
                            ? await RetryAfterMetadataOnlyConflictAsync(attempt, publishWalState.ProviderState, cancellationToken).ConfigureAwait(false)
                            : null;
                        if (refreshed is { } refreshedState)
                        {
                            walState = refreshedState;
                            _shared.Instruments.OnRetry("metadata_only_conflict");
                            continue;
                        }

                        throw CreateInconsistentWalStateException(
                            "Azure Blob journal WAL changed while publishing a checkpoint; recovery is required.",
                            publishWalState.ETag,
                            exception);
                    }
                }

                if (previousCheckpointName is not null && !string.Equals(previousCheckpointName, checkpointName, StringComparison.Ordinal))
                {
                    // 在发布新 WAL 之前保留旧检查点，使清单始终不会指向缺失的 Blob。
                    await DeleteCheckpointIfExistsAsync(previousCheckpointName, cancellationToken).ConfigureAwait(false);
                }

                LogReplace(_shared.Logger, checkpointClient.BlobContainerName, checkpointClient.Name, checkpointStream.Length);

                // 此时可以通过 WAL 元数据访问新检查点，旧状态已解除关联。
                succeeded = true;
                return;
            }
        }
        finally
        {
            _shared.Instruments.OnOperationCompleted(
                AzureBlobJournalStorageInstruments.OperationReplace,
                Stopwatch.GetElapsedTime(startTimestamp),
                value.Length,
                succeeded);
        }
    }

    private static void ThrowIfBatchTooLarge(long length)
    {
        // Azure 会拒绝过大的追加块，因此在本地提前失败，并给出日志存储专用的指导信息。
        if (length <= MaxAppendBlockBytes)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Azure Append Blob journal batch of {length:N0} bytes exceeds the per-block limit of {MaxAppendBlockBytes:N0} bytes (100 MiB). " +
            "Reduce the operation size or compact more aggressively.");
    }

    private void ThrowIfCompactionRequired()
    {
        // 在达到 Azure 硬性块数上限前停止，使调用方仍有追加余量时可以执行压实。
        if (_walProviderState.CommittedBlockCount < MaxBlocksPerBlob - HeadroomBlockCount)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Azure Blob journal WAL has {_walProviderState.CommittedBlockCount:N0} committed append blocks and must be compacted before more appends. " +
            $"Azure Append Blob supports at most {MaxBlocksPerBlob:N0} committed blocks.");
    }

    private async ValueTask EnsureWalAsync(string operation, CancellationToken cancellationToken)
    {
        if (_walETag is null)
        {
            throw new InvalidOperationException($"{operation} requires reading or creating the journal first.");
        }

        if (WalExists)
        {
            return;
        }

        try
        {
            // 此实例此前观测到 WAL 不存在，因此仅在 WAL 仍不存在时创建它。
            var created = await CreateWalAsync(
                checkpointName: null,
                new AppendBlobRequestConditions { IfNoneMatch = ETag.All },
                cancellationToken).ConfigureAwait(false);
            SetWal(created.Response.Value.ETag, created.ProviderState);
        }
        catch (RequestFailedException exception) when (IsBlobAlreadyExists(exception))
        {
            throw CreateInconsistentWalStateException(
                "Azure Blob journal WAL was created by another writer after this instance observed it absent; recovery is required.",
                _walETag.Value,
                exception);
        }
    }

    private async ValueTask<WalState?> TryLoadWalStateAsync(BlobRequestConditions? conditions, CancellationToken cancellationToken)
    {
        Response<BlobProperties> walProperties;
        try
        {
            // 只读取 WAL 属性和元数据；确定修改状态不需要读取日志字节。
            walProperties = await _walClient.GetPropertiesAsync(conditions, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status is 404)
        {
            return null;
        }

        return CreateWalState(walProperties.Value);
    }

    private async ValueTask<WalState?> RetryAfterMetadataOnlyConflictAsync(
        int attempt,
        WalProviderState expectedProviderState,
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
            var scaledTicks = initial.Ticks * multiplier;
            var cappedTicks = Math.Min(scaledTicks, max.Ticks);
            await Task.Delay(TimeSpan.FromTicks(cappedTicks), cancellationToken).ConfigureAwait(false);
        }

        return await TryRefreshWalStateAfterMetadataOnlyConflictAsync(expectedProviderState, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WalState?> TryRefreshWalStateAfterMetadataOnlyConflictAsync(
        WalProviderState expectedProviderState,
        CancellationToken cancellationToken)
    {
        if (expectedProviderState.Generation is null)
        {
            return null;
        }

        var walState = await TryLoadWalStateAsync(conditions: null, cancellationToken).ConfigureAwait(false);
        if (walState is null || walState.Value.ProviderState != expectedProviderState)
        {
            return null;
        }

        SetWal(walState.Value.ETag, walState.Value.ProviderState);
        return walState;
    }

    // 元数据操作绝不会建立或改变内容前置条件：它们仅刷新此实例所持有的 WAL 内容
    // 版本的 ETag。
    private void RefreshWalETag(ETag observedETag, ETag updatedETag, WalProviderState providerState)
    {
        if (WalExists
            && providerState == _walProviderState
            && (providerState.Generation is not null || observedETag == _walETag))
        {
            SetWal(updatedETag, providerState);
        }
    }

    private void SetWal(ETag eTag, WalProviderState providerState)
    {
        _walETag = eTag;
        _walProviderState = providerState;
    }

    private AppendBlobClient GetWalClient()
    {
        // 只解析一次提供程序专用的 WAL 客户端；检查点客户端按已发布的名称解析。
        var client = _shared.BlobClientProvider.GetWalClient(_journalId);
        return client ?? throw new InvalidOperationException("The configured Azure Blob journal WAL client provider returned null.");
    }

    private async ValueTask<CreatedWal> CreateWalAsync(
        string? checkpointName,
        AppendBlobRequestConditions conditions,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? callerMetadata = null)
    {
        var metadata = CreateWalMetadata(checkpointName, checkpointOffset: 0, callerMetadata);
        // 创建追加 Blob 也是压实操作发布新 WAL 清单的方式。
        var response = await _walClient.CreateAsync(
            new AppendBlobCreateOptions
            {
                Conditions = conditions,
                HttpHeaders = CreateHttpHeaders(_shared.MimeType),
                Metadata = metadata,
            },
            cancellationToken).ConfigureAwait(false);
        var manifest = CreateWalManifest(metadata);
        return new CreatedWal(response, manifest, CreateWalProviderState(manifest, contentLength: 0, committedBlockCount: 0));
    }

    private static BlobHttpHeaders? CreateHttpHeaders(string? contentType)
    {
        // 内容类型是可选的传输元数据；日志格式单独保存在 Blob 元数据中。
        return contentType is { Length: > 0 } ? new BlobHttpHeaders { ContentType = contentType } : null;
    }

    private string GetCheckpointName(string snapshotId)
    {
        // 由配置的提供程序负责检查点命名，使自定义布局与 WAL 命名保持一致。
        var checkpointName = _shared.BlobClientProvider.GetCheckpointName(_journalId, snapshotId);
        if (string.IsNullOrWhiteSpace(checkpointName))
        {
            throw new InvalidOperationException("The configured Azure Blob journal checkpoint client provider returned an empty blob name.");
        }

        return checkpointName;
    }

    private BlockBlobClient GetCheckpointClient(string checkpointName)
    {
        // 按已发布的检查点名称进行解析，使恢复过程能打开旧实例创建的检查点。
        var client = _shared.BlobClientProvider.GetCheckpointClient(_journalId, checkpointName);
        return client ?? throw new InvalidOperationException("The configured Azure Blob journal checkpoint client provider returned null.");
    }

    private async ValueTask DeleteCheckpointIfExistsAsync(string checkpointName, CancellationToken cancellationToken)
    {
        var checkpointClient = GetCheckpointClient(checkpointName);
        try
        {
            // 过时检查点采用尽力而为的方式清理，因为已发布的 WAL 不再引用它。
            await checkpointClient.DeleteIfExistsAsync(DeleteSnapshotsOption.None, conditions: null, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception)
        {
            LogCheckpointCleanupFailure(_shared.Logger, checkpointClient.BlobContainerName, checkpointClient.Name, exception);
        }
    }

    private Dictionary<string, string> CreateMetadataDictionary()
    {
        // 从 WAL 和检查点 Blob 的公共元数据开始构建，以便恢复时验证日志格式。
        var metadata = new Dictionary<string, string>();
        if (_shared.JournalFormatKey is { Length: > 0 })
        {
            metadata[FormatMetadataKey] = _shared.JournalFormatKey;
        }

        return metadata;
    }

    private Dictionary<string, string> CreateCheckpointBlobMetadata() => CreateMetadataDictionary();

    private Dictionary<string, string> CreateWalMetadata(
        string? checkpointName,
        long checkpointOffset,
        IReadOnlyDictionary<string, string>? callerMetadata = null)
    {
        // WAL 元数据是恢复清单：包含公共格式以及可选的检查点指针和 WAL 偏移量。
        var metadata = CreateMetadataDictionary();
        metadata[WalGenerationMetadataKey] = Guid.NewGuid().ToString("N");
        if (callerMetadata is not null)
        {
            foreach (var (key, value) in callerMetadata)
            {
                ValidateCallerMetadataProperty(key, value);
                metadata[key] = value;
            }
        }

        if (checkpointName is not null)
        {
            metadata[CheckpointMetadataKey] = checkpointName;
            metadata[CheckpointOffsetMetadataKey] = checkpointOffset.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static string? GetFormatKeyMetadata(IDictionary<string, string>? metadata)
        => metadata is not null
            && metadata.TryGetValue(FormatMetadataKey, out var storedKey)
            && storedKey is { Length: > 0 }
                ? storedKey
                : null;

    private static WalManifest CreateWalManifest(IDictionary<string, string>? metadata)
    {
        // 解码 WAL 清单，允许尚未压实、没有检查点指针的 WAL。
        var fileMetadata = CreateJournalMetadata(eTag: default, metadata);
        var generation = metadata is not null
            && metadata.TryGetValue(WalGenerationMetadataKey, out var storedGeneration)
            && storedGeneration is { Length: > 0 }
                ? storedGeneration
                : null;
        if (metadata is null || !metadata.TryGetValue(CheckpointMetadataKey, out var checkpointName) || checkpointName is not { Length: > 0 })
        {
            return new WalManifest(fileMetadata, Checkpoint: null, generation);
        }

        var checkpointOffset = 0L;
        if (metadata.TryGetValue(CheckpointOffsetMetadataKey, out var checkpointOffsetValue)
            && checkpointOffsetValue is { Length: > 0 }
            && (!long.TryParse(checkpointOffsetValue, NumberStyles.None, CultureInfo.InvariantCulture, out checkpointOffset) || checkpointOffset < 0))
        {
            throw new InvalidOperationException(
                $"Azure Blob journal checkpoint offset metadata is invalid: '{checkpointOffsetValue}'.");
        }

        return new WalManifest(fileMetadata, new CheckpointReference(checkpointName, checkpointOffset), generation);
    }

    private static WalState CreateWalState(BlobProperties properties)
    {
        var manifest = CreateWalManifest(properties.Metadata);
        return new WalState(
            properties.ETag,
            manifest,
            CreateWalProviderState(manifest, properties.ContentLength, properties.BlobCommittedBlockCount));
    }

    private static WalProviderState CreateWalProviderState(WalManifest manifest, long contentLength, int committedBlockCount)
        => new(
            manifest.Metadata.FormatKey,
            manifest.Checkpoint?.Name,
            manifest.Checkpoint?.WalOffset ?? 0,
            manifest.Generation,
            contentLength,
            committedBlockCount);

    private static IJournalMetadata ValidateCheckpointMetadata(CheckpointReference checkpoint, BlobDownloadDetails checkpointDetails, string? expectedFormat)
    {
        // 如果检查点和 WAL 声明的日志格式不同，则拒绝拼接两者的数据。
        var checkpointBlobFormat = GetFormatKeyMetadata(checkpointDetails.Metadata);
        if (expectedFormat is { Length: > 0 })
        {
            if (checkpointBlobFormat is null)
            {
                throw new InvalidOperationException(
                    $"Azure Blob journal checkpoint '{checkpoint.Name}' does not include format metadata.");
            }

            if (!string.Equals(expectedFormat, checkpointBlobFormat, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Azure Blob journal checkpoint '{checkpoint.Name}' format metadata is '{checkpointBlobFormat}', but recovery expected '{expectedFormat}'.");
            }
        }

        return CreateJournalMetadata(eTag: default, checkpointDetails.Metadata);
    }

    private static async ValueTask<BlobProperties?> GetPropertiesCoreAsync(
        AppendBlobClient blobClient,
        BlobRequestConditions? conditions,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient.GetPropertiesAsync(conditions, cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status is 404)
        {
            return null;
        }
    }

    internal static IJournalMetadata CreateJournalMetadata(ETag eTag, IDictionary<string, string>? metadata)
        => new JournalMetadata(
            GetFormatKeyMetadata(metadata),
            eTag == default ? null : eTag.ToString(),
            CopyCallerMetadata(metadata));

    private static Dictionary<string, string> CopyCallerMetadata(IDictionary<string, string>? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return result;
        }

        foreach (var (key, value) in metadata)
        {
            if (IsProviderMetadataKey(key))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

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

    private static Dictionary<string, string> CopyMetadata(IDictionary<string, string>? metadata)
        => metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

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
            ValidateCallerMetadataPropertyName(propertyName);
            changed |= metadata.Remove(propertyName);
        }

        foreach (var (propertyName, value) in set)
        {
            ValidateCallerMetadataProperty(propertyName, value);
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

        if (IsProviderMetadataKey(key))
        {
            throw new ArgumentException($"Journal metadata property '{key}' is provider-owned.", nameof(key));
        }
    }

    private static bool IsProviderMetadataKey(string key)
        => string.Equals(key, FormatMetadataKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, CheckpointMetadataKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, CheckpointOffsetMetadataKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, WalGenerationMetadataKey, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("$", StringComparison.Ordinal);

    private static ETag ToAzureETag(string eTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eTag);
        return new ETag(eTag);
    }

    /// <summary>
    /// 当 Azure 响应表明追加 Blob 已封存（HTTP 409 / BlobIsSealed）时返回 true。
    /// 已封存的 WAL 永久关闭，不再接受追加；日志层必须先执行恢复，
    /// 才能继续写入。
    /// </summary>
    private static bool IsBlobSealed(RequestFailedException exception)
        => exception.Status == 409
            && (string.Equals(exception.ErrorCode, "BlobIsSealed", StringComparison.Ordinal)
                || exception.Message.Contains("sealed", StringComparison.OrdinalIgnoreCase));

    private static bool IsBlobAlreadyExists(RequestFailedException exception)
        => exception.Status == 409
            && (string.Equals(exception.ErrorCode, "BlobAlreadyExists", StringComparison.Ordinal)
                || exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 当 Azure 响应表明 WAL 自缓存 ETag 获取以来已被修改时返回 true，
    /// 包括 HTTP 404（WAL 已删除或重建）、HTTP 412（前置条件失败或 IfMatch
    /// 被拒绝），或带有 <c>ConditionNotMet</c> 的 HTTP 409（条件写入冲突）。当此方法
    /// 返回 true 时，调用方应尝试
    /// <see cref="RetryAfterMetadataOnlyConflictAsync"/>，以便在
    /// 仅元数据发生变化时就地刷新缓存的 ETag；否则应传播
    /// <see cref="Orleans.Storage.InconsistentStateException"/>，触发日志层恢复。
    /// 瞬时传输故障（HTTP 5xx、网络错误、超时）由 Azure
    /// SDK 的内置重试策略处理，不会传递到此分类器。
    /// </summary>
    private static bool IsWalMutationConflict(RequestFailedException exception)
    {
        // 这些失败意味着缓存的 WAL 视图已过时或不复存在，因此调用方必须在重试前执行恢复。
        return exception.Status is 404 or 412
            || exception.Status == 409 && string.Equals(exception.ErrorCode, "ConditionNotMet", StringComparison.Ordinal);
    }

    private static InconsistentStateException CreateInconsistentWalStateException(string message, ETag expectedETag, Exception? exception = null)
    {
        var currentETag = expectedETag == default ? "Unknown" : expectedETag.ToString();
        return exception is null
            ? new InconsistentStateException(message, storedEtag: "Unknown", currentEtag: currentETag)
            : new InconsistentStateException(message, storedEtag: "Unknown", currentEtag: currentETag, exception);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Appended {Length} bytes to blob \"{ContainerName}/{BlobName}\"")]
    private static partial void LogAppend(ILogger logger, long length, string containerName, string blobName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Read {Length} bytes from blob \"{ContainerName}/{BlobName}\"")]
    private static partial void LogRead(ILogger logger, long length, string containerName, string blobName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Wrote checkpoint blob \"{ContainerName}/{BlobName}\" containing {Length} bytes")]
    private static partial void LogReplace(ILogger logger, string containerName, string blobName, long length);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to delete obsolete Azure Blob journal checkpoint \"{ContainerName}/{BlobName}\"")]
    private static partial void LogCheckpointCleanupFailure(ILogger logger, string containerName, string blobName, Exception exception);

    private sealed record WalManifest(IJournalMetadata Metadata, CheckpointReference? Checkpoint, string? Generation);

    private readonly record struct WalProviderState(
        string? Format,
        string? CheckpointName,
        long CheckpointOffset,
        string? Generation,
        long ContentLength,
        int CommittedBlockCount);

    private readonly record struct WalState(ETag ETag, WalManifest Manifest, WalProviderState ProviderState);

    private readonly record struct CreatedWal(Response<BlobContentInfo> Response, WalManifest Manifest, WalProviderState ProviderState);

    private readonly record struct CheckpointReference(string Name, long WalOffset);

    internal sealed class AzureBlobJournalStorageShared
    {
        public AzureBlobJournalStorageShared(
            ILogger<AzureBlobJournalStorage> logger,
            IOptions<AzureBlobJournalStorageOptions> options,
            BlobClientProvider blobClientProvider,
            AzureBlobJournalStorageInstruments instruments,
            string? mimeType = null,
            string? journalFormatKey = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(blobClientProvider);

            Logger = logger;
            Options = options.Value;
            ArgumentNullException.ThrowIfNull(Options);
            if (Options.MaxMetadataOnlyConflictRetries < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureBlobJournalStorageOptions.MaxMetadataOnlyConflictRetries)} must be non-negative.");
            }

            if (Options.MetadataOnlyConflictInitialBackoff < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureBlobJournalStorageOptions.MetadataOnlyConflictInitialBackoff)} must be non-negative.");
            }

            if (Options.MetadataOnlyConflictMaxBackoff < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(AzureBlobJournalStorageOptions.MetadataOnlyConflictMaxBackoff)} must be non-negative.");
            }

            MimeType = mimeType;
            JournalFormatKey = journalFormatKey;
            BlobClientProvider = blobClientProvider;
            Instruments = instruments;
        }

        public ILogger<AzureBlobJournalStorage> Logger { get; }

        public AzureBlobJournalStorageOptions Options { get; }

        public string? MimeType { get; }

        public string? JournalFormatKey { get; }

        public BlobClientProvider BlobClientProvider { get; }
        public AzureBlobJournalStorageInstruments Instruments { get; }
    }

    internal abstract class BlobClientProvider
    {
        public abstract AppendBlobClient GetWalClient(JournalId journalId);

        public abstract string GetCheckpointName(JournalId journalId, string snapshotId);

        public abstract BlockBlobClient GetCheckpointClient(JournalId journalId, string checkpointName);
    }

    internal sealed class OptionsBlobClientProvider(
        IBlobContainerFactory containerFactory,
        AzureBlobJournalStorageOptions options) : BlobClientProvider
    {
        public override AppendBlobClient GetWalClient(JournalId journalId)
        {
            var container = containerFactory.GetBlobContainerClient(journalId);
            return container.GetAppendBlobClient(options.GetWalBlobNameForJournal(journalId));
        }

        public override string GetCheckpointName(JournalId journalId, string snapshotId)
            => options.GetCheckpointBlobNameForJournal(journalId, snapshotId);

        public override BlockBlobClient GetCheckpointClient(JournalId journalId, string checkpointName)
            => containerFactory.GetBlobContainerClient(journalId).GetBlockBlobClient(checkpointName);
    }
}
