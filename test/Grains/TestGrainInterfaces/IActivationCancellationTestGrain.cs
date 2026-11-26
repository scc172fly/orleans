namespace UnitTests.GrainInterfaces;

/// <summary>
/// 测试激活取消情景的接口。
/// 这些 grains 用于核实在启动 grain 期间对取消的适当处理。
/// </summary>
public interface IActivationCancellationTestGrain : IGrainWithGuidKey
{
    /// <summary>
    /// 一种简单的方法,用于测试 grain 是否被激活和起作用。
    /// </summary>
    Task<string> GetActivationId();

    /// <summary>
    /// 检查激活是否成功 。
    /// </summary>
    Task<bool> IsActivated();
}

/// <summary>
/// Grain 投 OperationCanceledException 期间 OnActivateAsync 当取消标记被触发时 。
/// 此模拟代码, 以正确观察取消符号 。
/// </summary>
public interface IActivationCancellation_ThrowsOperationCancelledGrain : IActivationCancellationTestGrain;

/// <summary>
/// Grain 投 ObjectDisposedException 期间 OnActivateAsync 试图获取处置服务时。
/// 此模拟代码不观察取消的代号, 却试图访问已处理的服务 。
/// </summary>
public interface IActivationCancellation_ThrowsObjectDisposedGrain : IActivationCancellationTestGrain;

/// <summary>
/// Grain 中丢弃通用例外 OnActivateAsync (与取消无关)。
/// 用于核实是否仍然妥善处理非取消性例外。
/// </summary>
public interface IActivationCancellation_ThrowsGenericExceptionGrain : IActivationCancellationTestGrain;

/// <summary>
/// 顺利启动的Grain , 没有问题 。
/// 这是核查正常启动的基线,仍在运作。
/// </summary>
public interface IActivationCancellation_SuccessfulActivationGrain : IActivationCancellationTestGrain;

/// <summary>
/// Grain 投 TaskCanceledException 期间 OnActivateAsync.
/// TaskCanceledException 继承的 OperationCanceledException 并且应该以同样的方式处理
/// </summary>
public interface IActivationCancellation_ThrowsTaskCancelledGrain : IActivationCancellationTestGrain;

/// <summary>
/// Grain 投 ObjectDisposedException (不是由于取消)
/// 出于其他原因投掷的 ObjectDisposedException 试验不被视为取消。
/// </summary>
public interface IActivationCancellation_ThrowsObjectDisposedUnconditionallyGrain : IActivationCancellationTestGrain;

/// <summary>
/// Grain 投 OperationCanceledException (不是由于取消)
/// 出于其他原因投掷的 OperationCanceledException 试验不被视为取消。
/// </summary>
public interface IActivationCancellation_ThrowsOperationCancelledUnconditionallyGrain : IActivationCancellationTestGrain;
