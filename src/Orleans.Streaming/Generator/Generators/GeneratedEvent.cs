using System;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// 生成流中事件使用
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class GeneratedEvent
    {
        /// <summary>
        /// 生成事件类型
        /// </summary>
        public enum GeneratedEventType
        {
            /// <summary>
            /// 填充事件
            /// </summary>
            Fill,

            /// <summary>
            /// 触发报告的活动
            /// </summary>
            Report
        }

        /// <summary>
        /// 获取或设置事件类型。
        /// </summary>
        [Id(0)]
        public GeneratedEventType EventType { get; set; }

        /// <summary>
        /// 获取或设置有效载荷。
        /// </summary>
        [Id(1)]
        public int[]? Payload { get; set; }
    }
}
