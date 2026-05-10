
前面两节我们介绍了Agent Framework 中智能体的Sequential（顺序编排）和 Concurrent（并发编排）两种基础的编排模式。

今天我们来介绍另外一种非常重要的编排模式叫做 Handoffs（任务移交模式），它主要用于处理智能体之间的任务切换和协作。

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


## Handoffs：任务移交模式

Handoffs 是一种多 Agent 之间的任务转交编排模式。在该模式下，由某个 Agent 根据上下文动态决定是否将当前任务交给其他 Agent 继续处理。

从本质上看，Handoffs 可以理解为一种 **LLM-driven routing（由模型驱动的路由）**，即任务的流转路径并非固定，而是由 Agent 在运行时自主决策。

创建 Handoffs 编排链路的核心方法是 `CreateHandoffBuilderWith`，它需要指定一个入口 Agent，用于接收初始输入并做出转交决策：

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

注意：定义 WithHandoffs 只是声明允许的转交路径，Agent 是否真的执行 handoff，仍取决于模型推理和 Prompt 设计。

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





## 总结

截至目前，我们已经介绍了 Agent Framework 中三种核心编排模式：

Sequential：执行路径固定
Concurrent：并行执行固定
Handoffs：执行路径动态决定

也就是说Handoffs 是“运行时决策型编排”。


