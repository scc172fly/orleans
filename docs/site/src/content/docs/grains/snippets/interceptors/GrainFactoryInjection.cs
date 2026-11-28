namespace Orleans.Docs.Snippets.Interceptors;

// <grain_factory_injection>
public sealed class AuditCallFilter(IGrainFactory grainFactory)
    : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // 排除审计接口，避免对该接口的调用递归重入当前筛选器。
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
