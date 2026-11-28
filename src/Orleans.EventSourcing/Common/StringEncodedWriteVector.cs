namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 提供对写入向量的操作；该向量是以紧凑字符串编码的副本标识符集合。
    /// </summary>
    public static class StringEncodedWriteVector
    {

        // 副本的位向量通过编码到单个字符串中的副本标识符集合实现。
        // 位向量表示为对应位为 1 的副本 ID 的集合。
        // 将此集合写成一个字符串，其中每个副本 ID 前均有一个逗号。
        //
        // 假设副本命名为 A、B 和 BB，则：
        // ""     represents    {}        represents 000
        // ",A"   represents    {A}       represents 100
        // ",A,B" represents    {A,B}     represents 110
        // ",BB,A,B" represents {A,B,BB}  represents 111

        /// <summary>
        /// 获取 writeVector 中的某一位。
        /// </summary>
        /// <param name="writeVector">要从中获取位的写入向量。</param>
        /// <param name="Replica">要查询其对应位的副本。</param>
        /// <returns></returns>
        public static bool GetBit(string writeVector, string Replica)
        {
            var pos = writeVector.IndexOf(Replica, StringComparison.Ordinal);
            return pos > 0 && writeVector[pos - 1] == ',';
        }

        /// <summary>
        /// 翻转 writeVector 中的某一位，并返回新值。
        /// </summary>
        /// <param name="writeVector">要在其中翻转位的写入向量。</param>
        /// <param name="Replica">要翻转其对应位的副本。</param>
        /// <returns>翻转后该位的状态。</returns>
        public static bool FlipBit(ref string writeVector, string Replica)
        {
            var pos = writeVector.IndexOf(Replica, StringComparison.Ordinal);
            if (pos > 0 && writeVector[pos - 1] == ',')
            {
                var pos2 = writeVector.IndexOf(',', pos + 1);
                if (pos2 == -1)
                    pos2 = writeVector.Length;
                writeVector = writeVector.Remove(pos - 1, pos2 - pos + 1);
                return false;
            }
            else
            {
                writeVector = string.Format(",{0}{1}", Replica, writeVector);
                return true;
            }
        }
    }
}
