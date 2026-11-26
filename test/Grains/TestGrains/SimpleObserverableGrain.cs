using Microsoft.Extensions.Logging;
using Orleans.Utilities;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class SimpleObserverableGrain : Grain, ISimpleObserverableGrain
    {
        private readonly ILogger logger;
        internal int A { get; set; }
        internal int B { get; set; }
        internal int EventDelay { get; set; }
        internal ObserverManager<ISimpleGrainObserver> Observers { get; set; }

        public SimpleObserverableGrain(ILoggerFactory loggerFactory)
        {
            EventDelay = 1000;
            logger = loggerFactory.CreateLogger($"{nameof(SimpleObserverableGrain)}-{base.IdentityString}-{base.RuntimeIdentity}");
            this.Observers = new ObserverManager<ISimpleGrainObserver>(TimeSpan.FromMinutes(5), logger);
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Activate.");
            return Task.CompletedTask;
        }

        public async Task SetA(int a)
        {
            logger.LogInformation("SetA={A}", a);
            A = a;

            //如果用 Task.Run 运行, 则不需要添加 Unlop 调用 。
            //然而, Task.Run 运行中 ThreadPool 而不是在 Orleans TaskScheduler,与 Task.Factory.StartNew.
            //更多信息: https://learn.microsoft.com/dotnet/orleans/grains/external-tasks-and-grains。
            //额外任务来自内部因 Task.Delay而出现的无同步的羊羔。
            //洞察力,见 http://blogs.msdn.com/b/pfxteam/archive/2012/02/08/10265476.aspx。
            await Task.Factory.StartNew(async () =>
            {
                await Task.Delay(EventDelay);
                RaiseStateUpdateEvent();
            }).Unwrap();
        }

        public async Task SetB(int b)
        {
            this.B = b;

            //如果用 Task.Run 运行, 则不需要添加 Unlop 调用 。
            //然而, Task.Run 运行中 ThreadPool 而不是在 Orleans TaskScheduler,与 Task.Factory.StartNew.
            //更多信息: https://learn.microsoft.com/dotnet/orleans/grains/external-tasks-and-grains。
            //额外任务来自内部因 Task.Delay而出现的无同步的羊羔。
            //洞察力,见 http://blogs.msdn.com/b/pfxteam/archive/2012/02/08/10265476.aspx。
            await Task.Factory.StartNew(async () =>
            {
                await Task.Delay(EventDelay);
                RaiseStateUpdateEvent();
            }).Unwrap();
        }

        public async Task IncrementA()
        {
            await SetA(A + 1);
        }

        public Task<int> GetAxB()
        {
            return Task.FromResult(A * B);
        }

        public Task<int> GetAxB(int a, int b)
        {
            return Task.FromResult(a * b);
        }

        public Task<int> GetA()
        {
            return Task.FromResult(A);
        }

        public Task Subscribe(ISimpleGrainObserver observer)
        {
            Observers.Subscribe(observer, observer);
            return Task.CompletedTask;
        }

        public Task Unsubscribe(ISimpleGrainObserver observer)
        {
            Observers.Unsubscribe(observer);
            return Task.CompletedTask;
        }

        public Task<string> GetRuntimeInstanceId()
        {
            return Task.FromResult(RuntimeIdentity);
        }

        protected void RaiseStateUpdateEvent()
        {
            Observers.Notify((ISimpleGrainObserver observer) =>
            {
                observer.StateChanged(A, B);
            });
        }
    }
}
