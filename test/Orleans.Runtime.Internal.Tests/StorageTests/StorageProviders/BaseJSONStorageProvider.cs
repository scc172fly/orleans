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

using Newtonsoft.Json;
using Orleans.Runtime;
using Orleans.Storage;

namespace Samples.StorageProviders
{
    /// <summary>
    /// JSON- 基础 grain 存储提供者的基准类别。
    /// </summary>
    public abstract class BaseJSONStorageProvider : IGrainStorage
    {
        /// <summary>
        /// 存储供应商名称
        /// </summary>
        public string? Name { get; protected set; }

        /// <summary>
        /// 数据管理员实例
        /// </summary>
        /// <remarks>数据管理器负责读写 JSON 字符串。</remarks>
        protected IJSONStateDataManager? DataManager { get; set; }

        /// <summary>
        /// 构造建构器
        /// </summary>
        protected BaseJSONStorageProvider()
        {
        }

        /// <summary>
        /// 在 silo 关闭期间关闭存储提供者。
        /// </summary>
        /// <returns>这一行动的完成承诺。</returns>
        public Task Close()
        {
            if (DataManager != null)
                DataManager.Dispose();
            DataManager = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 背书店的读取状态持续, 并解除其向目标的序列
        /// grain 状态对象。
        /// </summary>
        /// <param name="grainType">字符串,内挂着 grain 类.</param>
        /// <param name="grainReference">代表长期身份 grain.</param>
        /// <param name="grainState">提及维持《公约》持续状态的物体 grain.</param>
        /// <returns>这一行动的完成承诺。</returns>
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
        /// 将持续状态从一个 grain 状态对象写入其备份仓库。
        /// </summary>
        /// <param name="grainType">字符串,内挂着 grain 类.</param>
        /// <param name="grainReference">代表长期身份 grain.</param>
        /// <param name="grainState">提及维持《公约》持续状态的物体 grain.</param>
        /// <returns>这一行动的完成承诺。</returns>
        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            if (DataManager == null) throw new ArgumentException("DataManager property not initialized");
            var entityData = ConvertToStorageFormat(grainState);
            grainState.RecordExists = true;
            return DataManager.Write(grainState.GetType().Name, grainId.ToString(), entityData);
        }

        /// <summary>
        /// 如果找到的话, 从备份存储中删除 grain 状态 。
        /// </summary>
        /// <param name="grainType">字符串,内挂着 grain 类.</param>
        /// <param name="grainReference">代表长期身份 grain.</param>
        /// <param name="grainState">保持该天体状态的物体 grain.</param>
        /// <returns></returns>
        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            if (DataManager == null) throw new ArgumentException("DataManager property not initialized");
            DataManager.Delete(grainState.GetType().Name, grainId.ToString());
            grainState.RecordExists = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从 grain 实例到 JSON 文档的序列。
        /// </summary>
        /// <param name="grainState">Grain 状态将转换成 JSON 储存格式。</param>
        /// <remarks>
        /// 见:
        /// http://msdn.microsoft.com/en-us/library/system.web.script.serialization.javascriptserializer.aspx
        /// 更多 JSON 连环器
        /// </remarks>
        protected static string ConvertToStorageFormat<T>(IGrainState<T> grainState)
        {
            return JsonConvert.SerializeObject(grainState.State);
        }

        /// <summary>
        /// 构造 a grain 状态,通过解除 JSON 文件。
        /// </summary>
        /// <param name="grainState">Grain 状态将被封存。</param>
        /// <param name="entityData">JSON 用于在 grain 状态。</param>
        protected static void ConvertFromStorageFormat<T>(IGrainState<T> grainState, string entityData)
        {
            var data = JsonConvert.DeserializeObject<T>(entityData);
            grainState.State = data!;
        }
    }

}
