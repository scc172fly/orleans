using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.TestingHost;

internal sealed class TestClusterFatalErrorHandler(
    ILogger<TestClusterFatalErrorHandler> logger,
    TestClusterHostTerminator hostTerminator) : IFatalErrorHandler
{
    public bool IsUnexpected(Exception exception) => exception is not ThreadAbortException;

    public void OnFatalException(object? sender = null, string? context = null, Exception? exception = null)
    {
        logger.LogError(
            exception,
            "Fatal error from {Sender}. Context: {Context}. The affected test silo will stop without terminating the test host.",
            sender,
            context);
        hostTerminator.Stop();
    }

    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<TestClusterHostTerminator>();
        services.Replace(ServiceDescriptor.Singleton<IFatalErrorHandler, TestClusterFatalErrorHandler>());
    }

    public static void Attach(IHost host) =>
        host.Services.GetRequiredService<TestClusterHostTerminator>().Attach(host);
}

internal sealed class TestClusterHostTerminator(ILogger<TestClusterHostTerminator> logger)
{
    private IHost? _host;
    private int _stopping;

    public void Attach(IHost host) => _host = host;

    public void Stop()
    {
        var host = _host ?? throw new InvalidOperationException("The test silo host has not been attached.");
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            // 致命错误模拟进程丢失：取消排空操作，同时完成进程内清理。
            var cancellationToken = new CancellationToken(canceled: true);
            try
            {
                await host.StopAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                if (exception.IsCancellation())
                {
                    logger.LogDebug("Canceled graceful draining after a fatal test silo error.");
                }
                else
                {
                    logger.LogError(exception, "Failed to stop a test silo after a fatal error.");
                }
            }
        });
    }
}
