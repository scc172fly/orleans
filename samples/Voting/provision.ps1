$resourceGroup = "votingapp"
$location = "westus"
$clusterName = "votingapp"
$containerRegistry = "dncvotingapp"

az login

# 创建资源组
az group create --name $resourceGroup --location $location

# 创建 AKSS 集组。 这需要几分钟时间
az aks create --resource-group $resourceGroup --name $clusterName --node-count 3

# 如果您尚未安装 Kubernetes CLI
az aks install-cli

# 认证 Kubernetes CLI
az aks get-credentials --resource-group $resourceGroup --name $clusterName

# 创建一个 Azure 集装箱登记账户并登录到该账户
az acr create --name $containerRegistry --resource-group $resourceGroup --sku Standard

# 为集装箱登记册创建一个服务本服务本,并以 Kubernetes 作为图像解密注册
$acrId = $(az acr show --name $containerRegistry --query id --output tsv)
$acrServicePrincipalName = "$($containerRegistry)-aks-service-principal"
$acrSpPw = $(az ad sp create-for-rbac --name http://$acrServicePrincipalName --scopes $acrId --role acrpull --query password --output tsv)
$acrSpAppId = $(az ad sp list --display-name http://$acrServicePrincipalName --query [].appId --output tsv)
$acrLoginServer = $(az acr show --name $containerRegistry --resource-group $resourceGroup --query loginServer).Trim('"')
kubectl create secret docker-registry $containerRegistry --namespace default --docker-server=$acrLoginServer --docker-username=$acrSpAppId --docker-password=$acrSpPw
