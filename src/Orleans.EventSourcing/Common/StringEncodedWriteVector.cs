namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 提供用于用作写矢量的一套紧凑、字符串编码的复制识别符操作。
    /// </summary>
    public static class StringEncodedWriteVector
    {

        // BitVector 复制件作为一套在字符串中编码的复制字符串
        // 位向量表示为一组其位数为 1 的复制ids
        // 此集以字符串形式写入, 包含复制的代码, 之前先有一个逗号
        //
        // 假设我们的复制品 命名A,B,BB,然后
        // 代表 代表 代表 000 
        // “ A” 表示 {A} 代表 100 
        // “A,B”表示 {A,B} 代表 110 
        // “,BB,A,B”代表 {A,B,B,BB}代表 111 

        /// <summary>
        /// 在 writeVector中获取一个位数
        /// </summary>
        /// <param name="writeVector">我们要从</param>获得位数的刻录矢量
        /// <param name="Replica">我们希望查找位数的复制件</param>
        /// <returns></returns>
        public static bool GetBit(string writeVector, string Replica)
        {
            var pos = writeVector.IndexOf(Replica, StringComparison.Ordinal);
            return pos > 0 && writeVector[pos - 1] == ',';
        }

        /// <summary>
        /// 切换 writeVector 中的比特之一并返回新值。
        /// </summary>
        /// <param name="writeVector">刻录矢量, 我们要翻转位</param>
        /// <param name="Replica">复制我们想要翻转位</param>
        /// <returns>翻转时位数的状态</returns>
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
