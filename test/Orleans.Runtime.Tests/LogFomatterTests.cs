using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Xunit;

namespace Tester
{
    /// <summary>
    /// 定制选项格式和事项分辨率测试,包括对指定选项的支持和敏感数据的编辑。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("BVT")]
    public class LogFomatterTests
    {
        [Fact]
        public void CanResolveFormatter()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptions>(options => options.IntField = 1);
            services.ConfigureFormatter<TestOptions, TestOptionsFormatter>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptions>);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void LogOption_NullFormatter_ThrowsBeforeLogging()
        {
            var loggerFactory = new TestLoggerFactory();
            var logger = loggerFactory.CreateLogger(nameof(LogOption_NullFormatter_ThrowsBeforeLogging));
            using var services = new ServiceCollection().BuildServiceProvider();
            var target = new DirectOptionsLogger(logger, services);
            var before = loggerFactory.ToString();

            var exception = Assert.Throws<ArgumentNullException>(() => target.LogOption(null!));

            Assert.Equal("formatter", exception.ParamName);
            Assert.Equal(before, loggerFactory.ToString());
        }

        [Fact]
        public void LogOptions_NullFormatters_ThrowsBeforeLogging()
        {
            var loggerFactory = new TestLoggerFactory();
            var logger = loggerFactory.CreateLogger(nameof(LogOptions_NullFormatters_ThrowsBeforeLogging));
            using var services = new ServiceCollection().BuildServiceProvider();
            var target = new DirectOptionsLogger(logger, services);
            var before = loggerFactory.ToString();

            var exception = Assert.Throws<ArgumentNullException>(() => target.LogOptions(null!));

            Assert.Equal("formatters", exception.ParamName);
            Assert.Equal(before, loggerFactory.ToString());
        }

        [Fact]
        public void CanResolveGenericFormatter()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptions>(options => options.IntField = 1);
            services.ConfigureFormatter<TestOptions>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptions>);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void GenericFormatterRedact()
        {
            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptionsWithSecrets>(options =>
            {
                options.Data = "Hello";
                // SuppressMessage( "Microsoft.Security" , "CS002:SecretInNextLine" ,说明=`非秘密'
                options.Password = "v3ryS3cur3!!!";
                options.SomeConnectionString = "DefaultEndpointsProtocol=https;AccountName=someAccount;AccountKey=someKey;EndpointSuffix=core.windows.net";
            });
            services.ConfigureFormatter<TestOptionsWithSecrets>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptionsWithSecrets>);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Contains("Hello", actual.ToString());
            Assert.Contains("Password: REDACTED", actual.ToString());
            Assert.Contains("SomeConnectionString: DefaultEndpointsProtocol=https;AccountName=someAccount;AccountKey=<--SNIP-->", actual.ToString());
        }

        [Fact]
        public void GenericFormatterWithListAndDictionary()
        {
            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptionsWithListAndDictionary>(options =>
            {
                options.SomeDictionary.Add("Account1", "Key1");
                options.SomeDictionary.Add("Account2", "Key2");
                options.SomeDictionary.Add("Account3", "Key3");
                options.SomeList.Add(10);
                options.SomeList.Add(11);
            });
            services.ConfigureFormatter<TestOptionsWithListAndDictionary>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptionsWithListAndDictionary>);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            var txt = actual.ToString();
            Assert.Contains("SomeList.0: 10", actual.ToString());
            Assert.Contains("SomeList.1: 11", actual.ToString());
            Assert.Contains("SomeDictionary.Account1: Key1", actual.ToString());
            Assert.Contains("SomeDictionary.Account2: Key2", actual.ToString());
            Assert.Contains("SomeDictionary.Account3: Key3", actual.ToString());
            Assert.Contains("NullList:", actual.ToString());
            Assert.Contains("NullDictionary:", actual.ToString());
        }

        [Fact]
        public void FormatterConfiguredTwiceDoesNotLeadToDuplicatedFormatter()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptions>(options => options.IntField = 1);
            services.ConfigureFormatter<TestOptions, TestOptionsFormatter2>();
            //和
            //DI最终将产生两个同一选项的要事项
            services.ConfigureFormatter<TestOptions, TestOptionsFormatter>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            // 只有一个选项格式化器, 它指向右边的
            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptions>);
            // 当解决单类类型特定事项时, 我们得到正确的
            var logFormatter = servicesProvider.GetService<IOptionFormatter<TestOptions>>();
            Assert.True(logFormatter is TestOptionsFormatter);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void CustomFormatterOverridesDefaultFormatter_PreRegistration()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptions>(options => options.IntField = 1);
            // 预登记前登记覆盖
            services.ConfigureFormatter<TestOptions, TestOptionsFormatter>();
            // 默认缺缺缺
            services.TryConfigureFormatter<TestOptions, TestOptionsFormatter2>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptions>);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void CustomFormatterOverridesDefaultFormatter_PostRegistration()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.Configure<TestOptions>(options => options.IntField = 1);
            // 默认缺缺缺
            services.TryConfigureFormatter<TestOptions, TestOptionsFormatter2>();
            // 邮件登记超
            services.ConfigureFormatter<TestOptions, TestOptionsFormatter>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Single(logFormatters);
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.First() is IOptionFormatter<TestOptions>);
            // 当解决单类类型特定事项时, 我们得到正确的
            var logFormatter = servicesProvider.GetService<IOptionFormatter<TestOptions>>();
            Assert.True(logFormatter is TestOptionsFormatter);
            // 确保伐木产出如预期的那样
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void NamedFormatterGoldenPath()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildNamedOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            services.ConfigureFormatterResolver<TestOptions, TestOptionsFormatter.Resolver>();
            Enumerable
                .Range(1, 3)
                .ToList()
                .ForEach(i =>
                {
                    string name = i.ToString();
                    services.Configure<TestOptions>(name, (options => options.IntField = i));
                    services.ConfigureNamedOptionForLogging<TestOptions>(name);
                });
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Equal(3, logFormatters.Count());
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(1) is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(2) is TestOptionsFormatter);
            var logFormatter = servicesProvider.GetService<IOptionFormatterResolver<TestOptions>>();
            Assert.True(logFormatter is TestOptionsFormatter.Resolver);
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void NamedGenericFormatterGoldenPath()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildNamedOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.AddSingleton(typeof(IOptionFormatterResolver<>), typeof(DefaultOptionsFormatterResolver<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            Enumerable
                .Range(1, 3)
                .ToList()
                .ForEach(i =>
                {
                    string name = i.ToString();
                    services.Configure<TestOptions>(name, (options => options.IntField = i));
                    services.ConfigureNamedOptionForLogging<TestOptions>(name);
                });
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Equal(3, logFormatters.Count());
            var logFormatter = servicesProvider.GetService<IOptionFormatterResolver<TestOptions>>();
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void CustomFormatterResolverOverridesDefaultFormatter_PreRegistration()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildNamedOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            // 预登记前登记覆盖
            services.ConfigureFormatterResolver<TestOptions, TestOptionsFormatter.Resolver>();
            // 配置选项
            Enumerable
                .Range(1, 3)
                .ToList()
                .ForEach(i =>
                {
                    string name = i.ToString();
                    services.Configure<TestOptions>(name, (options => options.IntField = i));
                    services.ConfigureNamedOptionForLogging<TestOptions>(name);
                });
            // 默认缺缺缺
            services.TryConfigureFormatterResolver<TestOptions, TestOptionsFormatter2.Resolver>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Equal(3, logFormatters.Count());
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(1) is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(2) is TestOptionsFormatter);
            var logFormatter = servicesProvider.GetService<IOptionFormatterResolver<TestOptions>>();
            Assert.True(logFormatter is TestOptionsFormatter.Resolver);
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public void CustomFormatterResolverOverridesDefaultFormatter_PostRegistration()
        {
            // 预期成果产出
            TestLoggerFactory expected = BuildNamedOptionsExpectedResult();

            // 实际实际产出
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton<TestLoggerFactory>();
            services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<TestLoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
            services.AddSingleton<OptionsLogger, TestOptionsLogger>();
            // 默认缺省
            services.TryConfigureFormatterResolver<TestOptions, TestOptionsFormatter2.Resolver>();
            // 配置选项
            Enumerable
                .Range(1, 3)
                .ToList()
                .ForEach(i =>
                {
                    string name = i.ToString();
                    services.Configure<TestOptions>(name, (options => options.IntField = i));
                    services.ConfigureNamedOptionForLogging<TestOptions>(name);
                });
            // 邮件登记超
            services.ConfigureFormatterResolver<TestOptions, TestOptionsFormatter.Resolver>();
            var servicesProvider = services.BuildServiceProvider();
            servicesProvider.GetRequiredService<OptionsLogger>().LogOptions();

            var logFormatters = servicesProvider.GetServices<IOptionFormatter>();
            Assert.Equal(3, logFormatters.Count());
            Assert.True(logFormatters.First() is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(1) is TestOptionsFormatter);
            Assert.True(logFormatters.ElementAt(2) is TestOptionsFormatter);
            var logFormatter = servicesProvider.GetService<IOptionFormatterResolver<TestOptions>>();
            Assert.True(logFormatter is TestOptionsFormatter.Resolver);
            var actual = servicesProvider.GetRequiredService<TestLoggerFactory>();
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        private static TestLoggerFactory BuildOptionsExpectedResult()
        {
            var services = new ServiceCollection();
            var testOptions = new TestOptions
            {
                IntField = 1
            };
            var expected = new TestLoggerFactory();
            var formatter = new TestOptionsFormatter(Options.Create(testOptions));
            var optionsLogger = new TestOptionsLogger(expected.CreateLogger<TestOptionsLogger>(), services.BuildServiceProvider());
            optionsLogger.LogOption(formatter);
            return expected;
        }

        private static TestLoggerFactory BuildNamedOptionsExpectedResult()
        {
            var services = new ServiceCollection();
            IOptionFormatter[] formatters = Enumerable
                .Range(1, 3)
                .Select(i => TestOptionsFormatter.CreateNamed(i.ToString(), Options.Create(new TestOptions { IntField = i })))
                .ToArray<IOptionFormatter>();
            var expected = new TestLoggerFactory();
            var optionsLogger = new TestOptionsLogger(expected.CreateLogger<TestOptionsLogger>(), services.BuildServiceProvider());
            optionsLogger.LogOptions(formatters);
            return expected;
        }

        private class TestOptionsWithSecrets
        {
            [Redact()]
            public string Password { get; set; } = null!;

            public string Data { get; set; } = null!;

            [RedactConnectionString()]
            public string SomeConnectionString { get; set; } = null!;
        }

        private class TestOptions
        {
            public int IntField { get; set; } = 0;
        }

        private class TestOptionsWithListAndDictionary
        {
            public List<int> SomeList { get; set; } = new List<int>();

            public Dictionary<string, string> SomeDictionary { get; set; } = new Dictionary<string, string>();

            public List<int>? NullList { get; set; } = null;

            public Dictionary<string, string>? NullDictionary { get; set; } = null;
        }

        private class TestOptionsFormatter2 : IOptionFormatter<TestOptions>
        {
            public string Name { get; private set; }

            private readonly TestOptions options;
            public TestOptionsFormatter2(IOptions<TestOptions> options)
            {
                this.options = options.Value;
                this.Name = nameof(TestOptions);
            }

            public static TestOptionsFormatter2 CreateNamed(string name, IOptions<TestOptions> options)
            {
                var result = new TestOptionsFormatter2(options);
                // 不同格式
                result.Name = $"{nameof(TestOptions)}+{name}";
                return result;
            }

            public IEnumerable<string> Format()
            {
                return new List<string>()
                {
                    // 不同格式
                    OptionFormattingUtilities.Format(nameof(options.IntField), options.IntField, "{0}=>{1}")
                };
            }

            public class Resolver : IOptionFormatterResolver<TestOptions>
            {
                private readonly IOptionsMonitor<TestOptions> optionsMonitor;
                public Resolver(IOptionsMonitor<TestOptions> optionsMonitor)
                {
                    this.optionsMonitor = optionsMonitor;
                }

                public IOptionFormatter<TestOptions> Resolve(string name)
                {
                    return TestOptionsFormatter2.CreateNamed(name, Options.Create(this.optionsMonitor.Get(name)));
                }
            }
        }

        private class TestOptionsFormatter : IOptionFormatter<TestOptions>
        {
            public string Name { get; private set; }

            private readonly TestOptions options;
            public TestOptionsFormatter(IOptions<TestOptions> options)
            {
                this.options = options.Value;
                this.Name = typeof(TestOptions).FullName!;
            }

            public static TestOptionsFormatter CreateNamed(string name, IOptions<TestOptions> options)
            {
                var result = new TestOptionsFormatter(options);
                result.Name = $"{typeof(TestOptions).FullName}-{name}";
                return result;
            }

            public IEnumerable<string> Format()
            {
                return new List<string>()
                {
                    OptionFormattingUtilities.Format(nameof(options.IntField), options.IntField)
                };
            }

            public class Resolver : IOptionFormatterResolver<TestOptions>
            {
                private readonly IOptionsMonitor<TestOptions> optionsMonitor;
                public Resolver(IOptionsMonitor<TestOptions> optionsMonitor)
                {
                    this.optionsMonitor = optionsMonitor;
                }

                public IOptionFormatter<TestOptions> Resolve(string name)
                {
                    return TestOptionsFormatter.CreateNamed(name, Options.Create(this.optionsMonitor.Get(name)));
                }
            }
        }

        private class TestLoggerFactory : ILoggerFactory
        {
            private readonly ConcurrentDictionary<string, Logger> loggers = new ConcurrentDictionary<string, Logger>();

            public void AddProvider(ILoggerProvider provider)
            {
                throw new NotImplementedException();
            }

            public ILogger CreateLogger(string categoryName)
            {
                return this.loggers.GetOrAdd(categoryName, new Logger());
            }

            public void Dispose()
            {
            }

            public override string ToString()
            {
                return string.Join(":", this.loggers.Select(kvp => $"{kvp.Key} =\n{kvp.Value.ToString()}\n"));
            }

            private class Logger : ILogger
            {
                private readonly List<string> entries = new List<string>();

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                {
                    throw new NotImplementedException();
                }

                public override string ToString()
                {
                    return string.Join(";", this.entries);
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    entries.Add(formatter(state, exception));
                }
            }
        }

        private class TestLogger<T> : ILogger<T>
        {
            private readonly ILogger<T> logger;

            public TestLogger(ILoggerFactory loggerFactory)
            {
                this.logger = loggerFactory.CreateLogger<T>();
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return logger.BeginScope<TState>(state);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logger.IsEnabled(logLevel);
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                logger.Log<TState>(logLevel, eventId, state, exception, formatter);
            }
        }


        private class TestOptionsLogger : OptionsLogger
        {
            public TestOptionsLogger(ILogger<TestOptionsLogger> logger, IServiceProvider services)
                : base(logger, services)
            {
            }
        }

        private sealed class DirectOptionsLogger : OptionsLogger
        {
            public DirectOptionsLogger(ILogger logger, IServiceProvider services)
                : base(logger, services)
            {
            }
        }
    }
}
