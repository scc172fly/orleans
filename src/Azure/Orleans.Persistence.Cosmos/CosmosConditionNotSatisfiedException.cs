using System.Runtime.Serialization;
using Orleans.Storage;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 存储提供程序尝试执行 WriteStateAsync 操作时检测到 ETag 不一致所抛出的异常。
/// </summary>
[Serializable]
[GenerateSerializer]
public class CosmosConditionNotSatisfiedException : InconsistentStateException
{
    private const string DefaultMessageFormat = "Cosmos DB condition not satisfied. GrainType: {0}, GrainId: {1}, TableName: {2}, StoredETag: {3}, CurrentETag: {4}";

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    public CosmosConditionNotSatisfiedException(
        string errorMsg,
        string grainType,
        GrainId grainId,
        string collection,
        string storedEtag,
        string? currentEtag)
        : base(errorMsg, storedEtag, currentEtag)
    {
        GrainType = grainType;
        GrainId = grainId.ToString();
        Collection = collection;
    }

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    public CosmosConditionNotSatisfiedException(
        string grainType,
        GrainId grainId,
        string collection,
        string storedEtag,
        string? currentEtag)
        : this(CreateDefaultMessage(grainType, grainId, collection, storedEtag, currentEtag), grainType, grainId, collection, storedEtag, currentEtag)
    {
    }

    /// <summary>
    /// 获取受影响的 Grain 的 ID。
    /// </summary>
    [Id(0)]
    public string GrainId { get; } = default!;

    /// <summary>
    /// 获取受影响的 Grain 的类型。
    /// </summary>
    [Id(1)]
    public string GrainType { get; } = default!;

    /// <summary>
    /// 获取集合名称。
    /// </summary>
    [Id(2)]
    public string Collection { get; } = default!;

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    public CosmosConditionNotSatisfiedException()
    {
    }

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    public CosmosConditionNotSatisfiedException(string msg)
        : base(msg)
    {
    }

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    public CosmosConditionNotSatisfiedException(string msg, Exception exc)
        : base(msg, exc)
    {
    }

    private static string CreateDefaultMessage(
        string grainType,
        GrainId grainId,
        string collection,
        string storedEtag,
        string? currentEtag) => string.Format(DefaultMessageFormat, grainType, grainId, collection, storedEtag, currentEtag);

    /// <summary>
    /// 在因不满足更新条件而抛出 Cosmos DB 异常时抛出的异常。
    /// </summary>
    [Obsolete]
    protected CosmosConditionNotSatisfiedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        GrainType = info.GetString("GrainType")!;
        GrainId = info.GetString("GrainId")!;
        Collection = info.GetString("Collection")!;
    }

    /// <inheritdoc />
    [Obsolete]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));

        info.AddValue("GrainType", GrainType);
        info.AddValue("GrainId", GrainId);
        info.AddValue("Collection", Collection);
        base.GetObjectData(info, context);
    }
}
