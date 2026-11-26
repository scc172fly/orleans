---
title: "冒险游戏试样项目"
description: "探索与 .NET Orleans一起写的冒险样本项目。"
ms.date: 08/02/2026
ms.topic: sample
---

<a id="adventure-game-sample-project"></a>

# 冒险游戏试样项目

这个样本是一个简单的多玩者文字冒险游戏 灵感来自旧式的,基于文字的冒险游戏

<a id="instructions"></a>

## 指示指示

1. 打开 [冒险抽样](https://github.com/dotnet/orleans/tree/main/samples/Adventure) 中在 Orleans 存储库。
1. 克隆 Orleans 仓库,并构建样本。
1. 启动 _ 冒险服务员_ , 然后是 _ 冒险服务员_ 。
1. 然后,您会被提示在命令行输入您的姓名。输入并开始游戏。

准确的命令和先决条件,请参见样本 [读读](https://github.com/dotnet/orleans/blob/main/samples/Adventure/README.md)。

<a id="overview"></a>

## 概述

_冒险服务员程序以读取 _AdventureMap.json_ 文件为起点。

游戏设置了一系列“ 房间” , 如森林、海滩、洞穴、空地等。 这些位置与其他房间相连, 以模拟游戏的位置和布局。 样本配置只描述少数位置 。

房间可以装有钥匙、剑等“东西”。

冒险者服务程序设置了玩家,并提供了一个简单的文本用户界面,使您可以玩游戏。

你可以在房间周围移动, 并且用简单的指令语言与事物互动, 说诸如“往北走”或“拿铜键”之类的话。

<a id="why-orleans"></a>

## 为什么 Orleans?

Orleans 允许您使用非常简单的 C# 代码描述游戏, 同时让它缩放到一个巨大的多人游戏中。 要让这个动机有意义, 房间的迷宫需要非常大, 支持许多同时播放的玩家。 Orleans 的一个值是你可以设计增长服务; 小规模运行服务的管理费并不重要, 您可以继续相信如果有需要的话会缩小规模 。

<a id="how-is-it-modeled"></a>

## 如何建模?

玩家和房间以 grains模式建模。这些 grains 允许您分发游戏,每个 grain 模型状态和功能都允许您使用此选项。

键等东西被仿制成普通的旧物体——它们只是简单的、不可变的数据结构,在房间和球员之间移动;它们不需要是 grains。

<a id="possible-improvements"></a>

## 可能的改进

1. 地图要大得多,还要大很多
1. 使铜键打开的东西。
1. 允许玩家相互传递信息。
1. 使食物和饮用水能够吃得饱,而且有意义。
