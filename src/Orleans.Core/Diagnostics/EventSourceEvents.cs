using System.Diagnostics.Tracing;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="CallbackData"/>事件源。
    /// </summary>
    [EventSource(Name = "Microsoft-Orleans-CallBackData")]
    internal sealed class OrleansCallBackDataEvent : EventSource
    {
        public static readonly OrleansCallBackDataEvent Instance = new OrleansCallBackDataEvent();

        /// <summary>
        /// 表示请求暂停执行。
        /// </summary>
        /// <param name="message">电文。</param>
        [NonEvent]
        public void OnTimeout(Message message)
        {
            if (this.IsEnabled())
            {
                this.OnTimeout();
            }
        }

        /// <summary>
        /// 表示请求暂停执行。
        /// </summary>
        [Event(1, Level = EventLevel.Warning)]
        private void OnTimeout() => this.WriteEvent(1);

        /// <summary>
        /// 表示目标 silo 失败。
        /// </summary>
        /// <param name="message">向目标发出的信息 silo.</param>
        [NonEvent]
        public void OnTargetSiloFail(Message message)
        {
            if (this.IsEnabled())
            {
                this.OnTargetSiloFail();
            }
        }

        /// <summary>
        /// 表示目标 silo 失败。
        /// </summary>
        [Event(2, Level = EventLevel.Warning)]
        private void OnTargetSiloFail() => this.WriteEvent(2);

        /// <summary>
        /// 表示已完成请求。
        /// </summary>
        [NonEvent]
        public void DoCallback(Message message)
        {
            if (this.IsEnabled())
            {
                this.DoCallback();
            }
        }

        /// <summary>
        /// 表示已完成请求。
        /// </summary>
        [Event(3, Level = EventLevel.Verbose)]
        private void DoCallback() => this.WriteEvent(3);

        /// <summary>
        /// 表示请求被取消
        /// </summary>
        [NonEvent]
        public void OnCanceled(Message message)
        {
            if (this.IsEnabled())
            {
                this.OnCanceled();
            }
        }

        /// <summary>
        /// 表示请求被取消
        /// </summary>
        [Event(4, Level = EventLevel.Verbose)]
        private void OnCanceled() => this.WriteEvent(4);
    }

    [EventSource(Name = "Microsoft-Orleans-OutsideRuntimeClient")]
    internal sealed class OrleansOutsideRuntimeClientEvent : EventSource
    {
        public static readonly OrleansOutsideRuntimeClientEvent Instance = new OrleansOutsideRuntimeClientEvent();

        [NonEvent]
        public void SendRequest(Message message)
        {
            if (this.IsEnabled())
            {
                this.SendRequest();
            }
        }

        [Event(1, Level = EventLevel.Verbose)]
        private void SendRequest() => this.WriteEvent(1);

        [NonEvent]
        public void ReceiveResponse(Message message)
        {
            if (this.IsEnabled())
            {
                this.ReceiveResponse();
            }
        }

        [Event(2, Level = EventLevel.Verbose)]
        private void ReceiveResponse() => this.WriteEvent(2);

        [NonEvent]
        public void SendResponse(Message message)
        {
            if (this.IsEnabled())
            {
                this.SendResponse();
            }
        }

        [Event(3, Level = EventLevel.Verbose)]
        private void SendResponse() => this.WriteEvent(3);
    }

    [EventSource(Name = "Microsoft-Orleans-Dispatcher")]
    internal sealed class OrleansDispatcherEvent : EventSource
    {
        public static readonly OrleansDispatcherEvent Instance = new OrleansDispatcherEvent();

        [NonEvent]
        public void ReceiveMessage(Message message)
        {
            if (this.IsEnabled())
            {
                this.ReceiveMessage();
            }
        }

        [Event(1, Level = EventLevel.Verbose)]
        private void ReceiveMessage() => WriteEvent(1);
    }

    [EventSource(Name = "Microsoft-Orleans-InsideRuntimeClient")]
    internal sealed class OrleansInsideRuntimeClientEvent : EventSource
    {
        public static readonly OrleansInsideRuntimeClientEvent Instance = new OrleansInsideRuntimeClientEvent();

        [NonEvent]
        public void SendRequest(Message message)
        {
            if (this.IsEnabled())
            {
                this.SendRequest();
            }
        }

        [Event(1, Level = EventLevel.Verbose)]
        private void SendRequest() => WriteEvent(1);

        [NonEvent]
        public void ReceiveResponse(Message message)
        {
            if (this.IsEnabled())
            {
                this.ReceiveResponse();
            }
        }

        [Event(2, Level = EventLevel.Verbose)]
        private void ReceiveResponse() => WriteEvent(2);

        [NonEvent]
        public void SendResponse(Message message)
        {
            if (this.IsEnabled())
            {
                this.SendResponse();
            }
        }

        [Event(3, Level = EventLevel.Verbose)]
        private void SendResponse() => WriteEvent(3);
    }

    [EventSource(Name = "Microsoft-Orleans-IncomingMessageAgent")]
    internal sealed class OrleansIncomingMessageAgentEvent : EventSource
    {
        public static readonly OrleansIncomingMessageAgentEvent Log = new OrleansIncomingMessageAgentEvent();

        [NonEvent]
        public void ReceiveMessage(Message message)
        {
            if (this.IsEnabled())
            {
                this.ReceiveMessage();
            }
        }

        [Event(1, Level = EventLevel.Verbose)]
        private void ReceiveMessage() => WriteEvent(1);
    }
}
