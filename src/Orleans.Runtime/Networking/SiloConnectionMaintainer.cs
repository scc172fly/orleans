using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.Messaging
{
    internal partial class SiloConnectionMaintainer : ILifecycleParticipant<ISiloLifecycle>, ISiloStatusListener, ILifecycleObserver
    {
        private readonly ConnectionManager connectionManager;
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IRuntimeClient runtimeClient;
        private readonly ILogger<SiloConnectionMaintainer> log;

        public SiloConnectionMaintainer(
            ConnectionManager connectionManager,
            ISiloStatusOracle siloStatusOracle,
            IRuntimeClient runtimeClient,
            ILogger<SiloConnectionMaintainer> log)
        {
            this.connectionManager = connectionManager;
            this.siloStatusOracle = siloStatusOracle;
            this.runtimeClient = runtimeClient;
            this.log = log;
        }

        public Task OnStart(CancellationToken ct)
        {
            this.siloStatusOracle.SubscribeToSiloStatusEvents(this);
            return Task.CompletedTask;
        }

        public Task OnStop(CancellationToken ct)
        {
            this.siloStatusOracle.UnSubscribeFromSiloStatusEvents(this);
            return Task.CompletedTask;
        }

        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(nameof(SiloConnectionMaintainer), ServiceLifecycleStage.RuntimeInitialize, this);
        }

        public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
        {
            if (status == SiloStatus.Dead && !updatedSilo.Equals(siloStatusOracle.SiloAddress))
            {
                this.runtimeClient.BreakOutstandingMessagesToSilo(updatedSilo);
                _ = Task.Run(() => this.CloseConnectionAsync(updatedSilo));
            }
        }

        private async Task CloseConnectionAsync(SiloAddress silo)
        {
            try
            {
                // 允许短的宽限期完成发送待发信件(例如八卦回复)
                await Task.Delay(TimeSpan.FromSeconds(10));

                await this.connectionManager.CloseAsync(silo);
            }
            catch (Exception exception)
            {
                LogExceptionWhileClosingConnections(this.log, silo, exception);
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = 1001,
            Message = "Exception while closing connections to defunct silo {SiloAddress}"
        )]
        private static partial void LogExceptionWhileClosingConnections(ILogger logger, SiloAddress siloAddress, Exception exception);
    }
}
