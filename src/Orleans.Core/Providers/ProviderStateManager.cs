using System;
using System.Runtime.Serialization;
using Orleans.Runtime;

namespace Orleans.Providers
{
    internal enum ProviderState
    {
        None,

        Initialized,
        Started,
        Closed
    }
    internal class ProviderStateManager
    {
        public ProviderState State { get; private set; }
        private ProviderState presetState;

        public ProviderStateManager()
        {
            State = ProviderState.None;
        }

        public bool PresetState(ProviderState state)
        {
            presetState = state;
            switch (state)
            {
                case ProviderState.None:
                    throw new ProviderStateException("Provider state can not be set to none.");

                case ProviderState.Initialized:
                    switch (State)
                    {
                        case ProviderState.None:
                            return true;
                    }
                    break;

                case ProviderState.Started:
                    switch (State)
                    {
                        case ProviderState.None:
                            throw new ProviderStateException("Trying to start a provider that hasn't been initialized.");
                        case ProviderState.Initialized:
                            return true;
                        case ProviderState.Closed:
                            throw new ProviderStateException("Trying to start a provider that has been closed.");
                    }
                    break;

                case ProviderState.Closed:
                    switch (State)
                    {
                        case ProviderState.None:
                            throw new ProviderStateException("Trying to close a provider that hasn't been initialized.");
                        case ProviderState.Initialized:
                        case ProviderState.Started:
                            return true;
                    }
                    return true;
            }

            return false;
        }

        public void CommitState()
        {
            State = presetState;
        }
    }

    /// <summary>
    /// 代表无效的供应商生命周期状态或国家过渡。
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class ProviderStateException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="ProviderStateException"/> 类的新实例。
        /// </summary>
        public ProviderStateException() : base("Unexpected provider state")
        { }

        /// <summary>
        /// 初始化 <see cref="ProviderStateException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        public ProviderStateException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="ProviderStateException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        /// <param name="innerException">造成当前例外的例外。</param>
        public ProviderStateException(string message, Exception innerException) : base(message, innerException) { }

        [Obsolete]
        private ProviderStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

}
