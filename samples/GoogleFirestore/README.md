<a id="google-cloud-firestore-providers"></a>

# Google Cloud Firestore 提供程序

本示例在一个进程内运行 Orleans Silo 和客户端，并配置 Google Cloud Firestore 用于：

- 集群成员资格和网关发现。
- 默认 Grain 目录。
- Grain 持久化状态。
- 持久化提醒器。

每次运行都会递增一个持久化计数器，并创建或更新提醒器。

<a id="run-with-the-firestore-emulator"></a>

## 使用 Firestore 模拟器运行

安装 Java 21 和 Firebase CLI，然后从仓库根目录启动 Firestore：

```powershell
npx firebase-tools@15.24.0 emulators:start --only firestore --project orleans-sample --config test/Extensions/Google/Emulators/firebase.json
```

在另一个终端中：

```powershell
$env:GOOGLE_CLOUD_PROJECT = "orleans-sample"
$env:FIRESTORE_EMULATOR_HOST = "127.0.0.1:8080"
dotnet run --project samples/GoogleFirestore
```

再次运行应用，观察持久化计数器递增。

<a id="run-with-google-cloud"></a>

## 使用 Google Cloud 运行

创建 Native 模式的 Firestore 数据库，使用应用默认凭据进行身份验证，并设置项目 ID：

```powershell
gcloud auth application-default login
$env:GOOGLE_CLOUD_PROJECT = "your-project-id"
Remove-Item Env:FIRESTORE_EMULATOR_HOST -ErrorAction Ignore
dotnet run --project samples/GoogleFirestore
```

通过身份验证的标识需要具有创建、查询、更新及删除文档的 Firestore 权限。示例在 `OrleansSample` 根集合下执行写入。
