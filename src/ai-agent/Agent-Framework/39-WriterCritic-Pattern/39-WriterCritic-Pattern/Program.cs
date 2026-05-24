// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace WriterCriticWorkflow;

/// <summary>
/// 此示例演示了 Writer 与 Critic 智能体之间的迭代优化工作流。
///
/// 该工作流实现了一个内容创建与审查循环：
/// 1. Writer 根据用户请求创建初始内容
/// 2. Critic 审查内容，并通过结构化输出提供反馈
/// 3. 如果通过：Summary 执行器展示最终内容
/// 4. 如果未通过：Writer 根据反馈进行修改（回环）
/// 5. 持续进行，直到通过或达到最大迭代次数（3）
///
/// 当你需要以下能力时，这种模式非常有用：
/// - 通过反馈循环持续改进内容
/// - 带有审查者批准机制的质量门禁
/// - 最大迭代次数限制，以防止无限循环
/// - 基于智能体决策的条件式工作流路由
/// - 用于可靠决策的结构化输出
///
/// 关键学习点：工作流可以结合条件边、共享状态和结构化输出来实现循环，
/// 从而实现健壮的智能体决策。
/// </summary>
/// <remarks>
/// 前置要求：
/// - 应先完成前面的基础示例。
/// - 必须已配置 Azure OpenAI 聊天补全部署。
/// </remarks>
public static class Program
{
    public const int MaxIterations = 3;

    private static async Task Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        Console.WriteLine("\n=== Writer-Critic 迭代工作流 ===\n");
        Console.WriteLine($"Writer 和 Critic 将最多迭代 {MaxIterations} 次，直到获得批准。\n");

        // 配置 Azure OpenAI 客户端
        string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
        IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential()).GetChatClient(deploymentName).AsIChatClient();

        // 创建用于内容生成和审查的执行器
        WriterExecutor writer = new(chatClient);
        CriticExecutor critic = new(chatClient);
        SummaryExecutor summary = new(chatClient);

        // 根据 Critic 的决策构建带条件路由的工作流
        WorkflowBuilder workflowBuilder = new WorkflowBuilder(writer)
            .AddEdge(writer, critic)
            .AddSwitch(critic, sw => sw
                .AddCase<CriticDecision>(cd => cd?.Approved == true, summary)
                .AddCase<CriticDecision>(cd => cd?.Approved == false, writer))
            .WithOutputFrom(summary);

        // 使用示例任务执行工作流
        // 如果内容未通过，工作流会回到 Writer；
        // 如果通过，则进入 Summary。状态跟踪可确保不会无限循环。
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("任务: 撰写一篇关于 AI 伦理的简短博客文章（200 字）");
        Console.WriteLine(new string('=', 80) + "\n");

        const string InitialTask = "撰写一篇关于 AI 伦理的 200 字博客文章。内容应深思熟虑且引人入胜。";

        Workflow workflow = workflowBuilder.Build();
        await ExecuteWorkflowAsync(workflow, InitialTask);

        Console.WriteLine("\n✅ 示例完成: Writer-Critic 迭代演示了条件工作流循环\n");
        Console.WriteLine("演示的关键概念:");
        Console.WriteLine("  ✓ 带有条件路由的迭代优化循环");
        Console.WriteLine("  ✓ 用于迭代跟踪的共享工作流状态");
        Console.WriteLine($"  ✓ 最大迭代次数限制 ({MaxIterations}) 以确保安全");
        Console.WriteLine("  ✓ 单个执行器中的多消息处理器");
        Console.WriteLine("  ✓ 流式支持与结构化输出\n");
    }

    private static async Task ExecuteWorkflowAsync(Workflow workflow, string input)
    {
        // 以流式模式执行，以便实时查看进度
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
        // 监听工作流事件
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent agentUpdate:
                    // 实时输出智能体的流式内容
                    if (!string.IsNullOrEmpty(agentUpdate.Update.Text))
                    {
                        Console.Write(agentUpdate.Update.Text);
                    }
                    break;
                case WorkflowOutputEvent output:
                    Console.WriteLine("\n\n" + new string('=', 80));
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✅ FINAL APPROVED CONTENT");
                    Console.ResetColor();
                    Console.WriteLine(new string('=', 80));
                    Console.WriteLine();
                    Console.WriteLine(output.Data);
                    Console.WriteLine();
                    Console.WriteLine(new string('=', 80));
                    break;
                case WorkflowErrorEvent workflowError:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "Unknown workflow error occurred.");
                    Console.ResetColor();
                    break;
                case ExecutorFailedEvent executorFailed:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Executor '{executorFailed.ExecutorId}' failed with {(executorFailed.Data == null ? "unknown error" : $"exception {executorFailed.Data}")}.");
                    Console.ResetColor();
                    break;
            }
        }
    }
}

// ====================================
// 用于迭代跟踪的共享状态
// ====================================

/// <summary>
/// 跟踪跨工作流执行的当前迭代次数和对话历史。
/// </summary>
internal sealed class FlowState
{
    public int Iteration { get; set; } = 1;
    public List<ChatMessage> History { get; } = [];
}

/// <summary>
/// 在工作流上下文中访问共享流程状态所使用的常量。
/// </summary>
internal static class FlowStateShared
{
    public const string Scope = "FlowStateScope";
    public const string Key = "singleton";
}

/// <summary>
/// 用于读取和写入共享流程状态的帮助方法。
/// </summary>
internal static class FlowStateHelpers
{
    public static async Task<FlowState> ReadFlowStateAsync(IWorkflowContext context)
    {
        FlowState? state = await context.ReadStateAsync<FlowState>(FlowStateShared.Key, scopeName: FlowStateShared.Scope);
        return state ?? new FlowState();
    }

    public static ValueTask SaveFlowStateAsync(IWorkflowContext context, FlowState state)
        => context.QueueStateUpdateAsync(FlowStateShared.Key, state, scopeName: FlowStateShared.Scope);
}

// ====================================
// 数据传输对象
// ====================================
/// <summary>
/// Critic 决策的结构化输出架构。
/// 使用 JsonPropertyName 和 Description 特性来定义 OpenAI 的 JSON 架构。
/// </summary>
[Description("Critic 的审查决策，包含批准状态和反馈")]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "通过 JSON 反序列化进行实例化")]
internal sealed class CriticDecision
{
    [JsonPropertyName("approved")]
    [Description("内容是否已获批准（true），或需要修改（false）")]
    public bool Approved { get; set; }

    [JsonPropertyName("feedback")]
    [Description("如果未获批准，则给出具体改进反馈；如果已批准，则为空")]
    public string Feedback { get; set; } = "";

    // 供工作流使用的非 JSON 属性
    [JsonIgnore]
    public string Content { get; set; } = "";

    [JsonIgnore]
    public int Iteration { get; set; }
}

// ====================================
// 自定义执行器
// ====================================

/// <summary>
/// 根据用户请求或Critic反馈创建或修订内容的执行器。
/// 该执行器演示了针对不同输入类型的多个消息处理器。
/// </summary>
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
    /// <summary>
    /// 处理来自用户的初始写作请求。
    /// </summary>
    [MessageHandler]
    public async ValueTask<ChatMessage> HandleInitialRequestAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return await this.HandleAsyncCoreAsync(new ChatMessage(ChatRole.User, message), context, cancellationToken);
    }

    /// <summary>
    /// 处理来自 Critic 且包含反馈的修订请求。
    /// </summary>
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

    /// <summary>
    /// 生成内容（初稿或修订稿）的核心实现。
    /// </summary>
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

/// <summary>
/// 审查内容并决定是批准还是要求修改的执行器。
/// 使用带流式输出的结构化结果来实现可靠决策。
/// </summary>
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

/// <summary>
/// 向用户展示最终已批准内容的执行器。
/// </summary>
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
}