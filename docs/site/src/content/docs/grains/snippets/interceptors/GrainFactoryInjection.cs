namespace Orleans.Docs.Snippets.Interceptors;

// <grain_factory_injection>
public sealed class AuditCallFilter(IGrainFactory grainFactory)
    : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // 排除审计界面, 这样它不会重新输入此过滤器 。
        if (context.InterfaceMethod.DeclaringType != typeof(ICallAuditGrain))
        {
            var auditGrain = grainFactory.GetGrain<ICallAuditGrain>(
                context.TargetId.ToString());
            await auditGrain.RecordCallAttempt(
                context.InterfaceName,
                context.MethodName);
        }

        await context.Invoke();
    }
}
// </grain_factory_injection>
