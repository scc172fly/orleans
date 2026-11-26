using Orleans.Hosting;
using Orleans.Runtime;

namespace Orleans.Docs.Snippets.Interceptors;

public static class IncomingFilterConfiguration
{
    public static void ConfigureDelegateFilter(ISiloBuilder siloHostBuilder)
    {
        // <silo_delegate_filter>
        siloHostBuilder.AddIncomingGrainCallFilter(async context =>
        {
            // 如果调用的方法为`MyInterceptedMethod,则设定一个数值
            // 上 RequestContext ,然后其他过滤器或 grain能够读取。
            if (string.Equals(
                context.InterfaceMethod.Name,
                nameof(IMyGrain.MyInterceptedMethod),
                StringComparison.Ordinal))
            {
                RequestContext.Set(
                    "intercepted value", "this value was added by the filter");
            }

            await context.Invoke();

            // 如果 grain 方法返回了一整英特,则将结果设定为该值的两倍。
            if (context.Result is int resultValue)
            {
                context.Result = resultValue * 2;
            }
        });
        // </silo_delegate_filter>
    }

    public static void RegisterLoggingFilter(ISiloBuilder siloHostBuilder)
    {
        // <register_incoming_filter>
        siloHostBuilder.AddIncomingGrainCallFilter<LoggingCallFilter>();
        // </register_incoming_filter>
    }

    public static void RegisterGrainFactoryFilter(ISiloBuilder siloHostBuilder)
    {
        // <register_grain_factory_filter>
        siloHostBuilder.AddIncomingGrainCallFilter<AuditCallFilter>();
        // </register_grain_factory_filter>
    }
}

public static class OutgoingFilterConfiguration
{
    public static void RegisterLoggingFilter(ISiloBuilder builder)
    {
        // <register_outgoing_filter>
        builder.AddOutgoingGrainCallFilter<OutgoingLoggingCallFilter>();
        // </register_outgoing_filter>
    }
}
