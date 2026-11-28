@setlocal
@ECHO off

SET CMDHOME=%~dp0
@REM 删除末尾反斜杠 \
set CMDHOME=%CMDHOME:~0,-1%

@REM 由于 Windows .cmd 脚本传递参数的特殊行为，无法将此值作为命令行参数传递，
@REM 因此需要通过 setlocal 作用域内的环境变量间接传递。
set TEST_FILTERS=/[(Category=BVT)^|(Category=SlowBVT)^|(Category=Functional)]

@REM 注意：由于未使用 CALL，此处将控制权完全交给 Test.cmd 脚本。

"%CMDHOME%\Test.cmd" %1

@REM 注意：执行不会返回此处；返回给调用方的退出代码就是另一个脚本返回的退出代码。
