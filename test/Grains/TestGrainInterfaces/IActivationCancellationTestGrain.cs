namespace UnitTests.GrainInterfaces;

/// <summary>
/// 用于测试激活取消场景的接口。
/// 这些 Grain 用于验证 Grain 激活期间能否正确处理取消。
/// </summary>
public interface IActivationCancellationTestGrain : IGrainWithGuidKey
{
    /// <summary>
    /// 用于验证 Grain 已激活并正常运行的简单方法。
    /// </summary>
    Task<string> GetActivationId();

    /// <summary>
    /// 检查激活是否成功。
    /// </summary>
    Task<bool> IsActivated();
}

/// <summary>
/// 在 OnActivateAsync 中，取消令牌触发时抛出 OperationCanceledException 的 Grain。
/// 模拟正确响应取消令牌的代码。
/// </summary>
public interface IActivationCancellation_ThrowsOperationCancelledGrain : IActivationCancellationTestGrain;

/// <summary>
/// 在 OnActivateAsync 中尝试访问已释放的服务时抛出 ObjectDisposedException 的 Grain。
/// 模拟不响应取消令牌却尝试访问已释放服务的代码。
/// </summary>
public interface IActivationCancellation_ThrowsObjectDisposedGrain : IActivationCancellationTestGrain;

/// <summary>
/// 在 OnActivateAsync 中抛出一般异常（与取消无关）的 Grain。
/// 用于验证与取消无关的异常仍能得到正确处理。
/// </summary>
public interface IActivationCancellation_ThrowsGenericExceptionGrain : IActivationCancellationTestGrain;

/// <summary>
/// 顺利完成激活的 Grain。
/// 作为基准，验证正常激活仍能正常运行。
/// </summary>
public interface IActivationCancellation_SuccessfulActivationGrain : IActivationCancellationTestGrain;

/// <summary>
/// 在 OnActivateAsync 中抛出 TaskCanceledException 的 Grain。
/// TaskCanceledException 继承自 OperationCanceledException，应采用相同的处理方式。
/// </summary>
public interface IActivationCancellation_ThrowsTaskCancelledGrain : IActivationCancellationTestGrain;

/// <summary>
/// 无条件抛出 ObjectDisposedException（并非由于取消）的 Grain。
/// 用于测试因其他原因抛出的 ObjectDisposedException 不会被视为取消。
/// </summary>
public interface IActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain : IActivationCancellationTestGrain;

/// <summary>
/// 无条件抛出 OperationCanceledException（并非由于取消）的 Grain。
/// 用于测试因其他原因抛出的 OperationCanceledException 不会被视为取消。
/// </summary>
public interface IActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain : IActivationCancellationTestGrain;
