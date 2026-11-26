//*********************************************************
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2. 0 许可证
//    
//    持有许可的芬兰公民可前往移民局申请居留许可。
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    除非适用法要求或书面约定,软件 
//    许可证下发放的,在“ASIS”BASISS上分发。 
//    没有任何战争或任何种类的战争或条件,无论是明示的还是 
//    意指的语种,请见有关 
//    许可证规定的许可和限制。
//
//*********************************************************

namespace Samples.StorageProviders
{
    /// <summary>
    /// 定义 JSON 存储提供者 i.e较低级别的储存供应商的接口。
    /// 将 JSON 字符串写入底存储的字符串。较高水平
    /// grain 国家数据和 JSON之间地图。
    /// </summary>
    /// <remarks>
    /// 拥有此界面可以使大多数序列级逻辑
    /// 将在储存提供者的基级类别中实施。
    /// </remarks>
    public interface IJSONStateDataManager : IDisposable
    {
        /// <summary>
        /// 从收藏中删除与给定密钥相关的 grain 状态
        /// </summary>
        /// <param name="collectionName">收藏库的名称,例如类型名称</param>
        /// <param name="key">删除对象的主要关键字</param>
        System.Threading.Tasks.Task Delete(string collectionName, string key);

        /// <summary>
        /// 从存储中读取 grain 状态。
        /// </summary>
        /// <param name="collectionName">收藏库的名称,如类型名称。</param>
        /// <param name="key">对象要读取的主键。</param>
        /// <returns>一个含有实体 JSON 表示的字符串,如果有的话;否则无效。</returns>
        System.Threading.Tasks.Task<string?> Read(string collectionName, string key);

        /// <summary>
        /// 将 grain 状态写入存储状态。
        /// </summary>
        /// <param name="collectionName">收藏库的名称,如类型名称。</param>
        /// <param name="key">要写入的对象的主键。</param>
        /// <param name="entityData">含有 JSON 代表实体。</param>
        System.Threading.Tasks.Task Write(string collectionName, string key, string entityData);
    }
}
