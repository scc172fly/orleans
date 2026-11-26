# --------------------
# Orleans 构建脚本
# --------------------

. (Join-Path $PSScriptRoot 'eng\common.ps1')

$scriptDir = $PSScriptRoot
$solution = Join-Path $scriptDir "Orleans.slnx"

# 定义构建旗帜配置
if ($null -eq $env:BUILD_FLAGS)
{
    $env:BUILD_FLAGS = "/m /v:m"
}
if ($null -eq $env:BuildConfiguration)
{
    $env:BuildConfiguration = "Debug"
}

# 清除本次会话的“ Platform” 信封变量, 因为它是建筑内部每个项目设置, 而且
# 误导值(如HPPCs中的“MCD”)可能导致断裂(问题:#69)。
$Platform = $null

# 禁用多级别查找 https://github.com/dotnet/core-setup/blob/main/Documentation/design-docs/multilevel-sharedfx-lookup.md
 $DOTNET_MULTILEVEL_LOOKUP = 0

$BuildProperties = "/p:Configuration=$env:BuildConfiguration";

 # 设置调试构建 DateTime 后缀
 if ($env:BuildConfiguration -eq "Debug")
 {
    $dateSuffix = Get-Date -Format "yyyyMMddHHmm"
    $BuildProperties = $BuildProperties + " /p:VersionDateSuffix=$dateSuffix"
 }

Write-Output "===== Building $solution ====="

Install-Dotnet

if ($args[0] -ne "Pack")
{
    Write-Output "Build $env:BuildConfiguration =============================="
    Invoke-Dotnet -Command "restore" -Arguments "$env:BUILD_FLAGS /bl:${env:BuildConfiguration}-Restore.binlog ${BuildProperties} `"$solution`""
    Invoke-Dotnet -Command "build" -Arguments "$env:BUILD_FLAGS /bl:${env:BuildConfiguration}-Build.binlog ${BuildProperties} `"$solution`""
}

Write-Output "Package $env:BuildConfiguration ============================"
Invoke-Dotnet -Command "pack" -Arguments "--no-build --no-restore $BUILD_FLAGS /bl:${env:BuildConfiguration}-Pack.binlog ${BuildProperties} `"$solution`""

Write-Output "===== Build succeeded for $solution ====="
