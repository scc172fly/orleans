using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;

namespace Orleans.Durable.Messaging.Tests.Support;

internal static class MessagingTestGrainTypes
{
    // 与源相关的操场和食谱付款 grains 同一种生产类型 grain 。
    public static void UseDocumentationPaymentGrain(this IServiceCollection services) =>
        services.PostConfigure<GrainTypeOptions>(options =>
            options.Classes.Remove(typeof(OrderProcessing.Grains.PaymentGrain)));
}
