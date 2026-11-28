$resourceGroup = "votingapp"
$location = "westus"
$clusterName = "votingapp"
$containerRegistry = "dncvotingapp"

az login

# 创建资源组。
az group create --name $resourceGroup --location $location

# 创建 AKS 群集，这可能需要几分钟。
az aks create --resource-group $resourceGroup --name $clusterName --node-count 3

# 如果尚未安装 Kubernetes CLI，请先安装。
az aks install-cli

# 对 Kubernetes CLI 进行身份验证。
az aks get-credentials --resource-group $resourceGroup --name $clusterName

# 创建 Azure 容器注册表账户并登录。
az acr create --name $containerRegistry --resource-group $resourceGroup --sku Standard

# 为容器注册表创建服务主体，并在 Kubernetes 中将其注册为镜像拉取 Secret。
$acrId = $(az acr show --name $containerRegistry --query id --output tsv)
$acrServicePrincipalName = "$($containerRegistry)-aks-service-principal"
$acrSpPw = $(az ad sp create-for-rbac --name http://$acrServicePrincipalName --scopes $acrId --role acrpull --query password --output tsv)
$acrSpAppId = $(az ad sp list --display-name http://$acrServicePrincipalName --query [].appId --output tsv)
$acrLoginServer = $(az acr show --name $containerRegistry --resource-group $resourceGroup --query loginServer).Trim('"')
kubectl create secret docker-registry $containerRegistry --namespace default --docker-server=$acrLoginServer --docker-username=$acrSpAppId --docker-password=$acrSpPw
