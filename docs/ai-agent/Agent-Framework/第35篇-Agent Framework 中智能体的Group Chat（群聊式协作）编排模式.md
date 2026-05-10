
上节我们讲到了 Agent Framework 中的 Handoffs 编排模式，这种模式适用于需要多个 Agent 之间动态协作的复杂场景。

今天我们来介绍 Agent Framework 中的第四种编排模式：Group Chat（群聊式协作）。在该模式下，多个 Agent 会基于一个对话管理器（GroupChatManager）进行协作，通过对话的方式共同完成任务。

## 创建 AI Agent

在 C# 中，一个 Agent 通常会基于一个聊天模型客户端创建。

```csharp
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");

var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-5.4-mini";

var chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();
```

这里我们不过多介绍如何创建基础Agent，毕竟不是我们关注的重点，关于创建基础Agent的细节，可以参考之前的文章：


## Group Chat：群聊式协作

Group Chat 是一种多 Agent 协作编排模式，通过对话管理器（GroupChatManager）控制多个 Agent 按一定策略参与对话，从而共同完成任务。

在 Agent Framework 中，`GroupChatManager` 负责调度 Agent 的发言顺序与交互流程。其中，`RoundRobinGroupChatManager` 是默认实现，它会让多个 Agent 按轮询方式依次发言，形成一个循环的对话过程。

当前 SDK 仅提供 `RoundRobinGroupChatManager` 作为默认实现，如果你想实现其他调度策略可以通过继承 `GroupChatManager` 自行扩展。

创建 Group Chat 工作流的核心方法是 `CreateGroupChatBuilderWith`，它接收一个工厂函数，用于创建具体的 `GroupChatManager` 实例，从而定义调度策略：

```csharp
public static GroupChatWorkflowBuilder CreateGroupChatBuilderWith(
    Func<IReadOnlyList<AIAgent>, GroupChatManager> managerFactory)
```

下面是一个基于轮询策略的 Group Chat 示例，其中 AddParticipants 方法用于添加参与的 Agent：

```csharp
var translationAgents = from lang in new[] { "法语", "中文", "西班牙语", "英语" } select GetTranslationAgent(lang, client);
var workflow = AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 5 })
    .AddParticipants(translationAgents)
    .WithName("翻译轮询工作流")
    .WithDescription("一个由四个翻译代理按轮询方式依次响应的工作流。")
    .Build();
await RunWorkflowAsync(workflow, [new(ChatRole.User, "Hello，World！")]);

```

输出如下结果：

【图片】



## 总结

本节介绍了 Agent Framework 中的 Group Chat 编排模式，以及 GroupChatManager 的基本调度机制。

Group Chat 与 Handoffs 的区别
Handoffs
当前 Agent 把任务交给另一个 Agent，控制权发生转移。
Group Chat
多个 Agent 持续参与同一上下文对话，由管理器控制发言顺序。

简单理解：

Handoffs = 接力
Group Chat = 圆桌讨论