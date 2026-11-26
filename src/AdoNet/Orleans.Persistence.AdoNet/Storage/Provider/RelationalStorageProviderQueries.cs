namespace Orleans.Storage;

/// <summary>
/// 用于 <see cref="AdoNetGrainStorage"/>目前使用的查询的容器类。
/// </summary>
/// <remarks>这是一项单独实体的规定,目的是使这些实体动态地可更新。</remarks>
public class RelationalStorageProviderQueries
{
    /// <summary>
    /// 写到存储处的条款。
    /// </summary>
    public string WriteToStorage { get; }

    /// <summary>
    /// 储存处的这一条款。
    /// </summary>
    public string ReadFromStorage { get; set; }

    /// <summary>
    /// 清除储藏的条款。
    /// </summary>
    public string ClearState { get; set; }

    /// <summary>
    /// 清除状态时删除存储行的条款 。
    /// </summary>
    public string? DeleteState { get; set; }

    /// <summary>
    /// 构造器
    /// </summary>
    /// <param name="writeToStorage">将文字写入存储的条款。</param>
    /// <param name="readFromStorage">该条款是从储存处读取的。</param>
    /// <param name="clearState">清除储藏的条款。</param>
    /// <param name="deleteState">从存储处删除行的条款。 可能为后向兼容性 <see langword="null"/> 。</param>
    public RelationalStorageProviderQueries(string writeToStorage, string readFromStorage, string clearState, string? deleteState)
    {
        ArgumentNullException.ThrowIfNull(writeToStorage);
        ArgumentNullException.ThrowIfNull(readFromStorage);
        ArgumentNullException.ThrowIfNull(clearState);

        // 在 deleteState 上没有反向兼容性的空格检查 。

        WriteToStorage = writeToStorage;
        ReadFromStorage = readFromStorage;
        ClearState = clearState;
        DeleteState = deleteState;
    }
}
