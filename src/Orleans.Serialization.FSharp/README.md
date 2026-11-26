<a id="microsoft-orleans-serialization-for-f"></a>

# Microsoft Orleans F# 的序列化

<a id="introduction"></a>

## 简介
Microsoft Orleans F#的序列化为F#具体类型提供了系列化支持 Microsoft Orleans。这个软件包能够将F#类型(如受歧视的工会、记录和其他F#具体结构)与 Orleans 序列系统。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Serialization.FSharp
```

<a id="example---configuring-f-serialization"></a>

## 示例 - F# 串级配置
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

## 示例 - 用 OrleansF# 类型
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

## 示例 - 使用 F# Grain的客户代码
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 序号](https://dotnet.github.io/orleans/docs/host/configuration-guide/serialization/)
- [F# 文件](https://learn.microsoft.com/dotnet/fsharp/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证