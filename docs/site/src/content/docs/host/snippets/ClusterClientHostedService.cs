// <cluster_client_hosted_service>
using Microsoft.Extensions.Hosting;

namespace Client;

public sealed class ClusterClientHostedService : IHostedService
{
    private readonly IClusterClient _client;

    public ClusterClientHostedService(IClusterClient client)
    {
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 使用 _client 调用 Grain……

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
// </cluster_client_hosted_service>
