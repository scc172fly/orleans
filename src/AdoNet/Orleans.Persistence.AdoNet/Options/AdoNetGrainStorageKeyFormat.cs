namespace Orleans.Configuration
{
    /// <summary>
    /// 指定如何 ADO.NET grain 储存图a grain 键栏 <c>OrleansStorage</c> 表格。
    /// </summary>
    /// <remarks>
    /// 关键格式决定状态存储的位置。更改现有数据库的格式可以让状态
    /// 与先前的格式写成,无法到达;不进行自动迁移。
    /// </remarks>
    public enum AdoNetGrainStorageKeyFormat
    {
        /// <summary>
        /// 国家名称存放在 <c>GrainTypeString</c> 键和键行 grain 仅键。整数和图形界面
        /// 以解析 grain 密钥的方式识别。
        /// </summary>
        /// <remarks>
        /// 这是先前发布版本和 Orleans v3 迁移所使用的格式,这些迁移在
        /// Orleans v3 grain 类。 Grains 类型不同,共用一个键,国名一行,
        /// 向相同整数或图形用户界面数据键解析的字符串密钥,例如 <c>"20235"</c> 和 <c>"020235"</c>,
        /// 共享一行。当键在数据库核对方式上不同时,也只用散列区分行。
        /// 忽略,例如默认 SQL 服务器和 MySQL 核对的字母大小写。
        /// </remarks>
        Legacy = 0,

        /// <summary>
        /// 将 grain 类型、状态名称和 grain 键作为精确字节的十六进制文本储存,以便每个字节
        /// 组合 grain 类型、国名和 grain 键在任何数据库核对和共享下都有自己的行。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>GrainTypeString</c> 持有 <c>"{hex(grain type)}/{hex(UTF-8 state name)}"</c> 和
        /// <c>GrainIdExtensionString</c> 持有 <c>hex(grain key)</c>; <c>GrainIdN0</c> 和 <c>GrainIdN1</c> 为零。
        /// 例如, grain <c>"cart/1A"</c> ,状态 <c>"state"</c> 与 <c>GrainTypeString</c>一起储存。
        /// <c>"63617274/7374617465"</c> 和 <c>GrainIdExtensionString</c> <c>"3141"</c>。
        /// </para>
        /// <para>
        /// 十六进制文本将存储的长度翻倍: grain 类型和国名加在一起,以及 grain 键有限
        /// 512字符字符键栏下大约255字节。
        /// <see cref="System.ArgumentException"/>.
        /// 此格式不能与 <see cref="AdoNetGrainStorageOptions.UseOrleans3CompatibleHasher"/>合并。
        /// </para>
        /// </remarks>
        GrainTypeQualified = 1,
    }
}
