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
    /// 实施文件备份仓库的存储供应商Orleans 。
    /// </summary>
    /// <remarks>
    /// 将这一行添加到 Orleans 服务器配置文件,从而在部署中包括存储提供者:
    /// 
    ///     <Provider Type="Samples.StorageProviders.OrleansFileStorage" Name="FileStore" RooDirectory="SOME FILE PATH" />
    ///
    /// 线和此直线 grain 使用该词的:
    /// 
    ///     [Orleans.Providers.StorageProvider(ProviderName = "FileStore")]
    /// 
    /// `FileStore' 的名称是任意选择的。
    /// 
    /// 请注意,除非根目录路径是部署中所有 silos 都可用的网络路径, grain 状态
    /// 绝不能从一物的被降下, silo 对另一个。
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
        /// 相对于 silo主主机的目录路径。 设置自
        /// 初始化时配置数据。
        /// </summary>
        public string RootDirectory { get; set; }
    }

    /// <summary>
    /// 与文件系统的接口。
    /// </summary>
    internal class GrainStateFileDataManager : IJSONStateDataManager
    {
        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="storageDirectory">相对于 silo 主机进程工作目录的路径。</param>
        public GrainStateFileDataManager(string storageDirectory)
        {
            directory = new DirectoryInfo(storageDirectory);
            if (!directory.Exists)
                directory.Create();
        }

        /// <summary>
        /// 删除代表 grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">类型 grain 状态对象。</param>
        /// <param name="key"> grain id字符串。</param>
        /// <returns>这一行动的完成承诺。</returns>
        public Task Delete(string collectionName, string key)
        {
            FileInfo fileInfo = GetStorageFilePath(collectionName, key);

            if (fileInfo.Exists)
                fileInfo.Delete();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 读取代表 grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">类型 grain 状态对象。</param>
        /// <param name="key"> grain id字符串。</param>
        /// <returns>这一行动的完成承诺。</returns>
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
        /// 写入一个代表 grain 状态对象的文件。
        /// </summary>
        /// <param name="collectionName">类型 grain 状态对象。</param>
        /// <param name="key"> grain id字符串。</param>
        /// <param name="entityData">下 grain 状态数据待存储。/</param>
        /// <returns>这一行动的完成承诺。</returns>
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
        /// 返回用这些密钥存储该数据的文件路径。
        /// </summary>
        /// <param name="collectionName">类型 grain 状态对象。</param>
        /// <param name="key"> grain id字符串。</param>
        /// <returns>此存储数据文件的文件信息 。</returns>
        private FileInfo GetStorageFilePath(string collectionName, string key)
        {
            string fileName = (key + "." + collectionName).Replace('/', '_');
            string path = Path.Combine(directory.FullName, fileName);
            return new FileInfo(path);
        }

        private readonly DirectoryInfo directory;
    }
}
