using System;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;

namespace Orleans
{
    /// <summary>
    /// 验证序列化器配置。
    /// </summary>
    public class SerializerConfigurationValidator : IConfigurationValidator
    {
        private readonly ICodecProvider _codecProvider;
        private readonly TypeManifestOptions _options;
        private readonly bool _enabled;

        /// <summary>
        /// 初始化 <see cref="SerializerConfigurationValidator"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">
        /// 编解码器提供程序。
        /// </param>
        /// <param name="options">
        /// 类型清单选项。
        /// </param>
        /// <param name="serviceProvider">
        /// 服务提供程序。
        /// </param>
        public SerializerConfigurationValidator(ICodecProvider codecProvider, IOptions<TypeManifestOptions> options, IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(codecProvider);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            _codecProvider = codecProvider;
            _options = options.Value;

            var configEnabled = _options.EnableConfigurationAnalysis;
            if (configEnabled.HasValue)
            {
                _enabled = configEnabled.Value;
            }
            else
            {
                // 默认在开发环境中启用。
                var environment = serviceProvider.GetService<IHostEnvironment>();
                _enabled = environment is not null && environment.IsDevelopment();
            }
        }

        void IConfigurationValidator.ValidateConfiguration()
        {
            if (!_enabled)
            {
                return;
            }

            var complaints = SerializerConfigurationAnalyzer.AnalyzeSerializerAvailability(_codecProvider, _options);
            if (complaints.Count > 0)
            {
                var result = new StringBuilder();
                result.AppendLine("Found unserializable or uncopyable types which are being referenced in grain interface signatures:");
                foreach (var (type, complaint) in complaints)
                {
                    result.Append($"Type: {type}");
                    if (!complaint.HasSerializer)
                    {
                        result.Append(" has no serializer");
                        if (!complaint.HasCopier)
                        {
                            result.Append(" or copier");
                        }
                    }
                    else if (!complaint.HasCopier)
                    {
                        result.Append(" has no copier");
                    }

                    result.Append(" and was referenced by the following:");
                    foreach (var (declaringType, methodList) in complaint.Methods)
                    {
                        result.Append($"\n\t* {RuntimeTypeNameFormatter.Format(declaringType)} methods: {string.Join(", ", methodList)}");
                    }

                    result.AppendLine();
                }

                result.AppendLine("Ensure that all types which are used in grain interfaces have serializers available for them.");
                result.AppendLine("Applying the [GenerateSerializer] attribute to your type and adding [Id(x)] attributes to serializable properties and fields is the simplest way to accomplish this.");
                result.AppendLine("Alternatively, for types which are outside of your control, serializers may have to be manually crafted, potentially using surrogate types.");

                throw new OrleansConfigurationException(result.ToString());
            }
        }
    }
}
