<a id="google-cloud-emulators"></a>

# Google Cloud 模拟器

Firebase 不发布官方 Emulator Suite 容器。请改用官方 Firebase CLI 包。

安装 Java 21 和 Firebase CLI 15.24.0 版本：

```shell
npm install --global firebase-tools@15.24.0
```

在此目录中启动已配置的模拟器：

```shell
firebase emulators:start --only firestore,pubsub,storage --project orleans-test
```
