namespace HelloWorld.Interfaces;

/// <summary>
/// Orleans grain 通讯接口
/// </summary>
public interface IHelloGrain : Orleans.IGrainWithIntegerKey
{
    Task<string> SayHello(string greeting);
}
