using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Orleans.TestingHost.Utils
{
    /// <summary>
    /// 遗留窗口的折纸夹 Azure 存储模拟器
    /// </summary>
    /// <remarks>
    /// 此助手仅管理以下所列遗留模拟程序执行文件;
    /// 无法启动或阻止 Azurite。使用
    /// <see href="https://learn.microsoft.com/azure/storage/common/storage-use-azurite">用于本地 Azure 储存开发的阿祖里特</see>
    /// 支持的本地仿真。
    /// </remarks>
    public static class StorageEmulator
    {
        /// <summary>
        /// 存储模拟进程名称。 引用运行中进程名称的一种方法是
        /// Get- process {} 格式表Id, ProcessName - 自动大小。 如果存在多个存储模拟器
        /// 运行的过程,他们会命名为WASTOR~1,WASTOR~2, ...WASTOR~N。
        /// </summary>
        private static readonly string[] storageEmulatorProcessNames = new[]
        {
            "AzureStorageEmulator", // 最新
            "Windows Azure Storage Emulator Service", // >= 2.7
            "WAStorageEmulator", // < 2.7
        };

        //文件名和进程名不一样 。
        private static readonly string[] storageEmulatorFilenames = new[]
        {
            "AzureStorageEmulator.exe", // >= 2.7
            "WAStorageEmulator.exe", // < 2.7
        };

        /// <summary>
        /// 存储模拟器已经启动吗 。
        /// </summary>
        /// <returns><see langword="true" /> 如果开始采用这一实例;否则, <see langword="false" />。</returns>
        public static bool IsStarted()
        {
            return GetStorageEmulatorProcess();
        }


        /// <summary>
        /// 检查存储模拟器是否存在, i.e是否安装。
        /// </summary>
        /// <value><see langword="true" /> ,如果储存模拟器存在;否则, <see langword="false" />。</value>
        public static bool Exists
        {
            get
            {
                return GetStorageEmulatorPath() != null;
            }
        }

        /// <summary>
        /// 存储模拟器帮助 。
        /// </summary>
        /// <returns>存储模拟器帮助。</returns>
        public static string Help()
        {
            if (!IsStarted()) return "Error happened. Has StorageEmulator.Start() been called?";

            try
            {
                //此程序会立即处理返回事宜 。
                using (var process = Process.Start(CreateProcessArguments("help"))!)
                {
                    process.WaitForExit();
                    StringBuilder help = new();
                    while (!process.StandardOutput.EndOfStream)
                    {
                        help.Append(process.StandardOutput.ReadLine());
                    }

                    return help.ToString();
                }
            }
            catch (Exception exc)
            {
                return exc.ToString();
            }
        }

        /// <summary>
        /// 尝试启动存储模拟器 。
        /// </summary>
        /// <returns><see langword="true"/> ,如果进程成功启动; <see langword="false"/> ,否则。</returns>
        public static bool TryStart()
        {
            if (!StorageEmulator.Exists)
                return false;

            return Start();
        }

        /// <summary>
        /// 如果尚未启动存储模拟器,则启动该模拟器。
        /// </summary>
        /// <returns><see langword="true"/> ,如果进程成功启动; <see langword="false"/> ,否则。</returns>
        public static bool Start()
        {
            if (IsStarted()) return true;

            try
            {
                //此程序会立即处理返回事宜 。
                using (var process = Process.Start(CreateProcessArguments("start")))
                {
                    if (process == null) return false;
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 如果启动, 停止存储模拟器 。
        /// </summary>
        /// <returns><see langword="true"/> ,如果进程成功停止或已经停止; <see langword="false"/> ,否则。</returns>
        public static bool Stop()
        {
            if (!IsStarted()) return false;

            try
            {
                //此程序会立即处理返回事宜 。
                using (var process = Process.Start(CreateProcessArguments("stop"))!)
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 创建一个新的 <see cref="ProcessStartInfo">ProcessStartInfo</see> ,用作参数
        /// 用于本类的其他操作。
        /// </summary>
        /// <param name="arguments">参数。</param>
        /// <returns>一个新的 <see cref="ProcessStartInfo"/> 并有既定的论点。</returns>
        private static ProcessStartInfo CreateProcessArguments(string arguments)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            return new ProcessStartInfo(GetStorageEmulatorPath()!)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = true,
                LoadUserProfile = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = arguments
            };
#pragma warning restore CA1416
        }

        /// <summary>
        /// 从系统中查询存储模拟器程序。
        /// </summary>
        /// <returns><see langword="true" /> 如果找到存储模拟程序, <see langword="false" /> 否则。</returns>
        private static bool GetStorageEmulatorProcess()
        {
            foreach (var name in storageEmulatorProcessNames)
            {
                var ps = Process.GetProcessesByName(name);
                if (ps.Length != 0)
                {
                    foreach (var p in ps)
                        p.Dispose();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 返回存储模拟器可执行文件的完整路径,包括可执行文件名称和扩展名。
        /// </summary>
        /// <returns>存储模拟器可执行的完整路径,如果找不到则无效。</returns>
        private static string? GetStorageEmulatorPath()
        {
            //尝试使用最新的已知模拟器路径。 如果它不存在, 请试用旧的路径 。
            string exeBasePath = Path.Combine(GetProgramFilesBasePath(), @"Microsoft SDKs\Azure\Storage Emulator\");

            return storageEmulatorFilenames
                .Select(filename => Path.Combine(exeBasePath, filename))
                .FirstOrDefault(File.Exists);
        }


        /// <summary>
        /// 确定程序文件基准目录。
        /// </summary>
        /// <returns>程序文件基础目录。</returns>
        private static string GetProgramFilesBasePath()
        {
            return Environment.Is64BitOperatingSystem ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }
    }
}
