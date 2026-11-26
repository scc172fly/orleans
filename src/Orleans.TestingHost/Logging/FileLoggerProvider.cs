using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orleans.TestingHost.Logging
{
    /// <summary>
    /// 将输出到日志文件的<see cref="ILoggerProvider"/> 。
    /// </summary>
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggingOutput output;

        /// <summary>
        /// 初始化 <see cref="FileLoggerProvider"/> 类的新实例。
        /// </summary>
        /// <param name="filePath">日志文件路径。</param>
        public FileLoggerProvider(string filePath)
        {
            this.output = new FileLoggingOutput(filePath);
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this.output, categoryName);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.output.Dispose();
        }
    }

    /// <summary>
    /// 配置扩展配置方法 ILoggingBuilder 与 FileLoggerProvider
    /// </summary>
    public static class FileLoggerProviderExtensions
    {
        /// <summary>
        /// 添加与伐木服务集装箱所属供应商的文件记录。
        /// </summary>
        /// <param name="builder">伐木工</param>
        /// <param name="filePathName">日志文件路径</param>
        /// <returns>伐木工</returns>
        public static ILoggingBuilder AddFile(
            this ILoggingBuilder builder,
            string filePathName)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(filePathName));
            return builder;
        }
    }
}
