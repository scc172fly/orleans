using System.Diagnostics;
using System.Reflection;

namespace Orleans.Runtime
{
    internal static class RuntimeVersion
    {
        /// <summary>
        /// Orleans 运行时间及其构建配置的信息版本,
        /// 当信息版本元数据无法使用时, 或组装版本 。
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
        /// 返回一个数值,以显示所提供的 <paramref name="assembly"/> 是否在调试模式下建。
        /// </summary>
        /// <param name="assembly">
        /// 装配检查。
        /// </param>
        /// <returns>
        /// 表示所提供的装配是否以调试模式构建的值。
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
