using System;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// IAsyncObservable 扩展方法用于通过代表进行观察的类别。
    /// </summary>
    /// <typeparam name="T">可见观测到的物体类型。</typeparam>
    internal class GenericAsyncObserver<T> : IAsyncObserver<T>
    {
        private readonly Func<T, StreamSequenceToken?, Task> onNextAsync;
        private readonly Func<Exception, Task> onErrorAsync;
        private readonly Func<Task> onCompletedAsync;

        public GenericAsyncObserver(Func<T, StreamSequenceToken?, Task> onNextAsync, Func<Exception, Task> onErrorAsync, Func<Task> onCompletedAsync)
        {
            if (onNextAsync == null) throw new ArgumentNullException(nameof(onNextAsync));
            if (onErrorAsync == null) throw new ArgumentNullException(nameof(onErrorAsync));
            if (onCompletedAsync == null) throw new ArgumentNullException(nameof(onCompletedAsync));

            this.onNextAsync = onNextAsync;
            this.onErrorAsync = onErrorAsync;
            this.onCompletedAsync = onCompletedAsync;
        }

        public Task OnNextAsync(T item, StreamSequenceToken? token = null)
        {
            return onNextAsync(item, token);
        }

        public Task OnCompletedAsync()
        {
            return onCompletedAsync();
        }

        public Task OnErrorAsync(Exception ex)
        {
            return onErrorAsync(ex);
        }
    }
}
