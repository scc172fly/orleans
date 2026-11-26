function Install-Dotnet 
{
   $installDotnet = $true
   try 
   {
       # 检查 dotnet.exe 命令是否存在
       Get-Command dotnet.exe 2>&1 | Out-Null
       # 从 global.json读取版本
       $globalJson = Get-Content .\global.json | ConvertFrom-Json
       $requiredVersion = $globalJson.sdk.version
       # 检查已安装版本
       $localVersions = dotnet --list-sdks |% {  $_.Split(" ")[0] }
       Write-Output "Required SDK: $requiredVersion"
       Write-Output "Installed SDKs: $localversions"
       # 如果所需版本没有安装, 我们将调用安装脚本
       $installDotnet = !$localVersions.Contains($globalJson.sdk.version)
   }
   catch
   {
       Write-Output "dotnet not found"
       # 什么都不做,我们安装点网
   }

   if ($installDotnet)
   {
       Write-Output "Installing dotnet ${requiredVersion}"

       New-Item -ItemType Directory -Force -Path .\Tools | Out-Null

       [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
       Invoke-WebRequest `
           -Uri "https://dot.net/v1/dotnet-install.ps1" `
           -OutFile ".\Tools\dotnet-install.ps1"

       & .\Tools\dotnet-install.ps1 -InstallDir Tools\dotnetcli\ -JSonFile .\global.json
   }
}

function Invoke-Dotnet 
{
   param 
   (
       $Command,
       $Arguments
   )
   $cmdArgs = @()
   $cmdArgs = $cmdArgs + $Command
   $cmdArgs = $cmdArgs + ($Arguments -split "\s+")
   Write-Output "dotnet $cmdArgs"
   & dotnet $cmdArgs
   if ($LASTEXITCODE -ne 0)
   {
       Write-Error "===== Build FAILED -- $Command with error $LASTEXITCODE - CANNOT CONTINUE ====="
       Exit $LASTEXITCODE
   }
}