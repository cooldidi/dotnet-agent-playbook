
在许多 LLM 应用中，单次生成往往难以稳定满足质量要求。模型可能遗漏约束、表达不够清晰，或者在复杂任务中产生不完整的结果。为了提高输出质量，一个常见思路是将“生成”与“评审”拆开：先由一个模型完成初稿，再由另一个模型审查结果、提出反馈，并在必要时触发修订。

这种思路通常被称为 Reflection（反思）。Reflection 指的是一次 LLM 生成之后，紧接着触发另一次基于前一次输出结果的评审或反思。第二个 LLM 不直接完成原始任务，而是针对第一个 LLM 的输出给出批评、建议或改进方向。

Writer–Critic 正是 Reflection 模式在多 Agent 协作中的一种典型实现：Writer 负责生成内容，Critic 负责评审内容，并根据评审结果决定是否进入下一轮修订。通过“生成—评审—修订”的闭环，系统可以在多轮迭代中逐步提升内容质量。

在 AutoGen 或 Agent Framework 的上下文中，这种模式可以被实现为一组协作 Agent。它们持续交互，直到达到某个停止条件，例如内容通过评审，或者达到最大迭代次数。Agent Framework 为这一模式提供了工程化实现方式：基于条件路由的工作流图、跨执行步骤的共享状态、结构化输出以及流式响应。

---

##  Writer–Critic 工作流的需求

本文示例实现的是一个可迭代的内容评审系统。

需求如下：

1. 用户输入一个写作主题或写作请求。
2. Writer Agent 根据用户请求生成初稿。
3. Critic Agent 对 Writer 生成的内容进行评审。
4. 如果内容不符合要求，Critic Agent 返回具体反馈。
5. Writer Agent 根据反馈对内容进行修订。
6. Writer 与 Critic 持续循环，直到内容通过评审，或达到最大迭代次数。
7. 内容通过后，Summary Agent 输出最终版本。

我们定义Writer Agent，首先Writer Agent 接收一个字符串输入，代表用户的初始写作请求。Writer Agent 生成的内容以 ChatMessage 对象的形式输出，供后续的 Critic Agent 评审使用。

Writer Agent 定义如下：

```csharp
internal sealed partial class WriterExecutor : Executor
{
    private readonly AIAgent _agent;
    public WriterExecutor(IChatClient chatClient) : base("Writer")
    {
        this._agent = new ChatClientAgent(
            chatClient,
            name: "Writer",
            instructions: """
                你是一个熟练的写作者。根据用户请求创建清晰、有吸引力的内容。
                如果收到反馈，请仔细修改内容以解决所有问题。
                保持相同的主题和长度要求。
                """
        );
    }
    [MessageHandler]
    public async ValueTask<ChatMessage> HandleInitialRequestAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return await this.HandleAsyncCoreAsync(new ChatMessage(ChatRole.User, message), context, cancellationToken);
    }
    [MessageHandler]
    public async ValueTask<ChatMessage> HandleRevisionRequestAsync(
        CriticDecision decision,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        string prompt = "根据以下反馈修改内容：\n\n" +

                       $"反馈: {decision.Feedback}\n\n" +
                       $"原始内容:\n{decision.Content}";

        return await this.HandleAsyncCoreAsync(new ChatMessage(ChatRole.User, prompt), context, cancellationToken);
    }
    private async Task<ChatMessage> HandleAsyncCoreAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        FlowState state = await FlowStateHelpers.ReadFlowStateAsync(context);

        Console.WriteLine($"\n=== 写作者 (迭代 {state.Iteration}) ===\n");

        StringBuilder sb = new();
        await foreach (AgentResponseUpdate update in this._agent.RunStreamingAsync(message, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                sb.Append(update.Text);
                Console.Write(update.Text);
            }
        }
        Console.WriteLine("\n");

        string text = sb.ToString();
        state.History.Add(new ChatMessage(ChatRole.Assistant, text));
        await FlowStateHelpers.SaveFlowStateAsync(context, state);

        return new ChatMessage(ChatRole.User, text);
    }
}
```
WriterExecutor 继承自 Executor，并通过构造函数创建内部的 ChatClientAgent。这个 Agent 的系统指令定义了 Writer 的角色：根据用户请求生成清晰、有吸引力的内容，并在收到反馈时进行修订。

这里有两个公开的消息处理方法：

```csharp
HandleInitialRequestAsync(string message, ...)
HandleRevisionRequestAsync(CriticDecision decision, ...)
```

它们都使用了 `[MessageHandler]` 特性。Agent Framework 会根据进入该节点的数据类型，选择匹配的消息处理方法。

**第一次进入 Writer 时**，输入是用户的原始字符串，因此会调用：

```csharp
HandleInitialRequestAsync(string message, ...)
```

**当 Critic 不通过并返回 CriticDecision 时**，数据再次回到 Writer，这时会调用：

```csharp
HandleRevisionRequestAsync(CriticDecision decision, ...)
```

`HandleAsyncCoreAsync` 是内部复用的核心方法，不会被工作流框架直接调用。它负责：
- 真正调用 Writer Agent
- 流式接收模型输出
- 更新共享状态
- 返回生成后的 ChatMessage

**需要注意的是**，`HandleAsyncCoreAsync` 本身并不直接调用 Critic。它只是返回 Writer 生成的 ChatMessage。随后，工作流中定义的 `writer -> critic` 边会把这个返回值路由给 `CriticExecutor`。

##  Critic Agent：结构化评审与反馈

Critic Agent 负责审查 Writer 生成的内容，并返回结构化决策。

其输出类型是 CriticDecision：
```csharp
internal sealed class CriticDecision
{
    [JsonPropertyName("approved")]
    public bool Approved { get; set; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = "";

    [JsonIgnore]
    public string Content { get; set; } = "";

    [JsonIgnore]
    public int Iteration { get; set; }
}
```

其中：

Approved 表示内容是否通过评审。
Feedback 表示未通过时的修改建议。
Content 和 Iteration 是工作流内部使用的字段，不参与 JSON 结构化输出。

Critic Agent 定义如下：

```csharp
internal sealed class CriticExecutor : Executor<ChatMessage, CriticDecision>
{
    private readonly AIAgent _agent;

    public CriticExecutor(IChatClient chatClient) : base("Critic")
    {
        this._agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Critic",
            ChatOptions = new()
            {
                Instructions = """
                    你是一名建设性的评论者。请审查内容并提供具体反馈。
                    只有当内容经过至少 2 次修改后，才允许 approved 为 true。
                    如果是第一次审查，必须返回 approved=false，并给出具体修改建议。
                    请按照以下结构化格式给出你的判断：
                        approved：如果内容良好则为 true，如果需要修改则为 false
                        feedback：需要改进的具体建议（如果已批准则为空）
                    你的反馈应当简洁，但具体明确。
                    """,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<CriticDecision>()
            }
        });
    }

    public override async ValueTask<CriticDecision> HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        FlowState state = await FlowStateHelpers.ReadFlowStateAsync(context);

        Console.WriteLine($"=== 评论者 (迭代 {state.Iteration}) ===\n");

        // 使用 RunStreamingAsync 获取流式更新，然后在末尾反序列化
        IAsyncEnumerable<AgentResponseUpdate> updates = this._agent.RunStreamingAsync(message, cancellationToken: cancellationToken);

        // 实时输出流式内容（例如推理或说明）
        await foreach (AgentResponseUpdate update in updates)
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                Console.Write(update.Text);
            }
        }
        Console.WriteLine("\n");

        // 将流转换为响应，并反序列化结构化输出
        AgentResponse response = await updates.ToAgentResponseAsync(cancellationToken);
        CriticDecision decision = JsonSerializer.Deserialize<CriticDecision>(response.Text, JsonSerializerOptions.Web)
            ?? throw new JsonException("无法从响应文本反序列化 CriticDecision。");

        Console.WriteLine($" 决策: {(decision.Approved ? "✅ 已批准" : "❌ 需要修改")}");
        if (!string.IsNullOrEmpty(decision.Feedback))
        {
            Console.WriteLine($"反馈: {decision.Feedback}");
        }
        Console.WriteLine();

        // 安全措施：如果达到最大迭代次数，则自动批准
        if (!decision.Approved && state.Iteration >= Program.MaxIterations)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ 达到最大迭代次数 ({Program.MaxIterations}) - 自动批准");
            Console.ResetColor();
            decision.Approved = true;
            decision.Feedback = "";
        }
        // 仅在拒绝时递增迭代次数（将回到 Writer）
        if (!decision.Approved)
        {
            state.Iteration++;
        }
        // 将决策存入历史记录
        state.History.Add(new ChatMessage(ChatRole.Assistant,
            $"[决策: {(decision.Approved ? "已批准" : "需要修改")}] {decision.Feedback}"));

        await FlowStateHelpers.SaveFlowStateAsync(context, state);
        // 填充工作流专用字段
        decision.Content = message.Text ?? "";
        decision.Iteration = state.Iteration;

        return decision;
    }
}
```
CriticExecutor 继承自：
```csharp
Executor<ChatMessage, CriticDecision>
```

这表示该执行器接收 ChatMessage 作为输入，并输出 CriticDecision。

它的入口方法是：
```csharp
HandleAsync(ChatMessage message, ...)
```
这里的 message 就是 Writer 生成的内容。

Critic 的评审结果来自 LLM 的结构化输出。通过：
```csharp
ResponseFormat = ChatResponseFormat.ForJsonSchema<CriticDecision>()
```
我们要求模型按照 CriticDecision 的 JSON Schema 返回结果，从而让程序可以稳定解析：

```csharp
CriticDecision decision = JsonSerializer.Deserialize<CriticDecision>(...)
```
不过需要注意，approved=true 或 approved=false 的初始判断来自 LLM。Agent Framework 只是提供结构化输出能力，并不会替我们判断内容质量。程序侧仍然可以在必要时覆盖这个判断，例如达到最大迭代次数时自动批准：
```csharp
if (!decision.Approved && state.Iteration >= Program.MaxIterations)
{
    decision.Approved = true;
    decision.Feedback = "";
}
```
这就是防止无限循环的安全阀。

## Summary Agent：输出最终内容

当 Critic 返回 Approved = true 时，工作流会进入 Summary Agent。
Summary Agent 的职责不是继续评审，而是将最终通过的内容呈现给用户


```csharp
internal sealed class SummaryExecutor : Executor<CriticDecision, ChatMessage>
{
    private readonly AIAgent _agent;

    public SummaryExecutor(IChatClient chatClient) : base("Summary")
    {
        this._agent = new ChatClientAgent(
            chatClient,
            name: "Summary",
            instructions: """
                你将最终批准后的内容呈现给用户。
                仅输出润色后的最终内容——不需要任何额外说明。
                """
        );
    }
    public override async ValueTask<ChatMessage> HandleAsync(
        CriticDecision message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== 总结 ===\n");

        string prompt = $"呈现此已批准的内容：\n\n{message.Content}";

        StringBuilder sb = new();
        await foreach (AgentResponseUpdate update in this._agent.RunStreamingAsync(new ChatMessage(ChatRole.User, prompt), cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                sb.Append(update.Text);
            }
        }

        ChatMessage result = new(ChatRole.Assistant, sb.ToString());
        await context.YieldOutputAsync(result, cancellationToken);
        return result;
    }
```

SummaryExecutor 继承自：
```csharp
Executor<CriticDecision, ChatMessage>
```
这表示它接收 Critic 的决策结果，并输出最终的 ChatMessage。

这里的关键方法是：
```csharp
context.YieldOutputAsync(result, cancellationToken);
```

它用于向工作流外部发布最终输出。外层的 WatchStreamAsync 会接收到 WorkflowOutputEvent，然后打印最终结果。


## 工作流图与条件路由

三个执行器定义完成后，需要通过 WorkflowBuilder 组织成工作流图：
```csharp
WorkflowBuilder workflowBuilder = new WorkflowBuilder(writer)
    .AddEdge(writer, critic)
    .AddSwitch(critic, sw => sw
        .AddCase<CriticDecision>(cd => cd?.Approved == true, summary)
        .AddCase<CriticDecision>(cd => cd?.Approved == false, writer))
    .WithOutputFrom(summary);
```
这段代码定义了完整的路由关系：

Writer -> Critic
Critic -> Summary，条件是 Approved == true
Critic -> Writer，条件是 Approved == false

因此，工作流的数据流转如下：

InitialTask(string)
  -> Writer.HandleInitialRequestAsync(string)
  -> Writer.HandleAsyncCoreAsync(ChatMessage)
  -> ChatMessage
  -> Critic.HandleAsync(ChatMessage)
  -> CriticDecision
      -> Approved == false: Writer.HandleRevisionRequestAsync(CriticDecision)
      -> Approved == true: Summary.HandleAsync(CriticDecision)
  -> WorkflowOutputEvent

第一次进入 Writer 时，输入类型是 string，因此匹配 HandleInitialRequestAsync。

当 Critic 不通过时，它返回的是 CriticDecision。这个对象再次流入 Writer，因此匹配 HandleRevisionRequestAsync。

这也是 [MessageHandler] 的关键作用：对于继承自非泛型 Executor 的执行器，可以定义多个消息处理方法，框架根据进入节点的数据类型选择对应方法。

而对于：

Executor<TInput, TOutput>
输入类型已经由泛型参数 TInput 明确指定，因此框架会调用其重写的 HandleAsync 方法。

##  状态控制
在 Writer–Critic 工作流中，状态控制非常重要。因为 Writer 和 Critic 之间存在循环，如果没有状态记录，就无法判断当前已经迭代了多少次，也无法设置最大迭代次数。

状态定义如下：
```csharp
internal sealed class FlowState
{
    public int Iteration { get; set; } = 1;
    public List<ChatMessage> History { get; } = [];
}
```
其中：

Iteration 表示当前迭代轮次。
History 保存 Writer 输出和 Critic 决策记录。
状态通过 IWorkflowContext 读写：
```csharp
internal static class FlowStateShared
{
    public const string Scope = "FlowStateScope";
    public const string Key = "singleton";
}

internal static class FlowStateHelpers
{
    public static async Task<FlowState> ReadFlowStateAsync(IWorkflowContext context)
    {
        FlowState? state = await context.ReadStateAsync<FlowState>(
            FlowStateShared.Key,
            scopeName: FlowStateShared.Scope);

        return state ?? new FlowState();
    }

    public static ValueTask SaveFlowStateAsync(IWorkflowContext context, FlowState state)
        => context.QueueStateUpdateAsync(
            FlowStateShared.Key,
            state,
            scopeName: FlowStateShared.Scope);
}
```
ReadStateAsync 用于从工作流上下文中读取状态。如果之前没有保存过状态，则返回 null，因此这里会创建一个新的 FlowState。

QueueStateUpdateAsync 用于将修改后的状态保存回工作流上下文，使后续执行器能够读取到更新后的状态。

在 Writer 中，状态主要用于记录生成历史：

```csharp
state.History.Add(new ChatMessage(ChatRole.Assistant, text));
await FlowStateHelpers.SaveFlowStateAsync(context, state);
```
在 Critic 中，状态用于控制循环次数：
```csharp
if (!decision.Approved && state.Iteration >= Program.MaxIterations)
{
    decision.Approved = true;
    decision.Feedback = "";
}

if (!decision.Approved)
{
    state.Iteration++;
}
```

因此状态控制的逻辑是：

第 1 轮：
Writer 生成内容
Critic 评审
如果不通过，Iteration 增加到 2

第 2 轮：
Writer 根据反馈修订
Critic 再次评审
如果不通过，Iteration 增加到 3

第 3 轮：
Writer 再次修订
Critic 再次评审
如果仍不通过，由程序侧强制批准，避免无限循环
需要注意的是，当前样例虽然把 Writer 输出和 Critic 决策写入了 History，但并没有在后续 Agent 调用中将 History 作为上下文传给模型。因此，History 在当前代码中更多用于记录、调试或后续扩展，并不会自动影响模型生成结果。

如果希望历史真正参与模型推理，需要显式将 state.History 拼入 prompt，或者构造完整的消息列表传给 Agent。

## 小结

Writer–Critic 工作流展示了 Agent Framework 中一种典型的多 Agent 协作模式。

它的核心机制包括：

Writer Agent 负责生成和修订内容。
Critic Agent 负责评审内容，并通过结构化输出返回 CriticDecision。
Summary Agent 负责输出最终通过的内容。
工作流通过 AddSwitch 根据 Approved 字段进行条件路由。
IWorkflowContext 用于跨执行器共享状态。
Iteration 用于限制最大循环次数，避免无限迭代。
[MessageHandler] 允许同一个 Executor 根据输入类型处理不同场景。
从数据流角度看，整个流程是：

string
  -> Writer
  -> ChatMessage
  -> Critic
  -> CriticDecision
  -> Writer 或 Summary
从控制流角度看，整个流程是：

生成
  -> 评审
      -> 不通过：修订
      -> 通过：输出
这个样例的价值不只是展示多个 Agent 如何串联，更重要的是展示了如何把 LLM 的不确定输出纳入一个可控的工程流程中：通过结构化输出获得可解析的决策，通过条件路由实现闭环，通过共享状态控制迭代边界。


