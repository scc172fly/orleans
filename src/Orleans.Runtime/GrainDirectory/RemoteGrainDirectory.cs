using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.GrainDirectory;

namespace Orleans.Runtime.GrainDirectory
{
    internal sealed partial class RemoteGrainDirectory : SystemTarget, IRemoteGrainDirectory
    {
        private readonly LocalGrainDirectory router;
        private readonly LocalGrainDirectoryPartition partition;
        private readonly ILogger logger;

        internal RemoteGrainDirectory(LocalGrainDirectory localGrainDirectory, GrainType grainType, SystemTargetShared shared, bool registerAsSystemTarget = true)
            : base(grainType, shared)
        {
            router = localGrainDirectory;
            partition = localGrainDirectory.DirectoryPartition;
            logger = shared.LoggerFactory.CreateLogger($"{typeof(RemoteGrainDirectory).FullName}.CacheValidator");
            if (registerAsSystemTarget)
            {
                shared.ActivationDirectory.RecordNewTarget(this);
            }
        }

        public Task<AddressAndTag> RegisterAsync(GrainAddress address, GrainAddress? previousAddress, int hopCount)
        {
            router.DirectoryInstruments.RegistrationsSingleActRemoteReceived.Add(1);

            return router.RegisterAsync(address, previousAddress, hopCount);
        }

        public Task RegisterMany(List<GrainAddress> addresses, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (addresses == null || addresses.Count == 0)
            {
                throw new ArgumentException("Addresses cannot be an empty list or null");
            }

            // 验证这一请求是否正确收到
            //logger.Assert(ErrorCode.Runtime_Error_100140, silo.Matches(router.MyAddress),“目的地地址!”=我的地址”。

            LogRegisterMany(addresses.Count);

            return Task.WhenAll(addresses.Select(addr => router.RegisterAsync(addr, previousAddress: null, 1)))
                .WaitAsync(cancellationToken);
        }

        public Task UnregisterAsync(GrainAddress address, UnregistrationCause cause, int hopCount)
        {
            return router.UnregisterAsync(address, cause, hopCount);
        }

        public Task UnregisterManyAsync(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount)
        {
            return router.UnregisterManyAsync(addresses, cause, hopCount);
        }

        public Task DeleteGrainAsync(GrainId grainId, int hopCount)
        {
            return router.DeleteGrainAsync(grainId, hopCount);
        }

        public Task<AddressAndTag> LookupAsync(GrainId grainId, int hopCount)
        {
            return router.LookupAsync(grainId, hopCount);
        }

        public Task<List<AddressAndTag>> LookUpMany(
            List<(GrainId GrainId, int Version)> grainAndETagList,
            CancellationToken cancellationToken = default)
        {
            router.DirectoryInstruments.ValidationsCacheReceived.Add(1);
            LogLookUpMany(grainAndETagList.Count);

            var result = new List<AddressAndTag>();

            foreach (var tuple in grainAndETagList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int curGen = partition.GetGrainETag(tuple.GrainId);
                if (curGen == tuple.Version || curGen == GrainInfo.NO_ETAG)
                {
                    // grain 条目要么没有存在于本地分区(curGen = -1)中,要么尚未更新

                    result.Add(new(GrainAddress.GetAddress(null, tuple.GrainId, default), curGen));
                }
                else
                {
                    // grain 条目已经更新 -- -- 获取并返回当前版本
                    var lookupResult = partition.LookUpActivation(tuple.GrainId);
                    // 验证该条目是否仍然在目录中 (i.e., 它没有同时删除)
                    if (lookupResult.Address != null)
                    {
                        result.Add(lookupResult);
                    }
                    else
                    {
                        result.Add(new(GrainAddress.GetAddress(null, tuple.GrainId, default), GrainInfo.NO_ETAG));
                    }
                }
            }

            return Task.FromResult(result);
        }

        public Task AcceptSplitPartition(
            List<GrainAddress> singleActivations,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            router.HandoffManager.AcceptExistingRegistrations(singleActivations);
            return Task.CompletedTask;
        }

        public Task<AddressAndTag> RegisterAsync(GrainAddress address, int hopCount = 0) => router.RegisterAsync(address, hopCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "RegisterMany Count={Count}"
        )]
        private partial void LogRegisterMany(int count);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "LookUpMany for {Count} entries"
        )]
        private partial void LogLookUpMany(int count);
    }
}
