using System.Diagnostics;
using System.Reflection;

namespace Orleans.Runtime
{
    internal static class RuntimeVersion
    {
        /// <summary>
        /// Orleans 运行时的信息版本及其构建配置，
        /// 如果信息版本元数据不可用，则为程序集版本。
        /// </summary>
        public static string Current => GetVersion(typeof(RuntimeVersion).Assembly);

        internal static string GetVersion(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            var apiVersion = assembly.GetName().Version!.ToString();
            var productVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(productVersion)
                ? apiVersion
                : productVersion + (IsAssemblyDebugBuild(assembly) ? " (Debug)." : " (Release).");
        }

        /// <summary>
        /// 返回指示所提供 <paramref name="assembly"/> 是否以调试模式构建的值。
        /// </summary>
        /// <param name="assembly">
        /// 要检查的程序集。
        /// </param>
        /// <returns>
        /// 指示所提供程序集是否以调试模式构建的值。
        /// </returns>
        internal static bool IsAssemblyDebugBuild(Assembly assembly)
        {
            foreach (var debuggableAttribute in assembly.GetCustomAttributes<DebuggableAttribute>())
            {
                return debuggableAttribute.IsJITTrackingEnabled;
            }
            return false;
        }
    }
}
