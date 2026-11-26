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
    // WAL 和检查站布局使用此元数据键来声明其字节包含的日记格式 。
    internal const string FormatMetadataKey = "format";

    // WAL元数据使用这个关键值来指向持有紧凑日记前缀的检查站布洛布的回收点。
    internal const string CheckpointMetadataKey = "checkpoint";

    // WAL 元数据使用此密钥记录检查站中已经包含多少 WAL 字节;在重放 WAL 尾巴之前,回收会跳过前缀。
    internal const string CheckpointOffsetMetadataKey = "checkpoint_offset";

    // 沃尔玛元数据使用这一供应商拥有的钥匙,将仅供数据使用的ETag变化与WAL娱乐区分开来。
    internal const string WalGenerationMetadataKey = "wal_generation";

    // AppendAsync 在发送附加请求之前,拒绝超过 Azure文件记录的每个区块上限的批次。
    internal const long MaxAppendBlockBytes = 100L * 1024 * 1024;

    // ThrowIfCompactionRequired 用途 Azure之前显然会失败的硬性承诺封顶 Azure 拒绝附件。
    private const int MaxBlocksPerBlob = 50_000;

    // ThrowIfCompactionRequired 在硬性压缩后保留了这么多的附加能力块。
    private const int HeadroomBlockCount = 100;

    // IsCompactionRequested 如此在硬性附加布蓝限制之前 紧凑起来
    private const int RequestCompactionBlockCount = 49_000;

    private readonly AzureBlobJournalStorageShared _shared;
    private readonly JournalId _journalId;
    private readonly AppendBlobClient _walClient;
    // 读取或创建前为空的 WAL 默认值( ETag) 。
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
        // 附加内容作为 Azure 附加块写入, 所以在触摸存储前验证 blob 限制 。
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
                    // 使用上次观察到的 WAL ETag , 如果WAL 自此恢复后更改了附件, 则附件失效 。
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

                    // Cache Azure的后附加状态, 下次突变由新的ETag和区块数来守卫。
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
                // 将WAL清单装入前提条件下,因为删除会使检查站无法进入。
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

            // 记得检查站之前清理当地WAL州,
            var checkpointName = walState.Value.Manifest.Checkpoint?.Name;
            for (var attempt = 0; ; attempt++)
            {
                var deleteWalState = walState.Value;
                try
                {
                    // 在检查站清理前,在ETag下删除WAL,这样赛车WAL更新工作不会失去其检查站。
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
                // 检查点清理在WAL删除后发生,因为没有WAL显示器就无法到达。
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
                // 首先下载 WAL 是因为其元数据是任何必须重播的检查站的显示器。
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

                // 首先重放不可变的检查站, 因为它代表着WAL 进入前的紧凑前缀 。
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

                // 检查站已经包含WAL字节,
                await AzureBlobJournalStorageStreamHelpers.SkipStreamAsync(walStream, checkpointOffset.WalOffset, cancellationToken).ConfigureAwait(false);
            }

            // 更倾向于WAL格式元数据, 当压缩重新生成空 WAL时返回检查站。
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

            // 只有在检查站和WAL尾巴作为一份逻辑日记流出后才完成。
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
            // 压缩通过WAL元数据发布, 所以首先创建WAL, 如果发现没有, 将会检查ETag。
            await EnsureWalAsync(nameof(ReplaceAsync), cancellationToken).ConfigureAwait(false);

            var expectedWalETag = _walETag!.Value;
            var expectedProviderState = _walProviderState;
            WalState? walState;
            try
            {
                // 阅读WAL显示,压缩保存呼叫者拥有的元数据,而取代供应商拥有的检查站元数据。
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
                // 检查站的布料是不可变换的,内容用随机快照识别码处理,以便在碰撞时安全重试。
                var checkpointName = GetCheckpointName(Guid.NewGuid().ToString("N"));
                var checkpointClient = GetCheckpointClient(checkpointName);
                try
                {
                    // 上传检查哨后, 从 WAL 元数据发布前先上传检查点, 所以上传失败后恢复不变 。
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
                    // 抓图id是随机的, 所以这应该很少消失。 用新的ID重新试镜 。
                    _shared.Instruments.OnRetry("checkpoint_collision");
                    continue;
                }

                for (var attempt = 0; ; attempt++)
                {
                    var publishWalState = walState.Value;
                    try
                    {
                        // 重新创建位于ETag下的WAL, 只有在现有的WAL没有改变的情况下才能公布检查站。
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
                    // 保留以前的检查站,直到新的WAL公布为止,因此其清单从未在缺失的空隙处点到过点。
                    await DeleteCheckpointIfExistsAsync(previousCheckpointName, cancellationToken).ConfigureAwait(false);
                }

                LogReplace(_shared.Logger, checkpointClient.BlobContainerName, checkpointClient.Name, checkpointStream.Length);

                // 目前,可从WAL元数据进入新的检查站,旧状态已经分离。
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
        // Azure 拒绝过大附加块大小, 所以无法使用本地的日记专要指南 。
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
        // 在 Azure硬块盖前停止,以便打电话者可以在仍有附件主机时缩放。
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
            // 实例显示,没有WAL,所以它只是在WAL仍然不存在的情况下才创建WAL。
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
            // 只读取 WAL 属性和元数据; 突变状态不需要日记字节 。
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

    // 元数据操作从未建立或移动内容的先决条件:它们只更新WAL内容的ETag
    // 此实例将存在版本 。
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
        // 一次性解决供应商特有的WAL客户;检查站客户以公布姓名的方式解决。
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
        // 创建附加体的布罗布也是压实式公布新的WAL清单的方式。
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
        // 内容类型是可选的传输元数据;日记格式在布洛元数据中单独记录。
        return contentType is { Length: > 0 } ? new BlobHttpHeaders { ContentType = contentType } : null;
    }

    private string GetCheckpointName(string snapshotId)
    {
        // 让配置的供应商拥有检查站命名, 使自定义布局与WAL 命名保持一致 。
        var checkpointName = _shared.BlobClientProvider.GetCheckpointName(_journalId, snapshotId);
        if (string.IsNullOrWhiteSpace(checkpointName))
        {
            throw new InvalidOperationException("The configured Azure Blob journal checkpoint client provider returned an empty blob name.");
        }

        return checkpointName;
    }

    private BlockBlobClient GetCheckpointClient(string checkpointName)
    {
        // 由公布的检查站名称决定,以便恢复工作能够开放旧事件造成的检查站。
        var client = _shared.BlobClientProvider.GetCheckpointClient(_journalId, checkpointName);
        return client ?? throw new InvalidOperationException("The configured Azure Blob journal checkpoint client provider returned null.");
    }

    private async ValueTask DeleteCheckpointIfExistsAsync(string checkpointName, CancellationToken cancellationToken)
    {
        var checkpointClient = GetCheckpointClient(checkpointName);
        try
        {
            // 废弃检查站清理工作最努力,因为已公布的WAL不再提及。
            await checkpointClient.DeleteIfExistsAsync(DeleteSnapshotsOption.None, conditions: null, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception)
        {
            LogCheckpointCleanupFailure(_shared.Logger, checkpointClient.BlobContainerName, checkpointClient.Name, exception);
        }
    }

    private Dictionary<string, string> CreateMetadataDictionary()
    {
        // 以WAL和检查站布局通用元数据开始,以便回收能够验证日记格式。
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
        // 物价交易元数据是回收清单:通用格式加上任择检查站指示器和物价交易折合。
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
        // 解码WAL舱单,接受没有检查站指示的不规范的WAL。
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
        // 如果所申报的日记格式不同,拒绝同时缝合检查站和WAL数据。
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
    /// 返回为真实,如果 Azure 回复表示已密封了附加布(HTTP 409/ BlobIsSealed)。
    /// 密封的WAL永久关闭,以接收新的附件;在回收前必须恢复日记层
    /// 任何进一步写作都可以进行。
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
    /// 当 Azure 回复表示WAL自我们藏匿的ETag以来就变异时返回为真实
    /// 捕获的有:HTTP 404(WAL删除/重建)、HTTP 412(先决条件失败/ IfMatch)
    /// 被拒绝),或 HTTP 409 与 <c>ConditionNotMet</c> (相匹配的写法冲突) 。当此时
    /// 返回正返回正返回,呼叫者应尝试
    /// <see cref="RetryAfterMetadataOnlyConflictAsync"/> 以刷新当
    /// 变化是只使用元数据,否则会传播
    /// 触发日记层回收的<see cref="Orleans.Storage.InconsistentStateException"/>
    /// 由 Azure处理中程运输故障(HTTP 5xxx、网络错误、超时)
    /// SDK的内在再研究政策 永远不能到达这个分类器
    /// </summary>
    private static bool IsWalMutationConflict(RequestFailedException exception)
    {
        // 这些失败意味着我们隐藏的WAL视图已经过时或消失,所以打电话者必须在重新尝试之前恢复。
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
