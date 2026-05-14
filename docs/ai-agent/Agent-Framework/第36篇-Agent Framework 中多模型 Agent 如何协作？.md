
我们前面介绍了 Agent Framework 的几种编排模式：

- Sequential
- Concurrent
- Handoffs
- Group Chat

前面的示例基本都是基于单模型完成的。也就是说，不管工作流怎么编排，背后实际调用的其实还是同一个模型。

但真实项目里，很多时候并不是这样。让不同模型分工协作，而不是把所有事情都交给一个模型。

这篇我们用一个简单例子，看看怎么在 Agent Framework 里做多模型协作。

这里我用了三种不同的模型服务：

- Ollama 本地模型
- OpenAI
- Azure OpenAI

分别负责不同任务。

## 示例

这个例子很简单，我们给一个主题：

```csharp
const string Topic = "金毛贵宾犬是最好的宠物。";
```

然后让三个 Agent 接力完成任务：

### Researcher

第一个 Agent 是 Researcher。这里我使用的是本地 Ollama 的 qwen3:1.7b 模型，它的职责很简单，就是根据给定主题生成内容。为了演示后面的事实核查流程，我在提示词里故意要求它加入一条明显错误的信息。这样设计主要是为了让后面的 Checker 有事情可做，如果前面生成的内容全部正确，那整个核查流程就失去意义了。

### Checker

第二个 Agent 是 Checker，它负责检查 Researcher 生成内容里的事实是否可靠。它会把内容判断为“支持”“部分支持”“未验证”或者“错误”。这里还额外挂了 HostedWebSearchTool（支持OpenAI原生API），也就是说这个 Agent 不只是依赖模型自身知识，还可以联网搜索辅助判断，这样事实核查会更靠谱一些。

### Reporter

第三个 Agent 是 Reporter。这里我们使用 Azure OpenAI 模型，它的职责是根据前两个 Agent 的输出生成最终总结。它不会重新自由发挥生成内容，而是只保留经过 Checker 确认过的信息，把错误或者无法验证的内容过滤掉，最终输出一份更可靠的结果。

这个流程很适合展示多模型协作的价值：生成模型负责写内容，核查模型负责提高结果可信度，总结模型负责整理最终输出。

## 多模型服务的接入方式

我们创建三个Agent，分别对应三个不同模型：

```csharp
#region Ollama Provider
var endpoint = "http://localhost:11434/";
var modelName = "qwen3:1.7b";
AIAgent researcher = new OllamaApiClient(new Uri(endpoint), modelName)
                    .AsAIAgent(instructions: @"针对用户指定的主题写一篇短文。文章应为三到五段，使用高中阅读水平的语言，并包含相关背景信息、
                    关键论点和显著观点。你必须至少包含一条关于该主题的愚蠢且客观错误的信息，但你要相信它是真的。",
    name: "researcher",
    description: "研究某个主题并撰写相关内容。");
#endregion


#region OpenAI Provider
AIAgent checker = new OpenAI.OpenAIClient(
    "sk-xxxxxxxxxxxx")
    .GetResponsesClient()
    .AsIChatClient("gpt-5")
    .AsAIAgent(instructions: @"评估研究人员的文章。针对其中的任何声明，根据可靠来源验证其准确性，并标明其属于以下哪种情况：
                    支持、
                    部分支持、
                    未验证、
                    错误，
                    同时提供简短的理由说明。",
    name: "checker",
    description: "根据可靠来源进行事实核查，并标记不准确的信息。", [new HostedWebSearchTool()]);
#endregion


#region Azure OpenAI
endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("未设置 AZURE_OPENAI_ENDPOINT。");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
AIAgent reporter = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
                             .GetChatClient(deploymentName).AsIChatClient()
                             .AsAIAgent(instructions: """
                                        将原始文章总结为一个单段落，同时结合后续的事实核查来纠正任何不准确之处。只包含已被事实核查员确认的事实。
                                        省略任何被标记为不准确或未验证的信息。总结应清晰、简洁且信息充分。
                                        你绝对不要解释你正在做什么。只需输出最终段落。
                                        """,
                                        name: "reporter",
                                        description: "将研究员的文章总结为单个段落，只关注事实核查员已确认的事实。");
#endregion
```

## 构建顺序式工作流

三个 Agent 定义完成后，示例使用 `AgentWorkflowBuilder.BuildSequential` 构建顺序工作流：

```csharp
AIAgent workflowAgent = AgentWorkflowBuilder.BuildSequential(researcher, checker, reporter).AsAIAgent();
```

这个工作流的执行顺序是：

```text
Researcher -> Checker -> Reporter
```

也就是说：

1. `Researcher` 先生成一篇关于主题的短文。
2. `Checker` 接收这篇短文并进行事实核查。
3. `Reporter` 根据原始短文和核查结果生成最终总结。

## 流式运行工作流

最后，示例通过 `RunStreamingAsync` 运行工作流，并实时输出每个 Agent 的结果：

```csharp
await foreach (var update in workflowAgent.RunStreamingAsync(Topic))
{
    ...
    Console.Write(update.Text);
}
```

流式输出可以提升用户体验，因为用户不需要等待整个工作流完全结束后才看到结果。尤其是在多个模型顺序执行时，流式反馈可以让调用过程更加透明。

示例中还处理了 `WorkflowEvent`：

```csharp
if ((update.Contents == null || update.Contents.Count == 0) && update.RawRepresentation is WorkflowEvent)
{
    continue;
}
```

这是为了跳过只表示工作流状态、但没有实际文本内容的事件。

此外，程序会根据 `AuthorName` 区分当前输出来自哪个 Agent：

```csharp
if (lastAuthor != update.AuthorName)
{
    lastAuthor = update.AuthorName;
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n\n** {update.AuthorName} **");
    Console.ResetColor();
}
```

这样在控制台中可以清晰看到每个 Agent 的输出边界。



## 总结

Agent Framework 在 .NET 中具有编排多模型 Agent 的能力。它通过 Google Gemini、OpenAI 和 Anthropic Claude 分别承担不同的任务。