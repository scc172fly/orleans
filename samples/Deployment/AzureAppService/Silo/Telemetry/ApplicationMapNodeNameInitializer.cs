// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Silo.Telemetry;

internal class ApplicationMapNodeNameInitializer : ITelemetryInitializer
{
    private readonly string _name;

    internal ApplicationMapNodeNameInitializer(string name) => _name = name;

    public void Initialize(ITelemetry telemetry) =>
        (telemetry.Context.Cloud.RoleName, telemetry.Context.Cloud.RoleInstance) = (
            _name,
            Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName);
}
