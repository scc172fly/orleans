namespace Orleans.Storage;

/// <summary>
/// <see cref="AdoNetGrainStorage"/> 当前使用的查询的容器类。
/// </summary>
/// <remarks>将其作为独立实体提供，以便可以动态更新这些查询。</remarks>
public class RelationalStorageProviderQueries
{
    /// <summary>
    /// 用于写入存储的语句。
    /// </summary>
    public string WriteToStorage { get; }

    /// <summary>
    /// 用于读取存储的语句。
    /// </summary>
    public string ReadFromStorage { get; set; }

    /// <summary>
    /// 用于清除存储的语句。
    /// </summary>
    public string ClearState { get; set; }

    /// <summary>
    /// 清除状态时从存储删除对应行的语句。
    /// </summary>
    public string? DeleteState { get; set; }

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="writeToStorage">用于写入存储的语句。</param>
    /// <param name="readFromStorage">用于读取存储的语句。</param>
    /// <param name="clearState">用于清除存储的语句。</param>
    /// <param name="deleteState">用于从存储删除行的语句。为保持向后兼容性，可能为 <see langword="null"/>。</param>
    public RelationalStorageProviderQueries(string writeToStorage, string readFromStorage, string clearState, string? deleteState)
    {
        ArgumentNullException.ThrowIfNull(writeToStorage);
        ArgumentNullException.ThrowIfNull(readFromStorage);
        ArgumentNullException.ThrowIfNull(clearState);

        // 为保持向后兼容性，不对 deleteState 进行 null 检查。

        WriteToStorage = writeToStorage;
        ReadFromStorage = readFromStorage;
        ClearState = clearState;
        DeleteState = deleteState;
    }
}
