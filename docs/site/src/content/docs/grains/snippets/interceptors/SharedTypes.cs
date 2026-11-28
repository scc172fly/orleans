using Orleans;

namespace Orleans.Docs.Snippets.Interceptors;

// 筛选器示例共用的接口。
public interface IMyGrain : IGrainWithIntegerKey
{
    Task<int> MyInterceptedMethod();
}

public interface IMyFilteredGrain : IGrainWithIntegerKey
{
    Task<int> GetFavoriteNumber();
}

public interface IAccessControlledGrain : IGrainWithIntegerKey
{
    Task<int> GetFavoriteNumber();
}

public interface ICallAuditGrain : IGrainWithStringKey
{
    Task RecordCallAttempt(string interfaceName, string methodName);
}
