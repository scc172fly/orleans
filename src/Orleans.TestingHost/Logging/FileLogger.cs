using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.TestingHost.Logging
{
    /// <summary>
    /// 所有 <see cref="FileLogger"/> 共享的日志输出目标。
    /// </summary>
    public class FileLoggingOutput : IDisposable
    {
        private static readonly ConcurrentDictionary<FileLoggingOutput, object?> Instances = new();
        private readonly TimeSpan flushInterval = Debugger.IsAttached ? TimeSpan.FromMilliseconds(10) : TimeSpan.FromSeconds(1);
        private readonly Lock lockObj = new();
        private readonly string logFileName;
        private DateTime lastFlush = DateTime.UtcNow;
        private StreamWriter? logOutput;

        /// <summary>
        /// 初始化 <see cref="FileLoggingOutput"/> 类的静态成员。
        /// </summary>
        static FileLoggingOutput()
        {
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            static void CurrentDomain_ProcessExit(object? sender, EventArgs args)
            {
                foreach (var instance in Instances)
                {
                    instance.Key.Dispose();
                }
            }
        }

        /// <summary>
        /// 初始化 <see cref="FileLoggingOutput"/> 类的新实例。
        /// </summary>
        /// <param name="fileName">日志文件名称。</param>
        public FileLoggingOutput(string fileName)
        {
            this.logFileName = fileName;
            logOutput = new StreamWriter(File.Open(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
            Instances[this] = null;
        }

        /// <summary>
        /// 记录一条消息。
        /// </summary>
        /// <typeparam name="TState"><paramref name="state"/> 的类型。</typeparam>
        /// <param name="logLevel">日志级别。</param>
        /// <param name="eventId">事件标识符。</param>
        /// <param name="state">状态。</param>
        /// <param name="exception">异常。</param>
        /// <param name="formatter">格式化器。</param>
        /// <param name="category">类别。</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter, string category)
        {
            var logMessage = FormatMessage(DateTime.UtcNow, logLevel, category, formatter(state, exception), exception, eventId);
            lock (this.lockObj)
            {
                if (this.logOutput == null) return;

                this.logOutput.WriteLine(logMessage);
                var now = DateTime.UtcNow;
                if (now - this.lastFlush > flushInterval)
                {
                    this.lastFlush = now;
                    this.logOutput.Flush();
                }
            }
        }

        private static string FormatMessage(
            DateTime timestamp,
            LogLevel logLevel,
            string caller,
            string message,
            Exception? exception,
            EventId errorCode)
        {
            if (logLevel == LogLevel.Error)
                message = "!!!!!!!!!! " + message;

            var exc = LogFormatter.PrintException(exception);
            var msg = string.Format("[{0} {1}\t{2}\t{3}\t{4}]\t{5}\t{6}",
                LogFormatter.PrintDate(timestamp),           //0
                Environment.CurrentManagedThreadId,   //1
                logLevel.ToString(),    //2
                errorCode.ToString(),                              //3
                caller,                                 //4
                message,                                //5
                exc);      //6

            return msg;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                lock (this.lockObj)
                {
                    if (this.logOutput is StreamWriter output)
                    {
                        this.logOutput = null;
                        _ = Instances.TryRemove(this, out _);

                        // 释放输出对象，这会刷新所有缓冲区。
                        output.Dispose();
                    }
                }
            }
            catch (Exception exc)
            {
                var msg = string.Format("Ignoring error closing log file {0} - {1}", this.logFileName,
                    LogFormatter.PrintException(exc));
                Console.WriteLine(msg);
            }
        }
    }

    /// <summary>
    /// 文件日志记录器，将消息记录到文件。
    /// </summary>
    public class FileLogger : ILogger
    {
        private readonly FileLoggingOutput output;
        private readonly string category;

        /// <summary>
        /// 初始化 <see cref="FileLogger"/> 类的新实例。
        /// </summary>
        /// <param name="output">输出日志记录器。</param>
        /// <param name="category">类别。</param>
        public FileLogger(FileLoggingOutput output, string category)
        {
            this.category = category;
            this.output = output;
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.output.Log(logLevel, eventId, state, exception, formatter, this.category);

        }

        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            private NullScope()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
