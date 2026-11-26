<a id="google-cloud-emulators"></a>

# Google 云模拟器

消防基地不公布正式模拟器套装容器。 使用官方消防基地 CLI 软件包代替。

安装 Java 21 和 Firebase CLI 版本 15.2.40.:

```shell
npm install --global firebase-tools@15.24.0
```

从此目录开始配置的模拟器 :

```shell
firebase emulators:start --only firestore,pubsub,storage --project orleans-test
```
