namespace Orleans.Storage
{
    /// <summary>
    /// 当前所有散列操作的界面 Orleans 储存作业。
    /// </summary>
    /// <remarks>执行此功能,为数据库中带有具体属性的密钥提供支流。
    /// 举例说:对失控身份证提供者的碰撞抗力。</remarks>
    public interface IHasher
    {
        /// <summary>
        /// 描述散列功能。
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 大麻烟
        /// </summary>
        /// <param name="data">类组数据。</param>
        /// <returns>给定的字节散列。</returns>
        int Hash(byte[] data);
    }
}
