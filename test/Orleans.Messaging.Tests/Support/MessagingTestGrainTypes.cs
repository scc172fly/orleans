using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;

namespace Orleans.Durable.Messaging.Tests.Support;

internal static class MessagingTestGrainTypes
{
    // 以源文件链接方式引用的 Playground 和实践示例支付 Grain 共用同一个生产 Grain 类型。
    public static void UseDocumentationPaymentGrain(this IServiceCollection services) =>
        services.PostConfigure<GrainTypeOptions>(options =>
            options.Classes.Remove(typeof(OrderProcessing.Grains.PaymentGrain)));
}
