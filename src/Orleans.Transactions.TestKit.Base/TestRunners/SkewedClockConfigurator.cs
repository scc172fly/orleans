using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 配置测试 Silo，使其使用具有随机偏差的事务时钟。
    /// </summary>
    public class SkewedClockConfigurator : ISiloConfigurator
    {
        private static readonly TimeSpan MinSkew = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(5);

        /// <inheritdoc/>
        public void Configure(ISiloBuilder hostBuilder)
        {
            hostBuilder
                .ConfigureServices(services => services.AddSingleton<IClock>(sp => new SkewedClock(MinSkew, MaxSkew)));
        }
    }
}
