namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 测试多抓集的接口 grain
/// </summary>
public interface ITestMultiCollectionGrain : IGrainWithGuidKey
{
    Task<Guid> GetActivationId();

    // 词典操作
    Task AddToDictionary(string key, int value);
    Task RemoveFromDictionary(string key);
    Task<int> GetDictionaryValue(string key);
    Task<int> GetDictionaryCount();

    // 名单行动
    Task AddToList(string item);
    Task RemoveListItemAt(int index);
    Task<string> GetListItem(int index);
    Task<int> GetListCount();

    // 列业务
    Task AddToQueue(int item);
    Task<int> DequeueItem();
    Task<int> PeekQueueItem();
    Task<int> GetQueueCount();

    // 设置操作
    Task AddToSet(string item);
    Task RemoveFromSet(string item);
    Task<bool> ContainsSetItem(string item);
    Task<int> GetSetCount();
}
