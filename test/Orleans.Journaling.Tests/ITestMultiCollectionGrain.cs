namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 测试用多集合 Grain 的接口
/// </summary>
public interface ITestMultiCollectionGrain : IGrainWithGuidKey
{
    Task<Guid> GetActivationId();

    // 字典操作
    Task AddToDictionary(string key, int value);
    Task RemoveFromDictionary(string key);
    Task<int> GetDictionaryValue(string key);
    Task<int> GetDictionaryCount();

    // 列表操作
    Task AddToList(string item);
    Task RemoveListItemAt(int index);
    Task<string> GetListItem(int index);
    Task<int> GetListCount();

    // 队列操作
    Task AddToQueue(int item);
    Task<int> DequeueItem();
    Task<int> PeekQueueItem();
    Task<int> GetQueueCount();

    // 集合操作
    Task AddToSet(string item);
    Task RemoveFromSet(string item);
    Task<bool> ContainsSetItem(string item);
    Task<int> GetSetCount();
}
