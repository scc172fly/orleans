// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollectionExtensions
{
    internal static void AddApplicationInsights(
        this IServiceCollection services, string applicationName)
    {
        services.AddApplicationInsightsTelemetry();
        services.AddSingleton<ITelemetryInitializer>(
            _ => new ApplicationMapNodeNameInitializer(applicationName));
    }
}
