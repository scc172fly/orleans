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
    /// 使用文件作为后备存储的 Orleans 存储提供程序实现。
    /// </summary>
    /// <remarks>
    /// 应在 Orleans 服务器配置文件中添加以下行，将该存储提供程序纳入部署：
    /// 
    ///     <Provider Type="Samples.StorageProviders.OrleansFileStorage" Name="FileStore" RooDirectory="SOME FILE PATH" />
    ///
    /// 并在使用它的任意 Grain 中添加以下行：
    /// 
    ///     [Orleans.Providers.StorageProvider(ProviderName = "FileStore")]
    /// 
    /// 名称 'FileStore' 是任意选定的。
    /// 
    /// 注意，除非根目录路径是部署中所有 Silo 都可访问的网络路径，否则 Grain 状态
    /// 无法从一个 Silo 转移至另一个 Silo。
    /// </remarks>
    public class OrleansFileStorage : BaseJSONStorageProvider
    {
        public OrleansFileStorage(string rootDirectory)
        {
            this.RootDirectory = rootDirectory;
            if (string.IsNullOrWhiteSpace(RootDirectory)) throw new ArgumentException("RootDirectory property not set");
            DataManager = new GrainStateFileDataManager(RootDirectory);
        }

        /// <summary>
        /// 相对于 Silo 主机的目录路径。初始化期间根据
        /// 配置数据设置。
        /// </summary>
        public string RootDirectory { get; set; }
    }

    /// <summary>
    /// 与文件系统交互。
    /// </summary>
    internal class GrainStateFileDataManager : IJSONStateDataManager
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="storageDirectory">相对于 Silo 宿主进程工作目录的路径。</param>
        public GrainStateFileDataManager(string storageDirectory)
        {
            directory = new DirectoryInfo(storageDirectory);
            if (!directory.Exists)
                directory.Create();
        }

        /// <summary>
        /// 删除表示 Grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">Grain 状态对象的类型名称。</param>
        /// <param name="key">Grain ID 字符串。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public Task Delete(string collectionName, string key)
        {
            FileInfo fileInfo = GetStorageFilePath(collectionName, key);

            if (fileInfo.Exists)
                fileInfo.Delete();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 读取表示 Grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">Grain 状态对象的类型名称。</param>
        /// <param name="key">Grain ID 字符串。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task<string?> Read(string collectionName, string key)
        {
            FileInfo fileInfo = GetStorageFilePath(collectionName, key);

            if (!fileInfo.Exists)
                return null;

            using (var stream = fileInfo.OpenText())
            {
                return await stream.ReadToEndAsync();
            }
        }

        /// <summary>
        /// 写入表示 Grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">Grain 状态对象的类型名称。</param>
        /// <param name="key">Grain ID 字符串。</param>
        /// <param name="entityData">要存储的 Grain 状态数据。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task Write(string collectionName, string key, string entityData)
        {
            FileInfo fileInfo = GetStorageFilePath(collectionName, key);

            using (var stream = new StreamWriter(fileInfo.Open(FileMode.Create, FileAccess.Write)))
            {
                await stream.WriteAsync(entityData);
            }
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// 返回使用这些键存储该数据的文件路径。
        /// </summary>
        /// <param name="collectionName">Grain 状态对象的类型名称。</param>
        /// <param name="key">Grain ID 字符串。</param>
        /// <returns>此存储数据文件的文件信息。</returns>
        private FileInfo GetStorageFilePath(string collectionName, string key)
        {
            string fileName = (key + "." + collectionName).Replace('/', '_');
            string path = Path.Combine(directory.FullName, fileName);
            return new FileInfo(path);
        }

        private readonly DirectoryInfo directory;
    }
}
