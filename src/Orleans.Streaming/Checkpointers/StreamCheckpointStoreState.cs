using System;

namespace Orleans.Streams;

/// <summary>
/// 表示已持久化的流检查点及其后端版本。
/// </summary>
public readonly struct StreamCheckpointStoreState : IEquatable<StreamCheckpointStoreState>
{
    /// <summary>
    /// 初始化 <see cref="StreamCheckpointStoreState"/> 结构的新实例。
    /// </summary>
    /// <param name="checkpoint">检查点值。</param>
    /// <param name="version">后端版本或实体标记（ETag）。</param>
    public StreamCheckpointStoreState(string checkpoint, string version)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(version);
        Checkpoint = checkpoint;
        Version = version;
    }

    /// <summary>
    /// 获取检查点值。
    /// </summary>
    public string Checkpoint { get; }

    /// <summary>
    /// 获取后端版本或实体标记（ETag）。
    /// </summary>
    public string Version { get; }

    /// <inheritdoc />
    public bool Equals(StreamCheckpointStoreState other)
        => string.Equals(Checkpoint, other.Checkpoint, StringComparison.Ordinal)
        && string.Equals(Version, other.Version, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is StreamCheckpointStoreState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Checkpoint, Version);

    public static bool operator ==(StreamCheckpointStoreState left, StreamCheckpointStoreState right) => left.Equals(right);

    public static bool operator !=(StreamCheckpointStoreState left, StreamCheckpointStoreState right) => !left.Equals(right);
}
