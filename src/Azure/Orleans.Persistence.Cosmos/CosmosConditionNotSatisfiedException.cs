using System.Runtime.Serialization;
using Orleans.Storage;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// 当储存提供者在试图进行 WriteStateAsync 操作时发现Etag不一致时,就提出例外。
/// </summary>
[Serializable]
[GenerateSerializer]
public class CosmosConditionNotSatisfiedException : InconsistentStateException
{
    private const string DefaultMessageFormat = "Cosmos DB condition not satisfied. GrainType: {0}, GrainId: {1}, TableName: {2}, StoredETag: {3}, CurrentETag: {4}";

    /// <summary>
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
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
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
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
    /// 获取受影响者的代号 grain。
    /// </summary>
    [Id(0)]
    public string GrainId { get; } = default!;

    /// <summary>
    /// 获取受影响者的 grain 类型 grain。
    /// </summary>
    [Id(1)]
    public string GrainType { get; } = default!;

    /// <summary>
    /// 获取收藏名称
    /// </summary>
    [Id(2)]
    public string Collection { get; } = default!;

    /// <summary>
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
    /// </summary>
    public CosmosConditionNotSatisfiedException()
    {
    }

    /// <summary>
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
    /// </summary>
    public CosmosConditionNotSatisfiedException(string msg)
        : base(msg)
    {
    }

    /// <summary>
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
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
    /// 宇宙DB例外因不符合更新条件而被抛出例外。
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
