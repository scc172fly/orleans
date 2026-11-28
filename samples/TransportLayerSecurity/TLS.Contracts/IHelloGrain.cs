namespace HelloWorld.Interfaces;

/// <summary>
/// Orleans Grain 通信接口 IHello。
/// </summary>
public interface IHelloGrain : Orleans.IGrainWithIntegerKey
{
    Task<string> SayHello(string greeting);
}
