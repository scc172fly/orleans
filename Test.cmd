@if not defined _echo @echo off
setlocal

if not defined BuildConfiguration SET BuildConfiguration=Debug

SET CMDHOME=%~dp0
@REM 移除尾端反斜线 \
set CMDHOME=%CMDHOME:~0,-1%

:: 清除本次会话的“ Platform” 信封变量, 因为它是建筑内部每个项目设置, 而且
:: 误导价值(如HPPC中的“MCD”)可能导致断裂(问题:#69)。
set Platform=

:: 禁用多级别搜索 https://github.com/dotnet/core-setup/blob/main/Documentation/design-docs/multilevel-sharedfx-lookup.md
set DOTNET_MULTILEVEL_LOOKUP=0

pushd "%CMDHOME%"
@cd

SET TestResultDir=%CMDHOME%\Binaries\%BuildConfiguration%\TestResults

if not exist %TestResultDir% md %TestResultDir%

SET _Directory=bin\%BuildConfiguration%\net461\win10-x64

set TESTS=^
'%CMDHOME%\test\Extensions\TesterAzureUtils',^
'%CMDHOME%\test\TesterInternal',^
'%CMDHOME%\test\Tester',^
'%CMDHOME%\test\DefaultCluster.Tests',^
'%CMDHOME%\test\NonSilo.Tests',^
'%CMDHOME%\test\Extensions\AWSUtils.Tests',^
'%CMDHOME%\test\Extensions\Consul.Tests',^
'%CMDHOME%\test\Extensions\ServiceBus.Tests',^
'%CMDHOME%\test\Extensions\TesterAdoNet',^
'%CMDHOME%\test\Extensions\TesterZooKeeperUtils',^
'%CMDHOME%\test\Transactions\Orleans.Transactions.Tests',^
'%CMDHOME%\test\Transactions\Orleans.Transactions.Azure.Test',^
'%CMDHOME%\test\TestInfrastructure\Orleans.TestingHost.Tests',^
'%CMDHOME%\test\DependencyInjection.Tests',^
'%CMDHOME%\test\Orleans.Connections.Security.Tests',^
'%CMDHOME%\test\Analyzers.Tests'"

@Echo Test assemblies = %TESTS%

PowerShell -NoProfile -ExecutionPolicy Bypass -Command "& ./eng/Parallel-Tests.ps1 -directories %TESTS% -dotnet '%_dotnet%'"
set testresult=%errorlevel%
popd
endlocal&set testresult=%testresult%
exit /B %testresult%
