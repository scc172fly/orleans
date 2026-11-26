using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// IResourceSelector 从资源列表中选择一定量的资源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal interface IResourceSelector<T>
    {
        /// <summary>
        /// 资源数
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 尝试从资源列表中选择一定的资源计数, 与现有的选择不相重叠
        /// </summary>
        /// <param name="newSelectionCount"></param>
        /// <param name="existingSelection"></param>
        /// <returns></returns>
        List<T> NextSelection(int newSelectionCount, List<T> existingSelection);
    }
}
