namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 测试用持久化 Grain 的接口
/// </summary>
public interface ITestDurableGrainInterface : IGrainWithGuidKey
{
    Task<Guid> GetActivationId();
    Task SetValues(string name, int counter);
    Task<(string Name, int Counter)> GetValues();
}
