using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orleans
{
    /// <summary>
    /// 客户端选项的 Loger 选项
    /// </summary>
    internal class ClientOptionsLogger : OptionsLogger, ILifecycleParticipant<IClusterClientLifecycle>
    {
        /// <summary>
        /// 尽快作出日志选择。
        /// </summary>
        private const int ClientOptionLoggerLifeCycleRing = int.MinValue;

        /// <summary>
        /// 初始化 <see cref="ClientOptionsLogger"/> 类的新实例。
        /// </summary>
        /// <param name="logger">
        /// 伐木工
        /// </param>
        /// <param name="services">
        /// 服务。
        /// </param>
        public ClientOptionsLogger(ILogger<ClientOptionsLogger> logger, IServiceProvider services)
            : base(logger, services)
        {
        }

        /// <inheritdoc />
        public void Participate(IClusterClientLifecycle lifecycle)
        {
            lifecycle.Subscribe<ClientOptionsLogger>(ClientOptionLoggerLifeCycleRing, this.OnStart);
        }

        /// <inheritdoc />
        public Task OnStart(CancellationToken token)
        {
            this.LogOptions();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 客户端和 silo 默认选项日志的基类。
    /// </summary>
    public abstract partial class OptionsLogger
    {
        private readonly ILogger logger;
        private readonly IServiceProvider services;

        /// <summary>
        /// 初始化 <see cref="OptionsLogger"/> 类的新实例。
        /// </summary>
        /// <param name="logger">
        /// 伐木工
        /// </param>
        /// <param name="services">
        /// 服务。
        /// </param>
        protected OptionsLogger(ILogger logger, IServiceProvider services)
        {
            this.logger = logger;
            this.services = services;
        }

        /// <summary>
        /// 登录所有已注册格式化器的选项
        /// </summary>
        public void LogOptions()
        {
            this.LogOptions(services.GetServices<IOptionFormatter>());
        }

        /// <summary>
        /// 使用一组格式化器的日志选项。
        /// </summary>
        /// <param name="formatters">收集选项格式化器。</param>
        public void LogOptions(IEnumerable<IOptionFormatter> formatters)
        {
            ArgumentNullException.ThrowIfNull(formatters);

            foreach (var optionFormatter in formatters.OrderBy(f => f.Name))
            {
                this.LogOption(optionFormatter);
            }
        }

        /// <summary>
        /// 使用要用事由记录一个选项。
        /// </summary>
        /// <param name="formatter">事项的选项。</param>
        public void LogOption(IOptionFormatter formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            try
            {
                var stringBuilder = new StringBuilder();
                foreach (var setting in formatter.Format())
                {
                    stringBuilder.AppendLine($"{setting}");
                }

                var options = stringBuilder.ToString();
                LogInformationOptions(logger, formatter.Name, options);
            }
            catch (Exception ex)
            {
                LogErrorOptions(logger, ex, formatter.Name);
                throw;
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            SkipEnabledCheck = true,
            Message = "Configuration {Name}:\n{Options}"
        )]
        private static partial void LogInformationOptions(ILogger logger, string name, string options);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "An error occurred while logging '{Name}' options."
        )]
        private static partial void LogErrorOptions(ILogger logger, Exception exception, string name);
    }
}
