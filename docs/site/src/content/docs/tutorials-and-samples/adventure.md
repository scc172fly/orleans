---
title: Adventure 冒险游戏示例项目
description: 了解使用 .NET Orleans 编写的 Adventure 示例项目。
ms.date: 08/02/2026
ms.topic: sample
---

<a id="adventure-game-sample-project"></a>

# Adventure 冒险游戏示例项目

本示例是一款简单的多人文字冒险游戏，灵感来自经典的纯文字冒险游戏。

<a id="instructions"></a>

## 操作说明

1. 在 Orleans 仓库中打开 [Adventure 示例](https://github.com/dotnet/orleans/tree/main/samples/Adventure)。
1. 克隆 Orleans 仓库并生成示例项目。
1. 首先启动 _AdventureServer_，然后启动 _AdventureClient_。
1. 命令行将提示输入姓名。输入姓名后即可开始游戏。

有关确切的命令和先决条件，请参阅示例的 [README](https://github.com/dotnet/orleans/blob/main/samples/Adventure/README.md)。

<a id="overview"></a>

## 概述

_AdventureServer_ 程序启动时读取 _AdventureMap.json_ 文件。

它建立一系列“房间”，例如森林、海滩、洞穴、林间空地等。这些地点与其他房间相连，用于描述游戏中的场所及其布局。示例配置仅描述了少数几个地点。

房间中可以包含钥匙、剑等“物品”。

_AdventureClient_ 程序创建你的玩家，并提供简单的文本用户界面，让你能够游玩游戏。

你可以使用简单的命令语言在房间之间移动并与物品交互，例如输入“go north”（向北走）或“take brass key”（拿起黄铜钥匙）。

<a id="why-orleans"></a>

## 为什么选择 Orleans？

借助 Orleans，可以使用非常简单的 C# 代码描述游戏，同时使其能够扩展为大型多人在线游戏。要体现这种扩展能力的价值，房间构成的迷宫必须足够大，并能够支持大量玩家同时在线。Orleans 的一项价值在于可以面向未来增长来设计服务：小规模运行时的额外开销并不显著，而在有需要时仍有信心扩展服务。

<a id="how-is-it-modeled"></a>

## 如何建模？

玩家和房间均建模为 Grain。这些 Grain 使游戏可以分布式运行，每个 Grain 分别封装自身的状态和功能。

钥匙等物品则建模为普通对象：它们只是简单的不可变数据结构，可以在房间和玩家之间转移，无需建模为 Grain。

<a id="possible-improvements"></a>

## 可能的改进

1. 大幅扩大地图规模。
1. 让黄铜钥匙能够打开某个物体的锁。
1. 允许玩家互相发送消息。
1. 支持进食和饮水，并让这些行为具有实际游戏意义。
