<a id="microsoft-orleans-serialization-for-f"></a>

# Microsoft Orleans F# 序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans F# 序列化为 Microsoft Orleans 中的 F# 特有类型提供序列化支持。此包使可区分联合、记录及其他 F# 特有构造能够与 Orleans 序列化系统无缝集成。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Serialization.FSharp
```

<a id="example---configuring-f-serialization"></a>

## 示例 - 配置 F# 序列化
```fsharp
open Microsoft.Extensions.Hosting
open Orleans.Hosting

let builder = 
    Host.CreateApplicationBuilder(args)
        .UseOrleans(fun siloBuilder ->
            siloBuilder
                .UseLocalhostClustering()
                // F# serialization is automatically configured when the package is referenced
                |> ignore)

// Run the host
await builder.RunAsync()
```

<a id="example---using-f-types-with-orleans"></a>

## 示例 - 在 Orleans 中使用 F# 类型
```fsharp
// Define F# discriminated union and record types
[<Orleans.GenerateSerializer>]
type UserRole =
    | [<Id(0u)>] Admin
    | [<Id(1u)>] Moderator
    | [<Id(2u)>] User of level:int

[<Orleans.GenerateSerializer>]
type UserRecord = {
    [<Id(0u)>] Id: string
    [<Id(1u)>] Name: string
    [<Id(2u)>] Role: UserRole
    [<Id(3u)>] Tags: string list
}

// Define a grain interface
type IFSharpGrain =
    inherit Orleans.IGrainWithStringKey
    abstract member GetUser: unit -> Task<UserRecord>
    abstract member UpdateUser: UserRecord -> Task

// Grain implementation
type FSharpGrain() =
    inherit Orleans.Grain()
    let mutable userData = Unchecked.defaultof<UserRecord>

    interface IFSharpGrain with
        member this.GetUser() =
            Task.FromResult(userData)
            
        member this.UpdateUser(user) =
            userData <- user
            Task.CompletedTask
```

<a id="example---client-code-using-f-grain"></a>

## 示例 - 使用 F# Grain 的客户端代码
```fsharp
// Client-side code
let client = clientBuilder.Build()
let grain = client.GetGrain<IFSharpGrain>("user1")

// Create an F# record with discriminated union
let user = {
    Id = "user1"
    Name = "F# User"
    Role = UserRole.Admin
    Tags = ["functional"; "programming"]
}

// Call the grain with F# types
grain.UpdateUser(user) |> Async.AwaitTask |> ignore
let retrievedUser = grain.GetUser() |> Async.AwaitTask |> Async.RunSynchronously
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序列化](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [F# 文档](https://learn.microsoft.com/dotnet/fsharp/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)