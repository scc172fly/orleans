using System;
using System.Collections.Generic;

namespace Orleans.Streams;

/// <summary>
/// 描述获取队列缓存游标的结果类型。
/// </summary>
public enum QueueCacheCursorResultKind
{
    /// <summary>
    /// 结果尚未初始化。
    /// </summary>
    Invalid,

    /// <summary>
    /// 已获取游标。
    /// </summary>
    Success,

    /// <summary>
    /// 请求的位置早于缓存中保留的消息。
    /// </summary>
    CacheMiss,

    /// <summary>
    /// 缓存不支持请求的操作。
    /// </summary>
    NotSupported,
}

/// <summary>
/// 描述推进队列缓存游标的结果类型。
/// </summary>
public enum QueueCacheCursorMoveResultKind
{
    /// <summary>
    /// 结果尚未初始化。
    /// </summary>
    Invalid,

    /// <summary>
    /// 游标已前进至一条消息。
    /// </summary>
    Success,

    /// <summary>
    /// 当前没有可用消息。
    /// </summary>
    NoData,

    /// <summary>
    /// 游标位置早于缓存所保留的消息。
    /// </summary>
    CacheMiss,
}

/// <summary>
/// 描述队列缓存未命中的情况。
/// </summary>
public readonly struct QueueCacheMissInfo : IEquatable<QueueCacheMissInfo>
{
    private readonly object? _requested;
    private readonly object? _low;
    private readonly object? _high;

    /// <summary>
    /// 初始化 <see cref="QueueCacheMissInfo"/> 结构的新实例。
    /// </summary>
    /// <param name="requested">请求的序列令牌。</param>
    /// <param name="low">最早可用的序列令牌。</param>
    /// <param name="high">最新可用的序列令牌。</param>
    public QueueCacheMissInfo(StreamSequenceToken requested, StreamSequenceToken low, StreamSequenceToken high)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        _requested = requested;
        _low = low;
        _high = high;
    }

    /// <summary>
    /// 初始化 <see cref="QueueCacheMissInfo"/> 结构的新实例。
    /// </summary>
    /// <param name="requested">请求的序列令牌。</param>
    /// <param name="low">最早可用的序列令牌。</param>
    /// <param name="high">最新可用的序列令牌。</param>
    public QueueCacheMissInfo(string? requested, string? low, string? high)
    {
        _requested = requested;
        _low = low;
        _high = high;
    }

    /// <summary>
    /// 获取请求的序列令牌。
    /// </summary>
    public string? Requested => _requested?.ToString();

    /// <summary>
    /// 获取最早可用的序列令牌。
    /// </summary>
    public string? Low => _low?.ToString();

    /// <summary>
    /// 获取最新可用的序列令牌。
    /// </summary>
    public string? High => _high?.ToString();

    /// <summary>
    /// 当请求的序列令牌可用时获取它。
    /// </summary>
    public StreamSequenceToken? RequestedToken => _requested as StreamSequenceToken;

    /// <summary>
    /// 当最早可用的序列令牌存在时获取它。
    /// </summary>
    public StreamSequenceToken? LowToken => _low as StreamSequenceToken;

    /// <summary>
    /// 当最新可用的序列令牌存在时获取它。
    /// </summary>
    public StreamSequenceToken? HighToken => _high as StreamSequenceToken;

    /// <inheritdoc/>
    public bool Equals(QueueCacheMissInfo other) =>
        Equals(_requested, other._requested)
        && Equals(_low, other._low)
        && Equals(_high, other._high);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QueueCacheMissInfo other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_requested, _low, _high);

    /// <summary>
    /// 确定两个缓存未命中描述是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheMissInfo left, QueueCacheMissInfo right) => left.Equals(right);

    /// <summary>
    /// 确定两个缓存未命中描述是否不相等。
    /// </summary>
    public static bool operator !=(QueueCacheMissInfo left, QueueCacheMissInfo right) => !left.Equals(right);

    /// <summary>
    /// 创建表示此次缓存未命中的异常。
    /// </summary>
    /// <returns>队列缓存未命中异常。</returns>
    public QueueCacheMissException ToException() => new(Requested, Low, High);
}

/// <summary>
/// 表示获取队列缓存游标的结果。
/// </summary>
/// <typeparam name="TCursor">游标类型。</typeparam>
/// <remarks>
/// <see cref="QueueCacheCursorResultKind.Success"/> 结果包含非 null 的 <see cref="Cursor"/>。
/// 所有其他结果均不包含游标。<see cref="QueueCacheCursorResultKind.CacheMiss"/> 结果
/// 包含 <see cref="CacheMiss"/> 详细信息，其他结果则不包含缓存未命中详情。
/// </remarks>
public readonly struct QueueCacheCursorResult<TCursor> : IEquatable<QueueCacheCursorResult<TCursor>> where TCursor : class
{
    private readonly TCursor? _cursor;
    private readonly QueueCacheMissInfo _cacheMiss;

    private QueueCacheCursorResult(
        QueueCacheCursorResultKind kind,
        TCursor? cursor = null,
        QueueCacheMissInfo cacheMiss = default)
    {
        Kind = kind;
        _cursor = cursor;
        _cacheMiss = cacheMiss;
    }

    /// <summary>
    /// 获取结果种类。
    /// </summary>
    public QueueCacheCursorResultKind Kind { get; }

    /// <summary>
    /// 当 <see cref="Kind"/> 为 <see cref="QueueCacheCursorResultKind.Success"/> 时，获取所获取的游标。
    /// </summary>
    public TCursor? Cursor => Kind == QueueCacheCursorResultKind.Success ? _cursor : null;

    /// <summary>
    /// 当 <see cref="Kind"/> 为 <see cref="QueueCacheCursorResultKind.CacheMiss"/> 时，获取缓存未命中的详细信息。
    /// </summary>
    public QueueCacheMissInfo? CacheMiss => Kind == QueueCacheCursorResultKind.CacheMiss ? _cacheMiss : null;

    /// <inheritdoc/>
    public bool Equals(QueueCacheCursorResult<TCursor> other) =>
        Kind == other.Kind
        && EqualityComparer<TCursor?>.Default.Equals(_cursor, other._cursor)
        && _cacheMiss.Equals(other._cacheMiss);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QueueCacheCursorResult<TCursor> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _cursor, _cacheMiss);

    /// <summary>
    /// 确定两个游标获取结果是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheCursorResult<TCursor> left, QueueCacheCursorResult<TCursor> right) => left.Equals(right);

    /// <summary>
    /// 确定两个游标获取结果是否不相等。
    /// </summary>
    public static bool operator !=(QueueCacheCursorResult<TCursor> left, QueueCacheCursorResult<TCursor> right) => !left.Equals(right);

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="cursor">获取的游标。</param>
    /// <returns>成功的游标获取结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cursor"/> 为 <see langword="null"/>。</exception>
    public static QueueCacheCursorResult<TCursor> FromCursor(TCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return new(QueueCacheCursorResultKind.Success, cursor);
    }

    /// <summary>
    /// 创建缓存未命中结果。
    /// </summary>
    /// <param name="cacheMiss">缓存未命中详情。</param>
    /// <returns>缓存未命中结果。</returns>
    public static QueueCacheCursorResult<TCursor> FromCacheMiss(QueueCacheMissInfo cacheMiss)
        => new(QueueCacheCursorResultKind.CacheMiss, cacheMiss: cacheMiss);

    /// <summary>
    /// 获取表示操作不受支持的结果。
    /// </summary>
    public static QueueCacheCursorResult<TCursor> NotSupported { get; } = new(QueueCacheCursorResultKind.NotSupported);
}

/// <summary>
/// 表示推进队列缓存游标的结果。
/// </summary>
/// <remarks>
/// <see cref="QueueCacheCursorMoveResultKind.Success"/> 结果表示游标具有有效的
/// 当前项。其他结果均表示未产生当前项。若结果为
/// <see cref="QueueCacheCursorMoveResultKind.CacheMiss"/>，则包含 <see cref="CacheMiss"/> 详细信息。
/// </remarks>
public readonly struct QueueCacheCursorMoveResult : IEquatable<QueueCacheCursorMoveResult>
{
    private readonly QueueCacheMissInfo _cacheMiss;

    private QueueCacheCursorMoveResult(
        QueueCacheCursorMoveResultKind kind,
        QueueCacheMissInfo cacheMiss = default)
    {
        Kind = kind;
        _cacheMiss = cacheMiss;
    }

    /// <summary>
    /// 获取结果种类。
    /// </summary>
    public QueueCacheCursorMoveResultKind Kind { get; }

    /// <summary>
    /// 当 <see cref="Kind"/> 为 <see cref="QueueCacheCursorMoveResultKind.CacheMiss"/> 时，获取缓存未命中的详细信息。
    /// </summary>
    public QueueCacheMissInfo? CacheMiss => Kind == QueueCacheCursorMoveResultKind.CacheMiss ? _cacheMiss : null;

    /// <inheritdoc/>
    public bool Equals(QueueCacheCursorMoveResult other) =>
        Kind == other.Kind && _cacheMiss.Equals(other._cacheMiss);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QueueCacheCursorMoveResult other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, _cacheMiss);

    /// <summary>
    /// 确定两个游标移动结果是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheCursorMoveResult left, QueueCacheCursorMoveResult right) => left.Equals(right);

    /// <summary>
    /// 确定两个游标移动结果是否不相等。
    /// </summary>
    public static bool operator !=(QueueCacheCursorMoveResult left, QueueCacheCursorMoveResult right) => !left.Equals(right);

    /// <summary>
    /// 获取游标成功前进的结果。
    /// </summary>
    public static QueueCacheCursorMoveResult Success { get; } = new(QueueCacheCursorMoveResultKind.Success);

    /// <summary>
    /// 获取表示当前没有可用消息的结果。
    /// </summary>
    public static QueueCacheCursorMoveResult NoData { get; } = new(QueueCacheCursorMoveResultKind.NoData);

    /// <summary>
    /// 创建缓存未命中结果。
    /// </summary>
    /// <param name="cacheMiss">缓存未命中详情。</param>
    /// <returns>缓存未命中结果。</returns>
    public static QueueCacheCursorMoveResult FromCacheMiss(QueueCacheMissInfo cacheMiss)
        => new(QueueCacheCursorMoveResultKind.CacheMiss, cacheMiss);
}
