using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// IAsyncBatchObservable 扩展方法用于通过代表进行观察的类别。
    /// </summary>
    /// <typeparam name="T">可见观测到的物体类型。</typeparam>
    internal class GenericAsyncBatchObserver<T> : IAsyncBatchObserver<T>
    {
        private readonly Func<IList<SequentialItem<T>>, Task> onNextAsync;
        private readonly Func<Exception, Task> onErrorAsync;
        private readonly Func<Task> onCompletedAsync;

        public GenericAsyncBatchObserver(Func<IList<SequentialItem<T>>, Task> onNextAsync, Func<Exception, Task> onErrorAsync, Func<Task> onCompletedAsync)
        {
            this.onNextAsync = onNextAsync ?? throw new ArgumentNullException(nameof(onNextAsync));
            this.onErrorAsync = onErrorAsync ?? throw new ArgumentNullException(nameof(onErrorAsync));
            this.onCompletedAsync = onCompletedAsync ?? throw new ArgumentNullException(nameof(onCompletedAsync));
        }

        public Task OnNextAsync(IList<SequentialItem<T>> items)
        {
            return this.onNextAsync(items);
        }

        public Task OnCompletedAsync()
        {
            return this.onCompletedAsync();
        }

        public Task OnErrorAsync(Exception ex)
        {
            return this.onErrorAsync(ex);
        }
    }
}
