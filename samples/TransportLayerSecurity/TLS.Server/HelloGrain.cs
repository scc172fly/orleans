using HelloWorld.Interfaces;
using Microsoft.Extensions.Logging;

namespace HelloWorld.Grains;

/// <summary>
/// Orleans grain 执行类别 HelloGrain。
/// </summary>
public sealed class HelloGrain : Grain, IHelloGrain
{
    private readonly ILogger _logger;

    public HelloGrain(ILogger<HelloGrain> logger)
    {
        _logger = logger;
    }

    Task<string> IHelloGrain.SayHello(string greeting)
    {
        _logger.LogInformation("SayHello message received: greeting = '{Greeting}'", greeting);
        return Task.FromResult($"You said: '{greeting}', I say: Hello!");
    }
}
