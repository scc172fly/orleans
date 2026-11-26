using System;
using System.Collections.Generic;

namespace Orleans.Streams;

/// <summary>
/// 描述获取队列缓存光标的结果 。
/// </summary>
public enum QueueCacheCursorResultKind
{
    /// <summary>
    /// 尚未初始化结果 。
    /// </summary>
    Invalid,

    /// <summary>
    /// 获取了光标。
    /// </summary>
    Success,

    /// <summary>
    /// 请求的位置比缓存保存的信件要老。
    /// </summary>
    CacheMiss,

    /// <summary>
    /// 请求的操作没有缓存支持 。
    /// </summary>
    NotSupported,
}

/// <summary>
/// 描述推进队列缓存光标的结果 。
/// </summary>
public enum QueueCacheCursorMoveResultKind
{
    /// <summary>
    /// 尚未初始化结果 。
    /// </summary>
    Invalid,

    /// <summary>
    /// 光标推进到一条消息 。
    /// </summary>
    Success,

    /// <summary>
    /// 目前没有可用的信件 。
    /// </summary>
    NoData,

    /// <summary>
    /// 光标位置比缓存保存的信件要老。
    /// </summary>
    CacheMiss,
}

/// <summary>
/// 描述队列缓存失误 。
/// </summary>
public readonly struct QueueCacheMissInfo : IEquatable<QueueCacheMissInfo>
{
    private readonly object? _requested;
    private readonly object? _low;
    private readonly object? _high;

    /// <summary>
    /// 初始化 <see cref="QueueCacheMissInfo"/> 结构的新实例。
    /// </summary>
    /// <param name="requested">请求的序列符号。</param>
    /// <param name="low">最早可用的序列符号。</param>
    /// <param name="high">最新的可用序列符号。</param>
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
    /// <param name="requested">请求的序列符号。</param>
    /// <param name="low">最早可用的序列符号。</param>
    /// <param name="high">最新的可用序列符号。</param>
    public QueueCacheMissInfo(string? requested, string? low, string? high)
    {
        _requested = requested;
        _low = low;
        _high = high;
    }

    /// <summary>
    /// 获取请求的序列符号 。
    /// </summary>
    public string? Requested => _requested?.ToString();

    /// <summary>
    /// 获得最早可用的序列符号 。
    /// </summary>
    public string? Low => _low?.ToString();

    /// <summary>
    /// 获取最新可用的序列符号 。
    /// </summary>
    public string? High => _high?.ToString();

    /// <summary>
    /// 当有要求序列符号时,获取该符号。
    /// </summary>
    public StreamSequenceToken? RequestedToken => _requested as StreamSequenceToken;

    /// <summary>
    /// 当有序列符号时, 获取最早可用的序列符号 。
    /// </summary>
    public StreamSequenceToken? LowToken => _low as StreamSequenceToken;

    /// <summary>
    /// 获取可用的最新序列符号。
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
    /// 确定两个缓存缺失描述是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheMissInfo left, QueueCacheMissInfo right) => left.Equals(right);

    /// <summary>
    /// 确定两个缓存漏记是否不均匀。
    /// </summary>
    public static bool operator !=(QueueCacheMissInfo left, QueueCacheMissInfo right) => !left.Equals(right);

    /// <summary>
    /// 创建代表此缓存错误的例外 。
    /// </summary>
    /// <returns>队列缓存错误例外。</returns>
    public QueueCacheMissException ToException() => new(Requested, Low, High);
}

/// <summary>
/// 表示获取队列缓存光标的结果。
/// </summary>
/// <typeparam name="TCursor">光标类型。</typeparam>
/// <remarks>
/// <see cref="QueueCacheCursorResultKind.Success"/> 结果包含非null <see cref="Cursor"/>。
/// 所有其他结果都不包含光标。 <see cref="QueueCacheCursorResultKind.CacheMiss"/> 结果结果结果
/// 包含 <see cref="CacheMiss"/> 详细信息,而其他结果则不含缓存漏漏漏细节。
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
    /// 得到的结果类型。
    /// </summary>
    public QueueCacheCursorResultKind Kind { get; }

    /// <summary>
    /// 当 <see cref="Kind"/> 是 <see cref="QueueCacheCursorResultKind.Success"/>时获取获得的光标。
    /// </summary>
    public TCursor? Cursor => Kind == QueueCacheCursorResultKind.Success ? _cursor : null;

    /// <summary>
    /// 当 <see cref="Kind"/> 是 <see cref="QueueCacheCursorResultKind.CacheMiss"/>时获取缓存丢失的细节 。
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
    /// 确定两个光标获取结果是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheCursorResult<TCursor> left, QueueCacheCursorResult<TCursor> right) => left.Equals(right);

    /// <summary>
    /// 确定两个光标获取结果是否不平等。
    /// </summary>
    public static bool operator !=(QueueCacheCursorResult<TCursor> left, QueueCacheCursorResult<TCursor> right) => !left.Equals(right);

    /// <summary>
    /// 创建成功的结果 。
    /// </summary>
    /// <param name="cursor">获得的光标。</param>
    /// <returns>一个成功的光标结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cursor"/> 是 <see langword="null"/>.</exception>
    public static QueueCacheCursorResult<TCursor> FromCursor(TCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return new(QueueCacheCursorResultKind.Success, cursor);
    }

    /// <summary>
    /// 创建缓存错误结果 。
    /// </summary>
    /// <param name="cacheMiss">缓存丢失细节。</param>
    /// <returns>缓存错误结果。</returns>
    public static QueueCacheCursorResult<TCursor> FromCacheMiss(QueueCacheMissInfo cacheMiss)
        => new(QueueCacheCursorResultKind.CacheMiss, cacheMiss: cacheMiss);

    /// <summary>
    /// 获得一个结果,显示操作不支持。
    /// </summary>
    public static QueueCacheCursorResult<TCursor> NotSupported { get; } = new(QueueCacheCursorResultKind.NotSupported);
}

/// <summary>
/// 表示推进队列缓存光标的结果。
/// </summary>
/// <remarks>
/// <see cref="QueueCacheCursorMoveResultKind.Success"/> 结果显示光标有一个有效的
/// 所有其他结果显示,没有产生当前项目。
/// <see cref="QueueCacheCursorMoveResultKind.CacheMiss"/> 结果包含 <see cref="CacheMiss"/> 细节。
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
    /// 得到的结果类型。
    /// </summary>
    public QueueCacheCursorMoveResultKind Kind { get; }

    /// <summary>
    /// 当 <see cref="Kind"/> 是 <see cref="QueueCacheCursorMoveResultKind.CacheMiss"/>时获取缓存丢失的细节 。
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
    /// 确定两个光标移动结果是否相等。
    /// </summary>
    public static bool operator ==(QueueCacheCursorMoveResult left, QueueCacheCursorMoveResult right) => left.Equals(right);

    /// <summary>
    /// 确定两个光标移动结果是否不平等。
    /// </summary>
    public static bool operator !=(QueueCacheCursorMoveResult left, QueueCacheCursorMoveResult right) => !left.Equals(right);

    /// <summary>
    /// 获得光标推进成功结果 。
    /// </summary>
    public static QueueCacheCursorMoveResult Success { get; } = new(QueueCacheCursorMoveResultKind.Success);

    /// <summary>
    /// 获取显示目前没有消息的输出结果 。
    /// </summary>
    public static QueueCacheCursorMoveResult NoData { get; } = new(QueueCacheCursorMoveResultKind.NoData);

    /// <summary>
    /// 创建缓存错误结果 。
    /// </summary>
    /// <param name="cacheMiss">缓存丢失细节。</param>
    /// <returns>缓存错误结果。</returns>
    public static QueueCacheCursorMoveResult FromCacheMiss(QueueCacheMissInfo cacheMiss)
        => new(QueueCacheCursorMoveResultKind.CacheMiss, cacheMiss);
}
