using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 储存一个长串排队检查站。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class StreamCheckpointerGrainState
    {
        /// <summary>
        /// 设置或设置长期存在的检查站。
        /// </summary>
        [Id(0)]
        public string Checkpoint { get; set; } = string.Empty;
    }
}
