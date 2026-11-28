using Xunit;
using Orleans.LeaseProviders;

namespace TestExtensions.Runners
{
    public class GoldenPathLeaseProviderTestRunner
    {
        private const string LeaseCategory = "TestLeaseCategory";

        private readonly ILeaseProvider leaseProvider;
        private readonly ITestOutputHelper output;

        protected GoldenPathLeaseProviderTestRunner(ILeaseProvider leaseProvider, ITestOutputHelper output)
        {
            this.leaseProvider = leaseProvider;
            this.output = output;
        }

        [Fact]
        public async Task ProviderCanAcquireLeases()
        {
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15)), new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15))
            };
            // 获取。
            AcquireLeaseResult[] results = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i];
                Assert.Equal(ResponseCode.OK, result.StatusCode);
                Assert.NotNull(result.AcquiredLease);
                Assert.Equal(leaseRequests[i].ResourceKey, result.AcquiredLease.ResourceKey);
            }
            ;
        }

        [Fact]
        public async Task ProviderCanReleaseLeases()
        {
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15)), new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15))
            };
            // 获取。
            var results = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            // 释放
            await this.leaseProvider.Release(LeaseCategory, results.Select(result => result.AcquiredLease!).ToArray()); // 此正常流程预期每次获取都成功。
        }

        [Fact]
        public async Task ProviderCanRenewLeases()
        {
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15)), new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15))
            };
            // 获取。
            var acquireResults = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            // 续订
            var renewResults = await this.leaseProvider.Renew(LeaseCategory, acquireResults.Select(result => result.AcquiredLease!).ToArray()); // 此正常流程预期每次获取都成功。
            for (int i = 0; i < renewResults.Count(); i++)
            {
                var result = renewResults[i];
                Assert.Equal(ResponseCode.OK, result.StatusCode);
                Assert.NotNull(result.AcquiredLease);
                Assert.Equal(leaseRequests[i].ResourceKey, result.AcquiredLease.ResourceKey);
            }
            ;
        }

        [Fact]
        public async Task Provider_TryAcquireLeaseWhichBelongToOtherEntity_Return_LeaseNotAvailable()
        {
            var resourceId = Guid.NewGuid().ToString();
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(resourceId, TimeSpan.FromSeconds(15)), new LeaseRequest(resourceId, TimeSpan.FromSeconds(15))
            };
            // 两个实体尝试获取同一资源上的租约
            var results = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            // 一次尝试成功，另一次失败
            Assert.Contains(results, result => result.StatusCode == ResponseCode.OK);
            Assert.Contains(results, result => result.StatusCode == ResponseCode.LeaseNotAvailable);
        }

        [Fact]
        public async Task Provider_TryRenewLeaseWithWrongToken_Return_InvalidToken()
        {
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15)), new LeaseRequest(Guid.NewGuid().ToString(), TimeSpan.FromSeconds(15))
            };
            // 获取。
            var results = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            var acquiredLeaseWithWrongToken = results.Select(result => new AcquiredLease(result.AcquiredLease!.ResourceKey, result.AcquiredLease.Duration, Guid.NewGuid().ToString(), result.AcquiredLease.StartTimeUtc)); // 每个不同的资源均获取一个租约。
            // 使用错误的令牌续订
            var renewResults = await this.leaseProvider.Renew(LeaseCategory, acquiredLeaseWithWrongToken.ToArray());
            for (int i = 0; i < renewResults.Count(); i++)
            {
                LeaseRequest request = leaseRequests[i];
                AcquireLeaseResult result = renewResults[i];
                Assert.Equal(ResponseCode.InvalidToken, result.StatusCode);
                Assert.Equal(request.ResourceKey, result.AcquiredLease!.ResourceKey); // 无效令牌响应会保留已获取的租约。
            }
        }

        [Fact]
        public async Task Provider_LifeCycle_Acqurie_Renew_Release_ShouldBeAbleToAcquireLeaseOnTheSameResourceAfterRelease()
        {
            var resourceId = Guid.NewGuid().ToString();
            var leaseRequests = new List<LeaseRequest>() {
                new LeaseRequest(resourceId, TimeSpan.FromSeconds(15))
            };

            // 首次获取
            var acquireResults1 = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            // 续订
            var renewResults = await this.leaseProvider.Renew(LeaseCategory, acquireResults1.Select(result => result.AcquiredLease!).ToArray()); // 此正常流程预期获取成功。
            for (int i = 0; i < renewResults.Count(); i++)
            {
                var result = renewResults[i];
                Assert.Equal(ResponseCode.OK, result.StatusCode);
                Assert.NotNull(result.AcquiredLease);
                Assert.Equal(leaseRequests[i].ResourceKey, result.AcquiredLease.ResourceKey);
            }
            // 释放
            await this.leaseProvider.Release(LeaseCategory, renewResults.Select(result => result.AcquiredLease!).ToArray()); // 成功续订后保留相应租约。

            // 第二次获取，在原租约释放之后重新获取同一资源上的租约
            var acquireResults2 = await this.leaseProvider.Acquire(LeaseCategory, leaseRequests.ToArray());
            for (int i = 0; i < acquireResults2.Count(); i++)
            {
                var result = acquireResults2[i];
                Assert.Equal(ResponseCode.OK, result.StatusCode);
                Assert.NotNull(result.AcquiredLease);
                Assert.Equal(leaseRequests[i].ResourceKey, result.AcquiredLease.ResourceKey);
                // 两个租约中的令牌应当不同
                Assert.NotEqual(acquireResults1[i].AcquiredLease!.Token, result.AcquiredLease!.Token); // 两次成功获取均返回租约。
            }
        }

    }
}
