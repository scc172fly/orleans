using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orleans.Runtime.MembershipService.SiloMetadata;

internal sealed class SiloMetadataSystemTarget : SystemTarget, ISiloMetadataSystemTarget, ILifecycleParticipant<ISiloLifecycle>
{
    private readonly SiloMetadata _siloMetadata;

    public SiloMetadataSystemTarget(
        IOptions<SiloMetadata> siloMetadata,
        SystemTargetShared shared) : base(Constants.SiloMetadataType, shared)
    {
        _siloMetadata = siloMetadata.Value;
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    public Task<SiloMetadata> GetSiloMetadata(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_siloMetadata);
    }
    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        // 我们不参与任何生命周期阶段: 启动这个实例是必需的。
    }
}
