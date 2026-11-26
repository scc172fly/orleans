using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Orleans.Durable.Journaling.Json;
using Orleans.Storage;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供由日记id所查明的共享的模拟日记存储实例。
/// </summary>
public sealed class VolatileJournalStorageProvider : IJournalStorageProvider, IJournalStorageCatalog
{
    private const string ProviderName = "volatile";
    private readonly IOptions<JournaledStateManagerOptions>? _options;
    private readonly ConcurrentDictionary<string, VolatileJournalStorage.Store> _storage = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _storageKeys = new(StringComparer.Ordinal);
    private readonly object _catalogLock = new();
    private readonly JournalStorageTelemetry _telemetry;
    private readonly VolatileJournalStorageOptions _storageOptions = new();

    /// <summary>
    /// 使用默认日记格式初始化 <see cref="VolatileJournalStorageProvider"/> 类的新实例。
    /// </summary>
    public VolatileJournalStorageProvider()
    {
        _telemetry = JournalStorageTelemetry.CreateForDirectConstruction();
    }

    /// <summary>
    /// 初始化 <see cref="VolatileJournalStorageProvider"/> 类的新实例。
    /// </summary>
    /// <param name="options">被记录的国家管理选项。</param>
    public VolatileJournalStorageProvider(IOptions<JournaledStateManagerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _telemetry = JournalStorageTelemetry.CreateForDirectConstruction();
    }

    /// <summary>
    /// 使用配置的 Orleans 公尺计量表初始化一个模拟供应商。
    /// </summary>
    /// <param name="options">被记录的国家管理选项。</param>
    /// <param name="instruments">运行时 Orleans 公 计。</param>
    public VolatileJournalStorageProvider(IOptions<JournaledStateManagerOptions> options, OrleansInstruments instruments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instruments);
        _options = options;
        _telemetry = new JournalStorageTelemetry(instruments);
    }

    /// <summary>
    /// 初始化了一个具有可配置快照阈值的模拟供应商。
    /// </summary>
    /// <param name="options">被记录的国家管理选项。</param>
    /// <param name="storageOptions">供应商的快照门槛</param>
    /// <param name="instruments">任选 Orleans 运行时公尺计。</param>
    public VolatileJournalStorageProvider(
        IOptions<JournaledStateManagerOptions> options,
        IOptions<VolatileJournalStorageOptions> storageOptions,
        OrleansInstruments? instruments) : this(options)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        var configuration = storageOptions.Value;
        configuration.Validate();
        _storageOptions = new()
        {
            MaxAppendsBeforeSnapshot = configuration.MaxAppendsBeforeSnapshot,
            MaxBytesBeforeSnapshot = configuration.MaxBytesBeforeSnapshot
        };
        if (instruments is not null)
        {
            _telemetry = new JournalStorageTelemetry(instruments);
        }
    }

    /// <inheritdoc/>
    public IJournalStorage CreateStorage(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var journalFormatKey = GetJournalFormatKey();
        if (!_storage.TryGetValue(journalId.Value, out var store))
        {
            lock (_catalogLock)
            {
                if (!_storage.TryGetValue(journalId.Value, out store))
                {
                    store = new VolatileJournalStorage.Store(journalId.Value);
                    _storageKeys.Add(journalId.Value);
                    _storage[journalId.Value] = store;
                }
            }
        }

        return new VolatileJournalStorage(store, journalFormatKey, _storageOptions);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<JournalCatalogEntry> ListAsync(
        JournalCatalogListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var range = new JournalCatalogRange(options);
        if (range.IsEmpty)
        {
            yield break;
        }

        string[] keys;
        lock (_catalogLock)
        {
            if (_storageKeys.Count == 0)
            {
                keys = [];
            }
            else
            {
                var lower = range.LowerBound ?? _storageKeys.Min!;
                var upper = range.UpperBound ?? _storageKeys.Max!;
                keys = string.CompareOrdinal(lower, upper) <= 0
                    ? _storageKeys.GetViewBetween(lower, upper).ToArray()
                    : [];
            }
        }

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!range.Contains(key))
            {
                continue;
            }

            var store = _storage[key];
            IJournalMetadata? metadata = null;
            lock (store.SyncRoot)
            {
                if (!store.Exists)
                {
                    continue;
                }

                if (range.IncludeMetadata)
                {
                    metadata = store.GetMetadata();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            _telemetry.OnCatalogEntry(ProviderName);
            yield return new JournalCatalogEntry(new JournalId(key), metadata);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private string GetJournalFormatKey()
        => JournalFormatServices.ValidateJournalFormatKey(_options?.Value.JournalFormatKey ?? JsonLinesJournalFormat.JournalFormatKey);
}

/// <summary>
/// 用于非耐用用途的 <see cref="IJournalStorage"/> 在开发和测试等情况下的模拟、不稳定实施。
/// </summary>
public sealed class VolatileJournalStorage : IJournalStorage
{
    private readonly Store _store;
    // 读取或创建前不为空日记本, 零 。
    private long? _writeVersion;
    private readonly int _maxAppendsBeforeSnapshot;
    private readonly long _maxBytesBeforeSnapshot;
    private string? _configuredJournalFormatKey;

    /// <summary>
    /// 使用默认日记格式初始化一个新的孤立的日记存储实例。
    /// </summary>
    public VolatileJournalStorage() : this(new Store(CreateVolatileStorageId()), journalFormatKey: null)
    {
    }

    /// <summary>
    /// 初始化 <see cref="VolatileJournalStorage"/> 类的新实例。
    /// </summary>
    /// <param name="journalFormatKey">日记写时要加盖的密钥 。</param>
    public VolatileJournalStorage(string? journalFormatKey) : this(new Store(CreateVolatileStorageId()), journalFormatKey)
    {
    }

    /// <summary>
    /// 初始化一个带有可配置快照阈值的孤立的模拟日记。
    /// </summary>
    /// <param name="journalFormatKey">日记写时要加盖的密钥 。</param>
    /// <param name="options">快速阈值。</param>
    public VolatileJournalStorage(string? journalFormatKey, VolatileJournalStorageOptions options)
        : this(new Store(CreateVolatileStorageId()), journalFormatKey, options ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    internal VolatileJournalStorage(Store store, string? journalFormatKey, VolatileJournalStorageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        options ??= new();
        options.Validate();
        _store = store;
        _maxAppendsBeforeSnapshot = options.MaxAppendsBeforeSnapshot;
        _maxBytesBeforeSnapshot = options.MaxBytesBeforeSnapshot;
        SetConfiguredJournalFormatKey(journalFormatKey);
    }

    /// <inheritdoc/>
    public bool IsCompactionRequested
    {
        get
        {
            lock (_store.SyncRoot)
            {
                return _store.AppendCount >= _maxAppendsBeforeSnapshot || _store.AppendedBytes >= _maxBytesBeforeSnapshot;
            }
        }
    }

    internal IReadOnlyList<byte[]> Segments => _store.Segments;

    internal string? StoredJournalFormatKey
    {
        get
        {
            lock (_store.SyncRoot)
            {
                return _store.StoredJournalFormatKey;
            }
        }

        set
        {
            lock (_store.SyncRoot)
            {
                _store.StoredJournalFormatKey = value;
            }
        }
    }

    internal void SetConfiguredJournalFormatKey(string? journalFormatKey)
    {
        _configuredJournalFormatKey = journalFormatKey;
    }

    /// <inheritdoc/>
    public ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = JournalMetadata.CopyProperties(metadata);
        lock (_store.SyncRoot)
        {
            if (_store.Exists)
            {
                return new(false);
            }

            _store.Create(values);
            _writeVersion = _store.ContentVersion;
            return new(true);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_store.SyncRoot)
        {
            return new(_store.Exists ? _store.GetMetadata() : null);
        }
    }

    /// <inheritdoc/>
    public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
        IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var setValues = JournalMetadata.CopyProperties(set);
        var removeValues = CopyRemove(remove, setValues);
        lock (_store.SyncRoot)
        {
            if (!_store.Exists || expectedETag is not null && !string.Equals(expectedETag, _store.ETag, StringComparison.Ordinal))
            {
                return new((IJournalMetadata?)null);
            }

            _store.ApplyMetadataUpdate(setValues, removeValues);
            return new(_store.GetMetadata());
        }
    }

    /// <inheritdoc/>
    public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        cancellationToken.ThrowIfCancellationRequested();

        byte[][] segments;
        IJournalMetadata metadata;
        long version;
        lock (_store.SyncRoot)
        {
            if (_writeVersion is not null)
            {
                ThrowIfVersionChanged(nameof(ReadAsync));
            }

            metadata = _store.Exists ? _store.GetMetadata() : JournalMetadata.Empty;
            segments = _store.Segments.ToArray();
            version = _store.ContentVersion;
        }

        consumer.Read(GetSegments(segments, cancellationToken), metadata, complete: true);
        _writeVersion = version;
        return default;
    }

    private static IEnumerable<ReadOnlyMemory<byte>> GetSegments(IEnumerable<byte[]> segments, CancellationToken cancellationToken)
    {
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return segment;
        }
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(ReadOnlySequence<byte> segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfZero(segment.Length, nameof(segment));
        lock (_store.SyncRoot)
        {
            ThrowIfVersionChanged(nameof(AppendAsync));
            var data = segment.ToArray();
            _store.Exists = true;
            _store.StoredJournalFormatKey = _configuredJournalFormatKey;
            _store.Segments.Add(data);
            _store.AppendedBytes += segment.Length;
            _store.AppendCount++;
            _store.OnContentChanged();
            _writeVersion = _store.ContentVersion;
        }

        return default;
    }

    /// <inheritdoc/>
    public ValueTask ReplaceAsync(ReadOnlySequence<byte> snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = snapshot.ToArray();
        lock (_store.SyncRoot)
        {
            ThrowIfVersionChanged(nameof(ReplaceAsync));
            _store.Exists = true;
            _store.StoredJournalFormatKey = _configuredJournalFormatKey;
            _store.Segments.Clear();
            _store.Segments.Add(data);
            _store.AppendedBytes = 0;
            _store.AppendCount = 0;
            _store.OnContentChanged();
            _writeVersion = _store.ContentVersion;
        }

        return default;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_store.SyncRoot)
        {
            ThrowIfVersionChanged(nameof(DeleteAsync));
            _store.Delete();
            _writeVersion = 0;
        }

        return default;
    }

    // 打电话的人守着商店锁
    private void ThrowIfVersionChanged(string operation)
    {
        if (_writeVersion is null)
        {
            throw new InvalidOperationException($"{operation} requires reading or creating the journal first.");
        }

        var current = _store.ContentVersion;
        if (_writeVersion != current)
        {
            throw new InconsistentStateException(
                $"Version conflict ({operation}): the volatile journal '{_store}' changed since this storage instance last read or wrote it.",
                _writeVersion.Value.ToString(CultureInfo.InvariantCulture),
                current.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string CreateVolatileStorageId() => $"volatile/{Guid.NewGuid():N}";

    internal sealed class Store(string storageId)
    {
        public object SyncRoot { get; } = new();

        public List<byte[]> Segments { get; } = [];

        public long AppendCount { get; set; }

        // 列表数和每个存储的阵列长度都与内装相容,所以总合长。
        public long AppendedBytes { get; set; }

        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

        public string? StoredJournalFormatKey { get; set; }

        public bool Exists { get; set; }

        public long Version { get; private set; }

        // 计及内容变化(创建、写作和删除),这样内容版本就永远不会被再利用;元数据更新保持不变。
        private long _contentChangeCount;

        public long ContentVersion => Exists ? _contentChangeCount : 0;

        public string? ETag { get; private set; }

        public void OnContentChanged()
        {
            _contentChangeCount++;
            RefreshETag();
        }

        public void Create(IReadOnlyDictionary<string, string>? properties)
        {
            Exists = true;
            Segments.Clear();
            AppendCount = 0;
            AppendedBytes = 0;
            Properties.Clear();
            StoredJournalFormatKey = null;
            if (properties is not null)
            {
                foreach (var (key, value) in properties)
                {
                    Properties.Add(key, value);
                }
            }

            OnContentChanged();
        }

        public void Delete()
        {
            Exists = false;
            Segments.Clear();
            AppendCount = 0;
            AppendedBytes = 0;
            Properties.Clear();
            StoredJournalFormatKey = null;
            ETag = null;
            Version++;
            _contentChangeCount++;
        }

        public IJournalMetadata GetMetadata() => new JournalMetadata(StoredJournalFormatKey, ETag, Properties);

        public bool ApplyMetadataUpdate(IReadOnlyDictionary<string, string> set, IReadOnlySet<string> remove)
        {
            var changed = false;
            foreach (var propertyName in remove)
            {
                changed |= Properties.Remove(propertyName);
            }

            foreach (var (propertyName, value) in set)
            {
                if (!Properties.TryGetValue(propertyName, out var currentValue)
                    || !string.Equals(currentValue, value, StringComparison.Ordinal))
                {
                    Properties[propertyName] = value;
                    changed = true;
                }
            }

            if (changed)
            {
                RefreshETag();
            }

            return changed;
        }

        public string RefreshETag()
        {
            Exists = true;
            ETag = (++Version).ToString("D", CultureInfo.InvariantCulture);
            return ETag;
        }

        public override string ToString() => storageId;
    }

    private static IReadOnlySet<string> CopyRemove(IEnumerable<string>? remove, IReadOnlyDictionary<string, string> set)
    {
        if (remove is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in remove)
        {
            JournalMetadata.ValidateCallerPropertyName(key);
            if (set.ContainsKey(key))
            {
                throw new ArgumentException($"Journal metadata property '{key}' cannot be both set and removed.", nameof(remove));
            }

            result.Add(key);
        }

        return result;
    }
}
