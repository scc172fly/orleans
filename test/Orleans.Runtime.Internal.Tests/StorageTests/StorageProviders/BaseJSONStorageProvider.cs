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

using Newtonsoft.Json;
using Orleans.Runtime;
using Orleans.Storage;

namespace Samples.StorageProviders
{
    /// <summary>
    /// 基于 JSON 的 Grain 存储提供程序的基类。
    /// </summary>
    public abstract class BaseJSONStorageProvider : IGrainStorage
    {
        /// <summary>
        /// 存储提供程序名称
        /// </summary>
        public string? Name { get; protected set; }

        /// <summary>
        /// 数据管理器实例
        /// </summary>
        /// <remarks>数据管理器负责读取和写入 JSON 字符串。</remarks>
        protected IJSONStateDataManager? DataManager { get; set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        protected BaseJSONStorageProvider()
        {
        }

        /// <summary>
        /// 在 Silo 关闭期间关闭存储提供程序。
        /// </summary>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public Task Close()
        {
            if (DataManager != null)
                DataManager.Dispose();
            DataManager = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从后备存储读取持久化状态，并将其反序列化至目标
        /// Grain 状态对象。
        /// </summary>
        /// <param name="grainType">包含 Grain 类名称的字符串。</param>
        /// <param name="grainReference">表示 Grain 的长期有效标识。（译注：原注释使用 grainReference，当前方法的对应参数为 grainId。）</param>
        /// <param name="grainState">对用于保存 Grain 持久化状态的对象的引用。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            if (DataManager == null) throw new ArgumentException("DataManager property not initialized");
            var entityData = await DataManager.Read(grainState.GetType().Name, grainId.ToString());
            if (entityData != null)
            {
                ConvertFromStorageFormat(grainState, entityData);
                grainState.RecordExists = true;
            }
        }

        /// <summary>
        /// 将 Grain 状态对象中的持久化状态写入其后备存储。
        /// </summary>
        /// <param name="grainType">包含 Grain 类名称的字符串。</param>
        /// <param name="grainReference">表示 Grain 的长期有效标识。（译注：原注释使用 grainReference，当前方法的对应参数为 grainId。）</param>
        /// <param name="grainState">对持有 Grain 持久化状态的对象的引用。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            if (DataManager == null) throw new ArgumentException("DataManager property not initialized");
            var entityData = ConvertToStorageFormat(grainState);
            grainState.RecordExists = true;
            return DataManager.Write(grainState.GetType().Name, grainId.ToString(), entityData);
        }

        /// <summary>
        /// 如果存在 Grain 状态，则将其从后备存储移除。
        /// </summary>
        /// <param name="grainType">包含 Grain 类名称的字符串。</param>
        /// <param name="grainReference">表示 Grain 的长期有效标识。（译注：原注释使用 grainReference，当前方法的对应参数为 grainId。）</param>
        /// <param name="grainState">持有 Grain 持久化状态的对象。</param>
        /// <returns></returns>
        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            if (DataManager == null) throw new ArgumentException("DataManager property not initialized");
            DataManager.Delete(grainState.GetType().Name, grainId.ToString());
            grainState.RecordExists = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 将 Grain 实例序列化为 JSON 文档。
        /// </summary>
        /// <param name="grainState">要转换为 JSON 存储格式的 Grain 状态。</param>
        /// <remarks>
        /// 参见：
        /// http://msdn.microsoft.com/en-us/library/system.web.script.serialization.javascriptserializer.aspx
        /// 以了解有关 JSON 序列化器的更多信息。
        /// </remarks>
        protected static string ConvertToStorageFormat<T>(IGrainState<T> grainState)
        {
            return JsonConvert.SerializeObject(grainState.State);
        }

        /// <summary>
        /// 通过反序列化 JSON 文档构造 Grain 状态实例。
        /// </summary>
        /// <param name="grainState">需要填充以供存储的 Grain 状态。</param>
        /// <param name="entityData">Grain 状态的 JSON 存储格式表示。</param>
        protected static void ConvertFromStorageFormat<T>(IGrainState<T> grainState, string entityData)
        {
            var data = JsonConvert.DeserializeObject<T>(entityData);
            grainState.State = data!;
        }
    }

}
