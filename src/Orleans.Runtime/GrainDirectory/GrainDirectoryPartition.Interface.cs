using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Diagnostics;

namespace Orleans.Runtime.GrainDirectory;

internal sealed partial class GrainDirectoryPartition
{
    async ValueTask<DirectoryResult<GrainAddress>> IGrainDirectoryPartition.RegisterAsync(
        MembershipVersion version,
        GrainAddress address,
        GrainAddress? currentRegistration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(address);
        LogRegisterAsync(version, address, currentRegistration);

        var currentView = await WaitForOwnershipViewAsync(address.GrainId, version, cancellationToken);
        if (!IsOwner(currentView, address.GrainId))
        {
            return DirectoryResult.RefreshRequired<GrainAddress>(currentView.Version);
        }

        DebugAssertOwnership(currentView, address.GrainId);

        if (_deadSiloLeaseDuration > TimeSpan.Zero)
        {
            var utcNow = _timeProvider.GetUtcNow();
            var rangeHash = address.GrainId.GetUniformHashCode();
            var isExistingRegistration = currentRegistration is not null && address.Matches(currentRegistration);

            // 范围租约保留。
            for (var i = _rangeLeaseHolds.Count - 1; !isExistingRegistration && i >= 0; i--)
            {
                var (lockedRange, expiration) = _rangeLeaseHolds[i];

                if (utcNow >= expiration)
                {
                    // 借此机会清理这个已经过期的范围租约保留。
                    _rangeLeaseHolds.RemoveAt(i);
                    continue;
                }

                // 如果仍然有效，是否会阻塞当前请求？
                if (lockedRange.Contains(rangeHash))
                {
                    var retryAfter = expiration - utcNow;
                    GrainDirectoryEvents.EmitRegistrationBlockedByRangeLease(_id, address.GrainId, lockedRange, expiration, retryAfter);
                    return DirectoryResult.RetryAfter<GrainAddress>(retryAfter);
                }
            }

            // Grain 租约保留。
            if (_directory.TryGetValue(address.GrainId, out var existingActivation))
            {
                if (_siloLeaseHolds.TryGetValue(existingActivation.SiloAddress!, out var expiration) && utcNow < expiration)
                {
                    // 此 Grain 属于当前分区，且激活实例位于一个仍存在有效租约保留的 Silo 上。
                    // 需要检查请求是否包含先前的激活实例 ID。如果包含，则是有效的更新/覆盖；
                    // 否则，这是一个新的激活实例试图在租约有效期间“抢占”该 ID，因此拒绝请求！
                    if (currentRegistration is null || !existingActivation.Matches(currentRegistration))
                    {
                        var retryAfter = expiration - utcNow;
                        GrainDirectoryEvents.EmitRegistrationBlockedBySiloLease(_id, address.GrainId, existingActivation.SiloAddress!, expiration, retryAfter);
                        return DirectoryResult.RetryAfter<GrainAddress>(retryAfter);
                    }
                }
            }
        }

        return DirectoryResult.FromResult(RegisterCore(address, currentRegistration, currentView.Version), version);
    }

    async ValueTask<DirectoryResult<GrainAddress?>> IGrainDirectoryPartition.LookupAsync(
        MembershipVersion version,
        GrainId grainId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogLookupAsync(version, grainId);

        var currentView = await WaitForOwnershipViewAsync(grainId, version, cancellationToken);
        if (!IsOwner(currentView, grainId))
        {
            return DirectoryResult.RefreshRequired<GrainAddress?>(currentView.Version);
        }

        return DirectoryResult.FromResult(LookupCore(grainId), version);
    }

    async ValueTask<DirectoryResult<bool>> IGrainDirectoryPartition.DeregisterAsync(
        MembershipVersion version,
        GrainAddress address,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(address);
        LogDeregisterAsync(version, address);

        var currentView = await WaitForOwnershipViewAsync(address.GrainId, version, cancellationToken);
        if (!IsOwner(currentView, address.GrainId))
        {
            return DirectoryResult.RefreshRequired<bool>(currentView.Version);
        }

        DebugAssertOwnership(currentView, address.GrainId);
        return DirectoryResult.FromResult(DeregisterCore(address), version);
    }

    private bool DeregisterCore(GrainAddress address)
    {
        if (!_directory.TryGetValue(address.GrainId, out var existing))
        {
            return false;
        }

        if (_deadSiloLeaseDuration > TimeSpan.Zero
            && existing.SiloAddress is { } siloAddress
            && _siloLeaseHolds.TryGetValue(siloAddress, out var expiration)
            && _timeProvider.GetUtcNow() < expiration)
        {
            return false;
        }

        if (existing.Matches(address) || IsSiloDead(existing))
        {
            return _directory.Remove(address.GrainId);
        }

        return false;
    }

    internal GrainAddress? LookupCore(GrainId grainId)
    {
        if (_directory.TryGetValue(grainId, out var existing) && !IsSiloDead(existing))
        {
            return existing;
        }

        return null;
    }

    private async ValueTask<DirectoryMembershipSnapshot> WaitForOwnershipViewAsync(
        GrainId grainId,
        MembershipVersion version,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ShutdownToken);
        while (true)
        {
            // 携带过时成员资格版本到达的请求，仍须等待当前视图中任何正在进行的所有权
            // 转移完成，才能确定此分区能否处理这些请求。
            var currentView = CurrentView;
            var waitVersion = currentView.Version > version ? currentView.Version : version;
            await WaitForRange(grainId, waitVersion, linkedCts.Token);
            linkedCts.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(currentView, CurrentView))
            {
                return currentView;
            }
        }
    }

    private GrainAddress RegisterCore(GrainAddress newAddress, GrainAddress? existingAddress, MembershipVersion currentVersion)
    {
        ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(_directory, newAddress.GrainId, out _);

        if (existing is null || existing.Matches(existingAddress) || IsSiloDead(existing))
        {
            if (newAddress.MembershipVersion != currentVersion)
            {
                // 将成员资格版本设置为注册时所在视图的版本号。
                newAddress = new()
                {
                    GrainId = newAddress.GrainId,
                    SiloAddress = newAddress.SiloAddress,
                    ActivationId = newAddress.ActivationId,
                    MembershipVersion = currentVersion
                };
            }

            existing = newAddress;
        }

        return existing;
    }

    private bool IsSiloDead(GrainAddress existing)
        => existing.SiloAddress is null || _owner.ClusterMembershipSnapshot.GetSiloStatus(existing.SiloAddress, existing.MembershipVersion) == SiloStatus.Dead;

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "RegisterAsync('{Version}', '{Address}', '{ExistingAddress}')"
    )]
    private partial void LogRegisterAsync(MembershipVersion version, GrainAddress address, GrainAddress? existingAddress);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "LookupAsync('{Version}', '{GrainId}')"
    )]
    private partial void LogLookupAsync(MembershipVersion version, GrainId grainId);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "DeregisterAsync('{Version}', '{Address}')"
    )]
    private partial void LogDeregisterAsync(MembershipVersion version, GrainAddress address);
}
