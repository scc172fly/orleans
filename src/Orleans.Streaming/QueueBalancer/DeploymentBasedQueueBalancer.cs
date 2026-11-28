using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orleans.Runtime;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streaming.Diagnostics;

namespace Orleans.Streams
{
    /// <summary>
    /// DeploymentBasedQueueBalancer 是一种利用部署信息
    /// 辅助均衡队列分布的流队列均衡器。
    /// DeploymentBasedQueueBalancer 使用部署配置确定预期的 Silo 数量，
    /// 并使用 Silo 状态查询服务确定哪些 Silo 可用。利用
    /// 这些信息，它尝试使用最佳适配资源均衡算法来均衡队列。
    /// </summary>
    public class DeploymentBasedQueueBalancer : QueueBalancerBase, IStreamQueueBalancer
    {
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IDeploymentConfiguration deploymentConfig;
        private readonly DeploymentBasedQueueBalancerOptions options;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<SiloAddress, bool> immatureSilos;
        private List<QueueId> allQueues = null!; // 在 Initialize 中初始化。
        private string _name;
        private bool isStarting;

        /// <summary>
        /// 初始化 <see cref="DeploymentBasedQueueBalancer"/> 类的新实例。
        /// </summary>
        /// <param name="siloStatusOracle">Silo 状态查询服务。</param>
        /// <param name="deploymentConfig">部署配置。</param>
        /// <param name="options">队列均衡器选项。</param>
        /// <param name="services">服务提供程序。</param>
        /// <param name="logger">日志记录器。</param>
        public DeploymentBasedQueueBalancer(
            ISiloStatusOracle siloStatusOracle,
            IDeploymentConfiguration deploymentConfig,
            DeploymentBasedQueueBalancerOptions options,
            IServiceProvider services,
            ILogger<DeploymentBasedQueueBalancer> logger)
            : base(services, logger)
        {
            this.siloStatusOracle = siloStatusOracle ?? throw new ArgumentNullException(nameof(siloStatusOracle));
            this.deploymentConfig = deploymentConfig ?? throw new ArgumentNullException(nameof(deploymentConfig));
            this.options = options;
            _timeProvider = services.GetKeyedService<TimeProvider>(StreamingTimeProviderNames.Streaming) ?? TimeProvider.System;
            _name = string.Empty;

            isStarting = true;

            // 将所有已活动的 Silo 记录为已成熟。
            // 即使它们尚未成熟，当本 Silo 成熟时（isStarting 变为 false 之后），它们也将成熟。
            immatureSilos = new ConcurrentDictionary<SiloAddress, bool>(
                from s in siloStatusOracle.GetApproximateSiloStatuses(true).Keys
                where !s.Equals(siloStatusOracle.SiloAddress)
                select new KeyValuePair<SiloAddress, bool>(s, false));
        }

        /// <summary>
        /// 创建基于部署的队列均衡器。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="deploymentConfiguration">部署配置。</param>
        /// <returns>队列均衡器。</returns>
        public static IStreamQueueBalancer Create(IServiceProvider services, string name, IDeploymentConfiguration deploymentConfiguration)
        {
            var options = services.GetRequiredService<IOptionsMonitor<DeploymentBasedQueueBalancerOptions>>().Get(name);
            var balancer = ActivatorUtilities.CreateInstance<DeploymentBasedQueueBalancer>(services, options, deploymentConfiguration);
            balancer._name = name;
            return balancer;
        }

        /// <inheritdoc />
        public override Task Initialize(IStreamQueueMapper queueMapper)
        {
            if (queueMapper == null)
            {
                throw new ArgumentNullException(nameof(queueMapper));
            }
            this.allQueues = queueMapper.GetAllQueues().ToList();
            NotifyAfterStart().Ignore();
            return base.Initialize(queueMapper);
        }

        private async Task NotifyAfterStart()
        {
            await Task.Delay(this.options.SiloMaturityPeriod, _timeProvider);
            isStarting = false;
            await NotifyListeners();
            StreamingEvents.EmitQueueBalancerMaturityCompleted(_name, SiloAddress, siloStatusOracle.SiloAddress, isLocalSilo: true, this);
        }

        private async Task<SiloAddress> RecordImmatureSilo(SiloAddress updatedSilo)
        {
            immatureSilos[updatedSilo] = true;      // 记录为尚未成熟。
            await Task.Delay(this.options.SiloMaturityPeriod, _timeProvider);
            immatureSilos[updatedSilo] = false;     // 记录为已成熟。
            return updatedSilo;
        }

        /// <inheritdoc />
        public override IEnumerable<QueueId> GetMyQueues()
        {
            BestFitBalancer<string, QueueId> balancer = GetBalancer();
            bool useIdealDistribution = this.options.IsFixed || isStarting;
            Dictionary<string, List<QueueId>> distribution = useIdealDistribution
                ? balancer.IdealDistribution
                : balancer.GetDistribution(GetActiveSilos(siloStatusOracle, immatureSilos));

            if (distribution.TryGetValue(siloStatusOracle.SiloName, out var myQueues))
            {
                if (!useIdealDistribution)
                {
                    HashSet<QueueId> queuesOfImmatureSilos = GetQueuesOfImmatureSilos(siloStatusOracle, immatureSilos, balancer.IdealDistribution);
                    // 滤除属于尚未成熟的 Silo 的队列。
                    myQueues.RemoveAll(queue => queuesOfImmatureSilos.Contains(queue));
                }
                return myQueues;
            }
            return Enumerable.Empty<QueueId>();
        }

        private static List<string> GetActiveSilos(ISiloStatusOracle siloStatusOracle, ConcurrentDictionary<SiloAddress, bool> immatureSilos)
        {
            var activeSiloNames = new List<string>();
            foreach (var kvp in siloStatusOracle.GetApproximateSiloStatuses(true))
            {
                bool immatureBit;
                if (!(immatureSilos.TryGetValue(kvp.Key, out immatureBit) && immatureBit)) // 如果现在不处于未成熟状态，或已不再处于该状态。
                {
                    string? siloName;
                    if (siloStatusOracle.TryGetSiloName(kvp.Key, out siloName))
                    {
                        activeSiloNames.Add(siloName);
                    }
                }
            }
            return activeSiloNames;
        }

        /// <summary>
        /// 检查部署配置是否因添加或移除 Silo 而发生变化。
        /// 若发生变化，则更新所有 Silo 名称列表并创建新的资源均衡器。
        /// 这种情况应很少发生。
        /// </summary>
        private BestFitBalancer<string, QueueId> GetBalancer()
        {
            var allSiloNames = deploymentConfig.GetAllSiloNames();
            // 使用新的实例名称列表重新构建均衡器。
            return new BestFitBalancer<string, QueueId>(allSiloNames, allQueues);
        }

        private static HashSet<QueueId> GetQueuesOfImmatureSilos(ISiloStatusOracle siloStatusOracle,
            ConcurrentDictionary<SiloAddress, bool> immatureSilos,
            Dictionary<string, List<QueueId>> idealDistribution)
        {
            HashSet<QueueId> queuesOfImmatureSilos = new HashSet<QueueId>();
            foreach (var silo in immatureSilos.Where(s => s.Value)) // 仅取未成熟集合中未成熟状态位已设置的项。
            {
                string? siloName;
                if (siloStatusOracle.TryGetSiloName(silo.Key, out siloName))
                {
                    if (idealDistribution.TryGetValue(siloName, out var queues))
                    {
                        queuesOfImmatureSilos.UnionWith(queues);
                    }
                }
            }
            return queuesOfImmatureSilos;
        }

        /// <inheritdoc />
        protected override void OnClusterMembershipChange(HashSet<SiloAddress> activeSilos)
        {
            SignalClusterChange(activeSilos).Ignore();
        }

        private async Task SignalClusterChange(HashSet<SiloAddress> activeSilos)
        {
            List<Task<SiloAddress>> tasks = new List<Task<SiloAddress>>();
            // 查看除自身之外当前所有活动 Silo。
            foreach (var silo in activeSilos)
            {
                if (!silo.Equals(siloStatusOracle.SiloAddress) && !immatureSilos.ContainsKey(silo))
                {
                    tasks.Add(RecordImmatureSilo(silo));
                }
            }
            if (!isStarting)
            {
                // 无条件发出通知，并在 GetMyQueues() 中处理变化。
                await NotifyListeners();
            }
            if (tasks.Count > 0)
            {
                var maturedSilos = await Task.WhenAll(tasks);
                await this.NotifyListeners(); // 无条件发出通知，并在 GetMyQueues() 中处理变化。
                foreach (var maturedSilo in maturedSilos)
                {
                    StreamingEvents.EmitQueueBalancerMaturityCompleted(_name, SiloAddress, maturedSilo, isLocalSilo: false, this);
                }
            }
        }
    }
}
