using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 存储持久化流队列检查点。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class StreamCheckpointerGrainState
    {
        /// <summary>
        /// 获取或设置已持久化的检查点。
        /// </summary>
        [Id(0)]
        public string Checkpoint { get; set; } = string.Empty;
    }
}
