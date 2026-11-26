using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Internal;
using Orleans.Runtime.Internal;

namespace Orleans.Runtime
{
    /// <summary>
    /// 维持最近激活的启动列表 。
    /// </summary>
    internal sealed partial class ActivationWorkingSet : IActivationWorkingSet, ILifecycleParticipant<ISiloLifecycle>
    {
        private class MemberState(GrainTypeMetrics metrics)
        {
            public GrainTypeMetrics Metrics { get; } = metrics;
            public bool IsIdle { get; set; }
        }

        private readonly ConcurrentDictionary<IActivationWorkingSetMember, MemberState> _members = new();
        private readonly ILogger _logger;
        private readonly IAsyncTimer _scanPeriodTimer;
        private readonly List<IActivationWorkingSetObserver> _observers;
        private readonly CatalogInstruments _catalogInstruments;

        private int _activeCount;
        private Task? _runTask;

        public ActivationWorkingSet(
            IAsyncTimerFactory asyncTimerFactory,
            ILogger<ActivationWorkingSet> logger,
            IEnumerable<IActivationWorkingSetObserver> observers,
            CatalogInstruments catalogInstruments,
            [FromKeyedServices(TimeProviderNames.SystemTimers)] TimeProvider timeProvider)
        {
            _logger = logger;
            _scanPeriodTimer = asyncTimerFactory.Create(TimeSpan.FromMilliseconds(5_000), nameof(ActivationWorkingSet) + "." + nameof(MonitorWorkingSet), timeProvider);
            _observers = observers.ToList();
            _catalogInstruments = catalogInstruments;
            catalogInstruments.RegisterActivationWorkingSetObserve();
        }

        public int Count => _activeCount;

        internal IEnumerable<IActivationWorkingSetMember> Members => EnumerateActiveMembers();

        private IEnumerable<IActivationWorkingSetMember> EnumerateActiveMembers()
        {
            foreach (var pair in _members)
            {
                if (!pair.Value.IsIdle)
                {
                    yield return pair.Key;
                }
            }
        }

        public void OnActivated(IActivationWorkingSetMember member)
        {
            Debug.Assert(member is not ActivationData activation || activation.IsValid);
            var state = CreateMemberState(member);
            if (_members.TryAdd(member, state))
            {
                Interlocked.Increment(ref _activeCount);
                state.Metrics.OnWorkingSetAdded();
                foreach (var observer in _observers)
                {
                    observer.OnAdded(member);
                }

                return;
            }

            throw new InvalidOperationException($"Member {member} is already a member of the working set");
        }

        public void OnActive(IActivationWorkingSetMember member)
        {
            if (_members.TryGetValue(member, out var state))
            {
                state.IsIdle = false;
            }
            else
            {
                state = CreateMemberState(member);
                if (_members.TryAdd(member, state))
                {
                    Interlocked.Increment(ref _activeCount);
                    state.Metrics.OnWorkingSetAdded();
                }
            }

            foreach (var observer in _observers)
            {
                observer.OnActive(member);
            }
        }

        public void OnEvicted(IActivationWorkingSetMember member)
        {
            if (_members.TryRemove(member, out var state))
            {
                Interlocked.Decrement(ref _activeCount);
                state.Metrics.OnWorkingSetRemoved();
                foreach (var observer in _observers)
                {
                    observer.OnEvicted(member);
                }
            }
        }

        private MemberState CreateMemberState(IActivationWorkingSetMember member) => new(member switch
        {
            ActivationData activation => activation.Shared.GrainTypeMetrics,
            IGrainContext context => _catalogInstruments.GetGrainTypeMetrics(context.GrainId.Type),
            _ => _catalogInstruments.GetGrainTypeMetrics(default)
        });

        public void OnDeactivating(IActivationWorkingSetMember member)
        {
            OnEvicted(member);
            foreach (var observer in _observers)
            {
                observer.OnDeactivating(member);
            }
        }

        public void OnDeactivated(IActivationWorkingSetMember member)
        {
            OnEvicted(member);
            foreach (var observer in _observers)
            {
                observer.OnDeactivated(member);
            }
        }

        private async Task MonitorWorkingSet()
        {
            while (await _scanPeriodTimer.NextTick())
            {
                foreach (var pair in _members)
                {
                    try
                    {
                        VisitMember(pair.Key, pair.Value);
                    }
                    catch (Exception exception)
                    {
                        LogExceptionVisitingWorkingSetMember(exception, pair.Key);
                    }
                }
            }
        }

        private void VisitMember(IActivationWorkingSetMember member, MemberState state)
        {
            var wouldRemove = state.IsIdle;
            if (member.IsCandidateForRemoval(wouldRemove))
            {
                if (wouldRemove)
                {
                    OnEvicted(member);
                }
                else
                {
                    state.IsIdle = true;
                    foreach (var observer in _observers)
                    {
                        observer.OnIdle(member);
                    }
                }
            }
            else
            {
                state.IsIdle = false;
                foreach (var observer in _observers)
                {
                    observer.OnActive(member);
                }
            }
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(
                nameof(ActivationWorkingSet),
                ServiceLifecycleStage.BecomeActive,
                StartMonitoring,
                StopMonitoring);

            Task StartMonitoring(CancellationToken ct)
            {
                using var _ = new ExecutionContextSuppressor();
                _runTask = Task.Run(MonitorWorkingSet, CancellationToken.None);
                return Task.CompletedTask;
            }

            async Task StopMonitoring(CancellationToken ct)
            {
                _scanPeriodTimer.Dispose();
                if (_runTask is Task task)
                {
                    await task.WaitAsync(ct).SuppressThrowing();
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Exception visiting working set member {Member}"
        )]
        private partial void LogExceptionVisitingWorkingSetMember(Exception exception, IActivationWorkingSetMember member);
    }

    /// <summary>
    /// 管理最近活动的 <see cref="IGrainContext"/> 实例集。
    /// </summary>
    public interface IActivationWorkingSet
    {
        /// <summary>
        /// 返回最近激活的 grain 启动次数。
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// 将新成员添加到工作集中。
        /// </summary>
        void OnActivated(IActivationWorkingSetMember member);

        /// <summary>
        /// 表示成员积极活动,应参加工作。
        /// </summary>
        void OnActive(IActivationWorkingSetMember member);

        /// <summary>
        /// 信号说,一个成员已开始停止工作。
        /// </summary>
        /// <param name="member"></param>
        void OnDeactivating(IActivationWorkingSetMember member);

        /// <summary>
        /// 信号显示成员已停止使用
        /// </summary>
        void OnDeactivated(IActivationWorkingSetMember member);
    }

    /// <summary>
    /// 从 <see cref="IActivationWorkingSet"/>的角度看,激活是指启动。
    /// </summary>
    public interface IActivationWorkingSetMember
    {
        /// <summary>
        /// 如果成员有资格被除名,则返回 <see langword="true"/> ,否则返回 <see langword="false"/> 。
        /// </summary>
        /// <returns><see langword="true"/> 如果该成员有资格被除名, <see langword="false"/> 就另外予以删除。</returns>
        /// <remarks>
        /// 如果该方法返回 <see langword="true"/> 和 <paramref name="wouldRemove"/> 是 <see langword="true"/>,则必须将该成员从工作组中除名,并有资格通过呼叫 <see cref="IActivationWorkingSet.OnActivated(IActivationWorkingSetMember)"/>再次添加。
        /// </remarks>
        bool IsCandidateForRemoval(bool wouldRemove);
    }

    /// <summary>
    /// A 音 A <see cref="IActivationWorkingSet"/> 观察员。
    /// </summary>
    public interface IActivationWorkingSetObserver
    {
        /// <summary>
        /// 当激活被添加到工作组时呼叫 。
        /// </summary>
        void OnAdded(IActivationWorkingSetMember member) { }

        /// <summary>
        /// 当激活启动时呼叫 。
        /// </summary>
        void OnActive(IActivationWorkingSetMember member) { }

        /// <summary>
        /// 当激活闲置时呼叫 。
        /// </summary>
        void OnIdle(IActivationWorkingSetMember member) { }

        /// <summary>
        /// 工作组移除激活时呼叫 。
        /// </summary>
        void OnEvicted(IActivationWorkingSetMember member) { }

        /// <summary>
        /// 当激活开始停止活动时呼叫 。
        /// </summary>
        void OnDeactivating(IActivationWorkingSetMember member) { }

        /// <summary>
        /// 激活解除后呼叫
        /// </summary>
        void OnDeactivated(IActivationWorkingSetMember member) { }
    }
}
