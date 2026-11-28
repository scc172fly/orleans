using System.Collections.Generic;

namespace Orleans.Streams
{
    /// <summary>
    /// IResourceSelector 从资源列表中选择一定数量的资源。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal interface IResourceSelector<T>
    {
        /// <summary>
        /// 资源数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 尝试从资源列表中选择指定数量的资源，且不与现有选择重复。
        /// </summary>
        /// <param name="newSelectionCount"></param>
        /// <param name="existingSelection"></param>
        /// <returns></returns>
        List<T> NextSelection(int newSelectionCount, List<T> existingSelection);
    }
}
