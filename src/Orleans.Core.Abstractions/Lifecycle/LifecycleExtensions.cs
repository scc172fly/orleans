using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans
{
    /// <summary>
    /// 用于操作生命周期观察者的扩展方法。
    /// </summary>
    public static class LifecycleExtensions
    {
        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="observerName">观察者名称。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="onStart">启动指定生命周期阶段时调用的委托。</param>
        /// <param name="onStop">停止指定生命周期阶段时调用的委托。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe(this ILifecycleObservable observable, string observerName, int stage, Func<CancellationToken, Task> onStart, Func<CancellationToken, Task>? onStop)
        {
            if (observable is null)
            {
                throw new ArgumentNullException(nameof(observable));
            }

            if (onStart is null)
            {
                throw new ArgumentNullException(nameof(onStart));
            }

            if (onStop is null)
            {
                return StartupObserver.Create(observable, observerName, stage, onStart);
            }

            return observable.Subscribe(observerName, stage, new Observer(onStart, onStop));
        }

        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="observerName">观察者名称。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="onStart">启动指定生命周期阶段时调用的委托。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe(this ILifecycleObservable observable, string observerName, int stage, Func<CancellationToken, Task> onStart)
        {
            return observable.Subscribe(observerName, stage, onStart, null);
        }

        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <typeparam name="TObserver">
        /// 观察者类型，用于诊断。
        /// </typeparam>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="observer">观察者。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe<TObserver>(this ILifecycleObservable observable, int stage, ILifecycleObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observable);
            ArgumentNullException.ThrowIfNull(observer);
            return observable.Subscribe(GetTypeName(typeof(TObserver)), stage, observer);
        }

        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <typeparam name="TObserver">
        /// 观察者类型，用于诊断。
        /// </typeparam>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="onStart">启动指定生命周期阶段时调用的委托。</param>
        /// <param name="onStop">停止指定生命周期阶段时调用的委托。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe<TObserver>(this ILifecycleObservable observable, int stage, Func<CancellationToken, Task> onStart, Func<CancellationToken, Task> onStop)
        {
            return observable.Subscribe(GetTypeName(typeof(TObserver)), stage, onStart, onStop);
        }

        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <typeparam name="TObserver">
        /// 观察者类型，用于诊断。
        /// </typeparam>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="onStart">启动指定生命周期阶段时调用的委托。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe<TObserver>(this ILifecycleObservable observable, int stage, Func<CancellationToken, Task> onStart)
        {
            return observable.Subscribe(GetTypeName(typeof(TObserver)), stage, onStart, null);
        }

        /// <summary>
        /// 创建可释放的生命周期订阅。
        /// </summary>
        /// <param name="observable">可观察的生命周期。</param>
        /// <param name="stage">要参与的阶段。</param>
        /// <param name="observer">观察者。</param>
        /// <returns>一个 <see cref="IDisposable"/> 实例，释放它即可取消观察者对生命周期的订阅。</returns>
        public static IDisposable Subscribe(this ILifecycleObservable observable, int stage, ILifecycleObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observable);
            ArgumentNullException.ThrowIfNull(observer);
            return observable.Subscribe(GetTypeName(observer.GetType()), stage, observer);
        }

        private class Observer : ILifecycleObserver
        {
            private readonly Func<CancellationToken, Task> onStart;
            private readonly Func<CancellationToken, Task> onStop;

            public Observer(Func<CancellationToken, Task> onStart, Func<CancellationToken, Task> onStop)
            {
                this.onStart = onStart;
                this.onStop = onStop;
            }

            public Task OnStart(CancellationToken ct) => this.onStart(ct);
            public Task OnStop(CancellationToken ct) => this.onStop(ct);
        }

        private sealed class StartupObserver : ILifecycleObserver
        {
            private readonly Func<CancellationToken, Task> _onStart;
            private readonly IDisposable _registration;

            private StartupObserver(ILifecycleObservable observable, string observerName, int stage, Func<CancellationToken, Task> onStart)
            {
                _onStart = onStart;
                _registration = observable.Subscribe(observerName, stage, this);
            }

            public static IDisposable Create(ILifecycleObservable observable, string observerName, int stage, Func<CancellationToken, Task> onStart)
            {
                var observer = new StartupObserver(observable, observerName, stage, onStart);
                return observer._registration;
            }

            public Task OnStart(CancellationToken ct)
            {
                var task = _onStart(ct);
                _registration?.Dispose();
                return task;
            }

            public Task OnStop(CancellationToken ct) => Task.CompletedTask;
        }

        private static string GetTypeName(Type type) => type.FullName ?? type.Name;
    }
}
