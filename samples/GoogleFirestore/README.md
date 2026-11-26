<a id="google-cloud-firestore-providers"></a>

# Google云火炉供应商

本样本在一个过程中运行 Orleans silo 和客户端, 配置 Google Cloud Firshstore 用于:

- 集群成员和网关发现。
- 默认 grain 目录。
- 持久性 grain 状态。
- 持久提醒。

每个递增都设置一个固定的计数台,并创建或更新提醒。

<a id="run-with-the-firestore-emulator"></a>

## 使用火炉模拟器运行

安装 Java 21 和 Firebase CLI, 然后从仓库根开始 Firestore:

```powershell
npx firebase-tools@15.24.0 emulators:start --only firestore --project orleans-sample --config test/Extensions/Google/Emulators/firebase.json
```

在另一个终端中 :

```powershell
$env:GOOGLE_CLOUD_PROJECT = "orleans-sample"
$env:FIRESTORE_EMULATOR_HOST = "127.0.0.1:8080"
dotnet run --project samples/GoogleFirestore
```

重新运行该应用程序以观察持续反增量。

<a id="run-with-google-cloud"></a>

## 使用谷歌云运行

以本地模式创建火炉数据库, 使用应用程序默认证书认证, 并设定项目编号 :

```powershell
gcloud auth application-default login
$env:GOOGLE_CLOUD_PROJECT = "your-project-id"
Remove-Item Env:FIRESTORE_EMULATOR_HOST -ErrorAction Ignore
dotnet run --project samples/GoogleFirestore
```

经认证的身份需要火炉许可来创建、查询、更新和删除文档。样本在 `OrleansSample` 根收藏下写着。
