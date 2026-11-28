using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Orleans.TestingHost.Utils
{
    /// <summary>
    /// 旧版 Windows Azure Storage Emulator 的包装器。
    /// </summary>
    /// <remarks>
    /// 此辅助工具仅管理下方列出的旧版模拟器可执行文件；它
    /// 不会启动或停止 Azurite。请使用
    /// <see href="https://learn.microsoft.com/azure/storage/common/storage-use-azurite">Azurite 进行本地 Azure Storage 开发</see>，
    /// 以获得受支持的本地模拟环境。
    /// </remarks>
    public static class StorageEmulator
    {
        /// <summary>
        /// 存储模拟器的进程名称。枚举运行中进程名称的一种方法是
        /// Get-Process | Format-Table Id, ProcessName -autosize。如果运行多个存储模拟器
        /// 进程，它们将命名为 WASTOR~1、WASTOR~2、……、WASTOR~n。
        /// </summary>
        private static readonly string[] storageEmulatorProcessNames = new[]
        {
            "AzureStorageEmulator", // 最新的。
            "Windows Azure Storage Emulator Service", // >= 2.7
            "WAStorageEmulator", // < 2.7
        };

        // 文件名称与进程名称并不相同。
        private static readonly string[] storageEmulatorFilenames = new[]
        {
            "AzureStorageEmulator.exe", // >= 2.7
            "WAStorageEmulator.exe", // < 2.7
        };

        /// <summary>
        /// 存储模拟器是否已启动。
        /// </summary>
        /// <returns><see langword="true" /> 表示此实例已启动；否则为 <see langword="false" />。</returns>
        public static bool IsStarted()
        {
            return GetStorageEmulatorProcess();
        }


        /// <summary>
        /// 检查存储模拟器是否存在，即是否已安装。
        /// </summary>
        /// <value><see langword="true" /> 表示存储模拟器存在；否则为 <see langword="false" />。</value>
        public static bool Exists
        {
            get
            {
                return GetStorageEmulatorPath() != null;
            }
        }

        /// <summary>
        /// Storage Emulator 帮助。
        /// </summary>
        /// <returns>存储模拟器帮助。</returns>
        public static string Help()
        {
            if (!IsStarted()) return "Error happened. Has StorageEmulator.Start() been called?";

            try
            {
                // 此进程句柄立即返回。
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
        /// 尝试启动存储模拟器。
        /// </summary>
        /// <returns><see langword="true"/> 表示进程成功启动；否则为 <see langword="false"/>。</returns>
        public static bool TryStart()
        {
            if (!StorageEmulator.Exists)
                return false;

            return Start();
        }

        /// <summary>
        /// 如果存储模拟器尚未启动，则启动它。
        /// </summary>
        /// <returns><see langword="true"/> 表示进程成功启动；否则为 <see langword="false"/>。</returns>
        public static bool Start()
        {
            if (IsStarted()) return true;

            try
            {
                // 此进程句柄立即返回。
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
        /// 如果存储模拟器已启动，则停止它。
        /// </summary>
        /// <returns><see langword="true"/> 表示进程成功停止或已经停止；否则为 <see langword="false"/>。</returns>
        public static bool Stop()
        {
            if (!IsStarted()) return false;

            try
            {
                // 此进程句柄立即返回。
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
        /// 创建新的 <see cref="ProcessStartInfo">ProcessStartInfo</see>，用于作为
        /// 此类中其他操作的参数。
        /// </summary>
        /// <param name="arguments">参数。</param>
        /// <returns>具有给定参数的新 <see cref="ProcessStartInfo"/>。</returns>
        private static ProcessStartInfo CreateProcessArguments(string arguments)
        {
#pragma warning disable CA1416 // 验证平台兼容性。
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
        /// 从系统查询存储模拟器进程。
        /// </summary>
        /// <returns><see langword="true" /> 表示找到了存储模拟器进程；否则为 <see langword="false" />。</returns>
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
        /// 返回存储模拟器可执行文件的完整路径，包括可执行文件名称及扩展名。
        /// </summary>
        /// <returns>存储模拟器可执行文件的完整路径；未找到时为 null。</returns>
        private static string? GetStorageEmulatorPath()
        {
            // 尝试获取最新的已知模拟器路径。如果不存在，则尝试较旧的路径。
            string exeBasePath = Path.Combine(GetProgramFilesBasePath(), @"Microsoft SDKs\Azure\Storage Emulator\");

            return storageEmulatorFilenames
                .Select(filename => Path.Combine(exeBasePath, filename))
                .FirstOrDefault(File.Exists);
        }


        /// <summary>
        /// 确定 Program Files 基础目录。
        /// </summary>
        /// <returns>Program Files 基础目录。</returns>
        private static string GetProgramFilesBasePath()
        {
            return Environment.Is64BitOperatingSystem ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }
    }
}
