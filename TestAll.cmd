@setlocal
@ECHO off

SET CMDHOME=%~dp0
@REM 移除尾端反斜线 \
set CMDHOME=%CMDHOME:~0,-1%

@REM 由于更多 Windows.cmd 脚本参数通过 quirks, 我们不能把这个值作为 cmdline 参数传递, 
@REM  所以我们需要通过后门, 以环境变量的形式,
set TEST_FILTERS=/[(Category=BVT)^|(Category=SlowBVT)^|(Category=Functional)]

@REM 注意:我们在这里将 _full_control 转到 Test.cmd 脚本, 因为不会使用电话。

"%CMDHOME%\Test.cmd" %1

@REM 注意:执行不会返回这里, 返回到调用者的退出代码将与其他脚本返回的一样。
