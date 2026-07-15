# dotnet-agent-playbook


一个面向实战的 **“.NET → AI Agent”** 演进仓库： 
用文档讲清架构思路，用代码样例落地工程实践。

## 仓库定位

- **核心主线：AI Agent（持续更新）**
  - Agent Framework
  - Semantic Kernel
  - Provider/模型接入（含 Foundry / DeepSeek 等相关实践）
- **历史沉淀：Foundation（已归档）**
  - 早期 ASP.NET Core / WebAPI / MVC 等基础内容，保留参考价值，不再作为主线扩展

## 当前目录结构

```text
docs/
├─ ai-agent/
│  ├─ Agent-Framework/
│  └─ Semantic-Kernel/
├─ overview/
└─ _archive/

src/
├─ ai-agent/
│  ├─ Agent-Framework/
│  ├─ Semantic-Kernel/
│  └─ Azure-AI-Foundry-SDK/
└─ _archive/
```

## 如何阅读（推荐路径）

1. 请参考公众号<<Pioneer AI>> 
2. `docs/ai-agent/Agent-Framework/`  
3. `docs/ai-agent/Semantic-Kernel/`  
4. 对照 `src/ai-agent/` 中对应目录运行示例

如果你只关心最新主线，请优先阅读 `docs/ai-agent/`，`_archive` 仅作历史参考。

## docs 与 src 的对应规则

- 文档在 `docs/ai-agent/*`，示例在 `src/ai-agent/*`
- 优先按“篇号/主题”对应（同主题同编号）

## 你可以从这里开始

- 公众号文章系列：https://mp.weixin.qq.com/s/UEAd1TdxHJbN6uWCY2_Zbw
- Agent Framework 示例：`src/ai-agent/Agent-Framework/`


## 仓库地址

https://github.com/bingbing-gui/dotnet-agent-playbook
