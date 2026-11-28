<a id="tester-project"></a>

# Tester 项目

本项目用于对 Orleans 运行时进行**黑盒**测试。

本项目只能访问 Orleans 运行时的公共 API 表面，因此所有测试代码均以普通用户级代码的方式执行。

本项目可能同时包含单元测试和系统测试，包括启动测试 Silo 的测试。
