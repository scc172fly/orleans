//*********************************************************
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
//    implied. See the License for the specific language governing
//    permissions and limitations under the License.
//
//*********************************************************

namespace Samples.StorageProviders
{
    /// <summary>
    /// 定义 JSON 存储提供程序的底层接口，即
    /// 将 JSON 字符串写入底层存储的部分。上层负责
    /// Grain 状态数据与 JSON 之间的映射。
    /// </summary>
    /// <remarks>
    /// 有了此接口，大部分序列化层逻辑便可
    /// 在存储提供程序的基类中实现。
    /// </remarks>
    public interface IJSONStateDataManager : IDisposable
    {
        /// <summary>
        /// 从集合中删除与指定键关联的 Grain 状态。
        /// </summary>
        /// <param name="collectionName">集合名称，例如类型名称。</param>
        /// <param name="key">要删除的对象的主键。</param>
        System.Threading.Tasks.Task Delete(string collectionName, string key);

        /// <summary>
        /// 从存储读取 Grain 状态。
        /// </summary>
        /// <param name="collectionName">集合名称，例如类型名称。</param>
        /// <param name="key">要读取的对象的主键。</param>
        /// <returns>如果实体存在，则返回包含实体 JSON 表示的字符串；否则返回 null。</returns>
        System.Threading.Tasks.Task<string?> Read(string collectionName, string key);

        /// <summary>
        /// 将 Grain 状态写入存储。
        /// </summary>
        /// <param name="collectionName">集合名称，例如类型名称。</param>
        /// <param name="key">要写入的对象的主键。</param>
        /// <param name="entityData">包含实体 JSON 表示的字符串。</param>
        System.Threading.Tasks.Task Write(string collectionName, string key, string entityData);
    }
}
