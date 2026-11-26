---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Voting sample app on Kubernetes"
urlFragment: "orleans-voting-sample-app-on-kubernetes"
description: "Orleans 样本,显示在 Kubernetes上有一个投票应用。"
---

<a id="orleans-voting-sample-app-on-kubernetes"></a>

# Orleans 有关 Kubernetes的投票样本应用

![应用程序的截图](./screenshot.png)

这是 [Orleans](https://github.com/dotnet/orleans) 显示用于 Kubernetes。应用程序是一个简单化的网络应用程序,用于投票决定一套自定义选项。 [.NET 通用主机](https://docs.microsoft.com/dotnet/core/extensions/generic-host) 要共同作为东道主 [ASP.NET 核心](https://docs.microsoft.com/aspnet/core) 和 Orleans 以及 [Orleans 机表](https://github.com/OrleansContrib/OrleansDashboard) 在同一进程中共同合作。

![奥尔良仪表板的截图](./dashboard.png)

由HTTP处理的请求 ASP.NET 核心监查控制控制器 Orleans grains.

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. 打开解压后的示例目录，再打开其中的 C# 项目文件（`.csproj`）。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接运行。
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

应用程序可执行 :

```powershell
dotnet run -c Release -- --environment Development --urls http://localhost:5000
```

一旦程序启动,打开浏览器到 <http://localhost:5000> 与应用程序一起玩。 Orleans Dashboard将在 <http://localhost:8888.> 提供。应用程序也可以被部署到 Kubernetes。将此样本部署到 Kubernetes 的关键文件是 [`deployment.yaml`](./deployment.yaml),它描述所需要的 Kubernetes 资源。在应用该应用程序之前,您需要提供以下资源:

* 一个资源组
* Azure 集装箱登记簿(ACR)集装箱登记册
* Azure Kubernetes 服务(AKS)群集
* A服务主任,允许AKS进入ACR

[`provision.ps1`](./provision.ps1) 脚本试图将这些步骤自动化, 在脚本顶部定义一些必需的名字 。
最好在 **PowerShell** 终端逐个执行以下步骤,因为脚本不执行错误处理。

```azurecli
# Choose some resource names. Note that some of these are globally unique across all of Azure, so you will need to change these values.
$resourceGroup = "votingapp"
$location = "westus"
$clusterName = "votingapp"
$containerRegistry = "votingappacr"

az login

# Create a resource group
az group create --name $resourceGroup --location $location

# Create an AKS cluster. This can take a few minutes
az aks create --resource-group $resourceGroup --name $clusterName --node-count 3

# If you haven't already, install the Kubernetes CLI
az aks install-cli

# Authenticate the Kubernetes CLI
az aks get-credentials --resource-group $resourceGroup --name $clusterName

# Create an Azure Container Registry account and login to it
az acr create --name $containerRegistry --resource-group $resourceGroup --sku Standard

# Create a service principal for the container registry and register it with Kubernetes as an image pulling secret
$acrId = $(az acr show --name $containerRegistry --query id --output tsv)
$acrServicePrincipalName = "$($containerRegistry)-aks-service-principal"
$acrSpPw = $(az ad sp create-for-rbac --name http://$acrServicePrincipalName --scopes $acrId --role acrpull --query password --output tsv)
$acrSpAppId = $(az ad sp show --id http://$acrServicePrincipalName --query appId --output tsv)
$acrLoginServer = $(az acr show --name $containerRegistry --resource-group $resourceGroup --query loginServer).Trim('"')
kubectl create secret docker-registry $containerRegistry --namespace default --docker-server=$acrLoginServer --docker-username=$acrSpAppId --docker-password=$acrSpPw
```

有了这些资源,我们可以确定我们的申请并部署。
创建一个名为 `deployment.yaml` 的文件,文件内容如下:根据资源提供时选择的资源名称,必要时根据需要进行修改。
查找 `# REPLACEME` 注释并替换这些值。
下面我们解释文件的结构

``` yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
      - name: redis
        image: mcr.microsoft.com/oss/bitnami/redis:6.0.8
        env:
        - name: ALLOW_EMPTY_PASSWORD
          value: "yes"
        resources:
          requests:
            cpu: 100m
            memory: 128Mi
          limits:
            cpu: 250m
            memory: 256Mi
        ports:
        - containerPort: 6379
          name: redis
---
apiVersion: v1
kind: Service
metadata:
  name: redis
spec:
  ports:
  - port: 6379
  selector:
    app: redis
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: votingapp
  labels:
    app: votingapp
spec:
  selector:
    matchLabels:
      app: votingapp
  replicas: 3
  template:
    metadata:
      labels:
        app: votingapp

        # The serviceId label is used to identify the service to Orleans
        orleans/serviceId: votingapp

        # The clusterId label is used to identify an instance of a cluster to Orleans.
        # Typically, this will be the same value as serviceId or any fixed value.
        # In cases where you are not using rolling deployments (for example, blue/green deployments),
        # this value can allow for distinct clusters which do not communicate directly with each others,
        # but which still share the same storage and other resources.
        orleans/clusterId: votingapp
    spec:
      containers:
        - name: main
          image: votingappacr.azurecr.io/votingapp # REPLACEME
          imagePullPolicy: Always
          ports:
          - containerPort: 80
          - containerPort: 443
          - containerPort: 11111
          - containerPort: 8888
          env:
          # Configure settings to let Orleans know which cluster it belongs to and which pod it is running in
          - name: ORLEANS_SERVICE_ID
            valueFrom:
              fieldRef:
                fieldPath: metadata.labels['orleans/serviceId']
          - name: ORLEANS_CLUSTER_ID
            valueFrom:
              fieldRef:
                fieldPath: metadata.labels['orleans/clusterId']
          - name: POD_NAMESPACE
            valueFrom:
              fieldRef:
                fieldPath: metadata.namespace
          - name: POD_NAME
            valueFrom:
              fieldRef:
                fieldPath: metadata.name
          - name: POD_IP
            valueFrom:
              fieldRef:
                fieldPath: status.podIP
          - name: DOTNET_SHUTDOWNTIMEOUTSECONDS
            value: "120"
          - name: REDIS
            value: "redis" # The name of the redis service
      terminationGracePeriodSeconds: 180
      imagePullSecrets:
        - name: votingappacr # REPLACEME
  minReadySeconds: 60
  strategy:
    rollingUpdate:
      maxUnavailable: 0
      maxSurge: 1

---
# In order to be able to access the service from outside the cluster, we will need to add a Service object
apiVersion: v1
kind: Service
metadata:
  name: votingapp
spec:
  type: LoadBalancer
  ports:
  - name: http
    port: 80
  - name: https
    port: 433
  - name: orleans-dashboard
    port: 8888
  selector:
    app: votingapp

# For RBAC-enabled clusters, the Kubernetes service account for the pods may also need to be granted the required access:
---
kind: Role
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  name: pod-reader
rules:
- apiGroups: [ "" ]
  resources: ["pods"]
  verbs: ["get", "watch", "list"]
---
kind: RoleBinding
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  name: pod-reader-binding
subjects:
- kind: ServiceAccount
  name: default
  apiGroup: ''
roleRef:
  kind: Role
  name: pod-reader
  apiGroup: ''
```

档案是大的,一开始可能很吓人 但基本结构是创造两个 *部署部署* 资源:1个用于 Redis 每一个部署都有相应的 *服务处* 用于路由交通。此外,由于此样本使用 `Microsoft.Orleans.Kubernetes.Hosting` 选项卡软件包,该软件包查询 Kubernetes API,您需要提供 *作用角色角色角色角色角色角色角色角色角色角色角色角色角色角色角色角色角色角色* 相应 *RoleBinding* 如果您的集组已启用 RBAC 。 `deployment.yaml` 由以下 `---`.

创建了 `deployment.yaml` 文件, 现在我们需要建立并部署应用程序。 请使用 `docker` 将源复制到一个新的构建容器中, 并构建程序, 然后将结果复制到新的层 。

执行以下程序以构建容器图像,将其推至 Azure 集装箱登记册,并将 `deployment.yaml` 文件部署到 Kubernetes。
请注意,您需要替换变量名称,就像您在提供资源时所做的那样。

```azurepowershell
$resourceGroup = "votingapp"
$containerRegistry = "votingappacr"

$acrLoginServer = $(az acr show --name $containerRegistry --resource-group $resourceGroup --query loginServer).Trim('"')
az acr login --name $containerRegistry

docker build ../.. -f Dockerfile -t $acrLoginServer/votingapp &&
docker push $acrLoginServer/votingapp &&
kubectl apply -f ./deployment.yaml &&
kubectl rollout restart deployment/votingapp
```

上次命令执行后重新启动部署。
只有当您使用上述脚本发布 *更新更新* 图像。
同样,如果 `deployment.yaml` 文件没有变化,则没有必要重新适用该文件。

如果所有前几个步骤都成功的话, 我们可以看活跃的播客的改变:

```powershell
kubectl get pods --watch
```

如果没有遇到错误, 则所有缓存都应输入 *运行中* 状态,在哪个时间点我们可以通过查询 `votingapp` 我们创建的服务对象:

```powershell
kubectl get service votingapp
```

输出中的 `EXTERNAL-IP` 值是如何使用网络浏览器访问服务。
