
在 Agent Framework 中，我们通常通过 Workflow 来组织多个 Agent，
而具体的执行方式则由不同的“编排模式（Orchestration Patterns）”决定。

常见的编排模式包括：

1. Sequential（顺序执行）
2. Concurrent（并发执行）
3. Handoffs（任务移交）
4. Group Chat（群聊协作）


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


## Sequential：顺序编排

Sequential 是最基础的一种编排模式。在该模式下，多个 Agent 会按照预定义的顺序依次执行，前一个 Agent 的输出会作为后一个 Agent 的输入。
在 Agent Framework 中，可以通过 `BuildSequential` 方法快速构建顺序执行的 Workflow，其定义如下：

```csharp
public static Workflow BuildSequential(params IEnumerable<AIAgent> agents)
```
该方法接收一个 IEnumerable<AIAgent>，用于指定执行链路中的 Agent 顺序，并返回一个 Workflow 实例。


示例：顺序翻译

下面的示例中，多个翻译 Agent 会按照定义顺序依次执行：

```csharp
var translationAgents =
    from lang in new[] { "法语", "中文", "西班牙语", "英语" }
    select GetTranslationAgent(lang, client);

// 构建顺序编排 Workflow
var workflow = AgentWorkflowBuilder.BuildSequential(translationAgents);

// 执行
await RunWorkflowAsync(
    workflow,
    [new(ChatRole.User, "Hello，World！")]
);
```

输出结果：

【图片】

其实Sqeuential（顺序执行）是最简单的编排方式，其实本质和我们上节介绍的是一样的，BuildSequential 本质上只是对顺序编排的一种语法糖，用于简化线性执行链路的构建。


## Concurrent：并发编排

Concurrent 是一种并发执行的编排模式。在该模式下，多个 Agent 会基于同一输入同时执行，各自生成结果。

创建并发 Workflow 的核心方法是 `BuildConcurrent`，其定义如下：


```csharp
public static Workflow BuildConcurrent(IEnumerable<AIAgent> agents, Func<IList<List<ChatMessage>>, List<ChatMessage>>? aggregator = null)
```

其中：

第一个参数 agents 用于指定参与执行的 Agent 集合
第二个参数 aggregator 是一个可选的聚合函数，用于在并发执行完成后对多个 Agent 的输出结果进行统一处理


### 聚合函数（Aggregator）

在 Concurrent 模式下，每个 Agent 都会生成独立的输出结果，aggregator 的作用是将这些结果合并为最终输出。

开发者可以在此基础上进行多种处理，例如：

- 合并（Merge）
- 筛选（Filter）
- 排序（Rank）
- 融合（Fusion）

如果未提供该参数，框架将使用默认策略对结果进行合并。

### 示例：并发翻译

下面的示例中，多个翻译 Agent 会同时处理同一输入，并通过聚合函数将结果合并：

```csharp
var translationAgents =
    from lang in new[] { "法语", "中文", "西班牙语", "英语" }
    select GetTranslationAgent(lang, client);

var workflow = AgentWorkflowBuilder.BuildConcurrent(
    translationAgents,
    results =>
    {
        var merged = new List<ChatMessage>();

        foreach (var agentOutput in results)
        {
            merged.AddRange(agentOutput);
        }

        return merged;
    });

await RunWorkflowAsync(
    workflow,
    [new(ChatRole.User, "Hello，World！")]);
```

输出结果如下：

【图片】


## Handoffs：任务移交模式

Handoffs 是一种多 Agent 之间的任务转交（delegation）编排模式。在该模式下，由某个 Agent 根据上下文动态决定是否将当前任务交给其他 Agent 继续处理。

从本质上看，Handoffs 可以理解为一种 **LLM-driven routing（由模型驱动的路由）**，即任务的流转路径并非固定，而是由 Agent 在运行时自主决策。


创建 Handoffs 工作流的核心方法是 `CreateHandoffBuilderWith`，它需要指定一个入口 Agent，用于接收初始输入并做出转交决策：

```csharp
public static HandoffWorkflowBuilder CreateHandoffBuilderWith(AIAgent initialAgent)
```

Handoffs 的核心在于定义 Agent 之间的可交接关系，主要有两种形式：

### 一对多（Fan-out）：任务分发

```csharp
public TBuilder WithHandoffs(AIAgent from, IEnumerable<AIAgent> to)
```

表示一个 Agent 可以将任务交给多个候选 Agent， 这种方式通常用于“分流”，由 Agent 根据上下文选择最合适的处理者。



### 多对一（Fan-in）：结果回流

```csharp
public TBuilder WithHandoffs(IEnumerable<AIAgent> from, AIAgent to, string? handoffReason = null)
```

表示多个 Agent 可以将任务交回同一个 Agent，这种方式通常用于“汇聚”，例如统一处理结果、继续决策或发起下一轮任务分发。

需要注意的是，WithHandoffs 仅用于定义“允许的交接路径”，而具体是否发生交接，仍由 Agent 在运行时根据上下文动态决定。

下面是一个典型的 Handoffs 示例：

```csharp
ChatClientAgent historyTutor = new ChatClientAgent(client,"你负责回答历史相关问题。请清晰解释重要事件和背景。仅回答历史相关内容。","history_tutor","用于处理历史问题的专业代理");
ChatClientAgent mathTutor = new ChatClientAgent(client,"你负责解答数学问题。请逐步解释你的推理过程，并包含示例。仅回答数学相关内容。","math_tutor","用于处理数学问题的专业代理");
ChatClientAgent triageAgent = new ChatClientAgent(client,"你需要根据用户的作业问题决定应使用哪个代理。必须始终将问题交接给其他代理。","triage_agent","将消息路由到合适专业代理的分流代理");

var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
               .WithHandoffs(triageAgent, [mathTutor, historyTutor])
               .WithHandoffs([mathTutor, historyTutor], triageAgent)
               .Build();
        List<ChatMessage> messages = [];
        while (true)
        {
            Console.Write("问题：");
            messages.Add(new(ChatRole.User, Console.ReadLine()));
            messages.AddRange(await RunWorkflowAsync(workflow, messages));
        }
```

输出结果为:




## Group Chat：群聊式协作

GroupChat 是一种多 Agent 协作编排模式，通过对话管理器（GroupChatManager）控制多个 Agent 按一定策略参与对话，从而共同完成任务。

在 Agent Framework 中，`GroupChatManager` 负责调度 Agent 的发言顺序与交互流程。其中，`RoundRobinGroupChatManager` 是默认实现，它会让多个 Agent 按轮询方式依次发言，形成一个循环的对话过程。

当前 SDK 仅提供 `RoundRobinGroupChatManager` 作为默认实现，如果你想实现其他调度策略可以通过继承 `GroupChatManager` 自行扩展。

创建 Group Chat 工作流的核心方法是 `CreateGroupChatBuilderWith`，它接收一个工厂函数，用于创建具体的 `GroupChatManager` 实例，从而定义调度策略：

```csharp
public static GroupChatWorkflowBuilder CreateGroupChatBuilderWith(
    Func<IReadOnlyList<AIAgent>, GroupChatManager> managerFactory)
```

下面是一个基于轮询策略的 GroupChat 示例，其中 AddParticipants 方法用于添加参与的 Agent：

```csharp
var translationAgents = from lang in new[] { "法语", "中文", "西班牙语", "英语" } select GetTranslationAgent(lang, client);
var workflow = AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 5 })
    .AddParticipants(translationAgents)
    .WithName("翻译轮询工作流")
    .WithDescription("一个由三个翻译代理按轮询方式依次响应的工作流。")
    .Build();
await RunWorkflowAsync(workflow, [new(ChatRole.User, "Hello，World！")]);

```

输出如下结果：

【图片】



## 总结

这节我们通过结合一些例子来介绍了 Agent Framework 中智能体常见的编排模式以及运行原理。