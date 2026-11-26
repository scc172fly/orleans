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
    /// DeploymentBasedQueueBalancer 是一个使用部署信息来
    /// 帮助平衡队列分布 。
    /// DeploymentBasedQueueBalancer 使用部署配置来确定有多少 silos
    /// 将预期并使用 silo 状态符,以确定其中哪个 silos 可用。
    /// 这些信息, 它试图使用最合适的资源平衡算法来平衡队列 。
    /// </summary>
    public class DeploymentBasedQueueBalancer : QueueBalancerBase, IStreamQueueBalancer
    {
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IDeploymentConfiguration deploymentConfig;
        private readonly DeploymentBasedQueueBalancerOptions options;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<SiloAddress, bool> immatureSilos;
        private List<QueueId> allQueues = null!; // 初始化时开始。
        private string _name;
        private bool isStarting;

        /// <summary>
        /// 初始化 <see cref="DeploymentBasedQueueBalancer"/> 类的新实例。
        /// </summary>
        /// <param name="siloStatusOracle">下 silo 甲骨文状态</param>
        /// <param name="deploymentConfig">部署配置。</param>
        /// <param name="options">队列平衡器选项。</param>
        /// <param name="services">服务供应商。</param>
        /// <param name="logger">日志器。</param>
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

            // 全部记录已激活 silos , 记录为已经到期 。 
            // 即使还没有成熟,在我成熟的时候(在长大之后),它们也会成熟。isStarting).
            immatureSilos = new ConcurrentDictionary<SiloAddress, bool>(
                from s in siloStatusOracle.GetApproximateSiloStatuses(true).Keys
                where !s.Equals(siloStatusOracle.SiloAddress)
                select new KeyValuePair<SiloAddress, bool>(s, false));
        }

        /// <summary>
        /// 创建基于部署的队列平衡器。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="deploymentConfiguration">部署配置。</param>
        /// <returns>队列平衡器。</returns>
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
            immatureSilos[updatedSilo] = true;      // 记录为不成熟
            await Task.Delay(this.options.SiloMaturityPeriod, _timeProvider);
            immatureSilos[updatedSilo] = false;     // 记录已到期
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
                    // 属于不成熟的过滤队列 silos
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
                if (!(immatureSilos.TryGetValue(kvp.Key, out immatureBit) && immatureBit)) // 如果现在或将来不成熟
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
        /// 检查以查看部署配置是否已经通过添加或删除 silos而改变。
        /// 如果有,它更新所有 silo 名称的清单并创建一个新的资源平衡器。
        /// 这种情况应该很少发生。
        /// </summary>
        private BestFitBalancer<string, QueueId> GetBalancer()
        {
            var allSiloNames = deploymentConfig.GetAllSiloNames();
            // 使用新实例名列表的重建平衡器
            return new BestFitBalancer<string, QueueId>(allSiloNames, allQueues);
        }

        private static HashSet<QueueId> GetQueuesOfImmatureSilos(ISiloStatusOracle siloStatusOracle,
            ConcurrentDictionary<SiloAddress, bool> immatureSilos,
            Dictionary<string, List<QueueId>> idealDistribution)
        {
            HashSet<QueueId> queuesOfImmatureSilos = new HashSet<QueueId>();
            foreach (var silo in immatureSilos.Where(s => s.Value)) // 只选择那些不成熟的设置, 且其不成熟状态位数设定的不成熟设置
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
            // 查看当前所有活动状态 silos 不包括我自己
            foreach (var silo in activeSilos)
            {
                if (!silo.Equals(siloStatusOracle.SiloAddress) && !immatureSilos.ContainsKey(silo))
                {
                    tasks.Add(RecordImmatureSilo(silo));
                }
            }
            if (!isStarting)
            {
                // 通知、不编码通知和处理 GetMyQueues()的变更
                await NotifyListeners();
            }
            if (tasks.Count > 0)
            {
                var maturedSilos = await Task.WhenAll(tasks);
                await this.NotifyListeners(); // 以 GetMyQueues() 处理更改
                foreach (var maturedSilo in maturedSilos)
                {
                    StreamingEvents.EmitQueueBalancerMaturityCompleted(_name, SiloAddress, maturedSilo, isLocalSilo: false, this);
                }
            }
        }
    }
}
