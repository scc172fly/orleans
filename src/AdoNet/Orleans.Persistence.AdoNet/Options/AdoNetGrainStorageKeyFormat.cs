namespace Orleans.Configuration
{
    /// <summary>
    /// 指定 ADO.NET Grain 存储如何将 Grain 状态映射到 <c>OrleansStorage</c> 表的键列。
    /// </summary>
    /// <remarks>
    /// 键格式决定状态存储的位置。更改现有数据库的格式后，使用旧格式写入的状态
    /// 将无法访问；不会自动进行迁移。
    /// </remarks>
    public enum AdoNetGrainStorageKeyFormat
    {
        /// <summary>
        /// 将状态名称存入 <c>GrainTypeString</c>，仅使用 Grain 键作为行键。通过解析 Grain 键来识别
        /// 整数键及 GUID 键。
        /// </summary>
        /// <remarks>
        /// 此格式用于早期版本，也用于从 Orleans v3 迁移并以 Orleans v3 Grain 类名命名各个状态的情形。
        /// 如果不同类型的 Grain 具有相同的键及状态名称，则会共享一行；
        /// 可解析为同一个整数键或 GUID 键的字符串键（例如 <c>"20235"</c> 与 <c>"020235"</c>）
        /// 也会共享一行。如果不同的键仅在数据库排序规则忽略的方面存在差异，则仅通过哈希区分这些行，
        /// 例如 SQL Server 和 MySQL 默认排序规则下的字母大小写。
        /// </remarks>
        Legacy = 0,

        /// <summary>
        /// 将 Grain 类型、状态名称及 Grain 键的确切字节存为十六进制文本，因此在任何数据库排序规则及哈希算法下，
        /// 每种 Grain 类型、状态名称及 Grain 键的组合都具有独立的行。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>GrainTypeString</c> 存放 <c>"{hex(grain type)}/{hex(UTF-8 state name)}"</c>，而
        /// <c>GrainIdExtensionString</c> 存放 <c>hex(grain key)</c>；<c>GrainIdN0</c> 和 <c>GrainIdN1</c> 均为零。
        /// 例如，标识为 <c>"cart/1A"</c> 的 Grain（状态名称为 <c>"state"</c>）存储时，<c>GrainTypeString</c>
        /// 为 <c>"63617274/7374617465"</c>，<c>GrainIdExtensionString</c> 为 <c>"3141"</c>。
        /// </para>
        /// <para>
        /// 十六进制文本使存储长度翻倍：受 512 字符键列限制，Grain 类型与状态名称的总长度以及 Grain 键的长度
        /// 均限制在约 255 字节。对超出此长度的标识执行操作将抛出
        /// <see cref="System.ArgumentException"/>.
        /// 此格式不能与 <see cref="AdoNetGrainStorageOptions.UseOrleans3CompatibleHasher"/> 结合使用。
        /// </para>
        /// </remarks>
        GrainTypeQualified = 1,
    }
}
