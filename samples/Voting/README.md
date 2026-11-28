---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Kubernetes 上的 Orleans 投票示例应用"
urlFragment: "orleans-voting-sample-app-on-kubernetes"
description: "演示 Kubernetes 上投票应用的 Orleans 示例。"
---

<a id="orleans-voting-sample-app-on-kubernetes"></a>

# Kubernetes 上的 Orleans 投票示例应用

![应用程序屏幕截图](./screenshot.png)

这是一个演示如何部署到 Kubernetes 的 [Orleans](https://github.com/dotnet/orleans) 示例应用。它是一个针对一组自定义选项进行投票的简单 Web 应用。应用程序使用 [.NET 通用主机](https://docs.microsoft.com/dotnet/core/extensions/generic-host)，在同一进程中共同托管 [ASP.NET Core](https://docs.microsoft.com/aspnet/core)、Orleans 及 [Orleans Dashboard](https://github.com/OrleansContrib/OrleansDashboard)。

![Orleans Dashboard 屏幕截图](./dashboard.png)

Web 应用发送 HTTP 请求，由 ASP.NET Core MVC 控制器处理，并调用 Orleans Grain。

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，目标框架为 .NET 10。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

按照以下步骤下载并运行示例：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择 **文件** > **打开** > **项目/解决方案**。
    2. 导航到存放已解压示例代码的文件夹，并打开 C# 项目（.csproj）文件。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 在不调试的情况下运行项目。
3. 从命令行运行：
   1. 导航到存放已解压示例代码的文件夹。
   2. 在命令行输入 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

可以执行以下命令在本地运行应用程序：

```powershell
dotnet run -c Release -- --environment Development --urls http://localhost:5000
```

应用程序启动后，在浏览器中打开 <http://localhost:5000> 即可体验应用。Orleans Dashboard 可通过 <http://localhost:8888.> 访问。应用也可部署到 Kubernetes。部署此示例的关键文件是 [`deployment.yaml`](./deployment.yaml)，它描述了所需的 Kubernetes 资源。在部署应用前，需要预配以下资源：

* 一个资源组
* 一个 Azure Container Registry (ACR) 容器注册表
* 一个 Azure Kubernetes Service (AKS) 集群
* 一个允许 AKS 访问 ACR 的服务主体

[`provision.ps1`](./provision.ps1) 脚本尝试自动完成这些步骤，所需的一些名称定义在脚本顶部。
由于该脚本不执行错误处理，最好在 **PowerShell** 终端中逐条执行以下步骤。

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

预配这些资源后，即可定义并部署应用程序。
创建名为 `deployment.yaml` 的文件，内容如下，并根据预配资源时选择的名称进行必要的修改。
查找 `# REPLACEME` 注释并替换相应值。
下面将解释文件的结构。

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

该文件较大，初看可能有些复杂，但其基本结构是创建两个 *Deployment* 资源：一个用于 Redis，另一个用于应用程序。每个 Deployment 都有一个对应的 *Service*，用于路由流量。此外，由于示例使用会查询 Kubernetes API 的 `Microsoft.Orleans.Kubernetes.Hosting` 包，如果集群启用了 RBAC，则需要预配 *Role* 及对应的 *RoleBinding*。`deployment.yaml` 文件为这些资源分别提供一个由 `---` 分隔的节。

创建 `deployment.yaml` 文件后，需要构建并部署应用程序。使用 `docker` 将源码复制到新的构建容器中并构建应用程序，再将结果复制到新的镜像层。

执行以下命令以构建容器镜像、推送至 Azure Container Registry，并将 `deployment.yaml` 文件部署到 Kubernetes。
请注意，需要像预配资源时一样替换变量名称。

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

最后执行的命令会重启部署。
只有使用上述脚本发布*更新后的*镜像时，才需要重启部署。
同样，如果 `deployment.yaml` 文件没有变化，则无需重新应用。

如果前面的所有步骤均成功，即可观察活跃 Pod 的变化：

```powershell
kubectl get pods --watch
```

如果没有遇到错误，所有 Pod 都应进入 *Running* 状态。此时可以查询我们创建的 `votingapp` Service 对象，以确定为服务分配了哪个 IP 地址：

```powershell
kubectl get service votingapp
```

输出中的 `EXTERNAL-IP` 值即为使用 Web 浏览器访问该服务的地址。
