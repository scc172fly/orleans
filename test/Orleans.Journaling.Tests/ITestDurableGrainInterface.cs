namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 测试耐久性 grain的接口
/// </summary>
public interface ITestDurableGrainInterface : IGrainWithGuidKey
{
    Task<Guid> GetActivationId();
    Task SetValues(string name, int counter);
    Task<(string Name, int Counter)> GetValues();
}
