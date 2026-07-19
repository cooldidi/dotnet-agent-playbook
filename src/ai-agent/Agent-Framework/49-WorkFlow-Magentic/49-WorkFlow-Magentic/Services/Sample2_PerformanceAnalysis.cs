using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;
using OpenAI;

/// <summary>
/// 样例二：API性能分析 - 使用Magentic工作流协调性能分析团队
/// </summary>
public class Sample2_PerformanceAnalysis
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _modelId;

    // ✅ 修改构造函数：直接接收 OpenAIClient 和 modelId
    public Sample2_PerformanceAnalysis(OpenAIClient openAIClient, string modelId)
    {
        _openAIClient = openAIClient;
        _modelId = modelId;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("样例二：API性能分析");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        // 任务定义
        string taskPrompt =
            "以下是最近24小时的API性能数据：\n" +
            "- /api/orders: 平均响应时间 850ms, P95: 2.3s, 请求量: 45,000\n" +
            "- /api/products: 平均响应时间 120ms, P95: 300ms, 请求量: 120,000\n" +
            "- /api/users: 平均响应时间 650ms, P95: 1.8s, 请求量: 38,000\n" +
            "- /api/search: 平均响应时间 1.2s, P95: 4.5s, 请求量: 15,000\n" +
            "分析性能瓶颈，给出优化建议和优先级排序。用中文输出";

        // 创建AI客户端
        var client = _openAIClient.GetChatClient(_modelId).AsIChatClient();

        // 创建Agent - 性能分析团队
        var perfManager = new ChatClientAgent(
            client,
            "你协调性能分析流程。重要：所有输出、计划、分析都必须使用中文。",
            "PerfManager",
            "性能分析协调者");

        var perfAnalyzer = new ChatClientAgent(
            client,
            "你精通性能分析，能识别慢查询、缓存问题和资源瓶颈。重要：用中文输出",
            "PerfAnalyzer",
            "性能分析专家");

        var optimizer = new ChatClientAgent(
            client,
            "你擅长性能优化，能提出具体的代码改进方案。重要：用中文输出",
            "Optimizer",
            "性能优化工程师");

        var dataProcessor = new ChatClientAgent(
            client,
            "你擅长用C#进行数据处理和统计分析。重要：用中文输出",
            "DataProcessor",
            "数据分析师",
            tools: [new HostedCodeInterpreterTool()]);
        var translatorAgent = new ChatClientAgent(
            client,
            "你将英文内容翻译成中文，如果没有传入任何数据的时候不要返回任何值",
            "TranslatorAgent",
            "翻译者");

        // 构建工作流
        Workflow workflow = new MagenticWorkflowBuilder(perfManager)
            .AddParticipants([perfAnalyzer, optimizer, dataProcessor,translatorAgent])
            .WithName("API性能分析流程")
            .WithDescription("自动化性能瓶颈分析")
            .RequirePlanSignoff(false)
            .WithMaxRounds(12)
            .Build();

        Console.WriteLine($"任务: {taskPrompt}");
        Console.WriteLine();
        Console.WriteLine("开始执行工作流...");
        Console.WriteLine();

        // 执行工作流
        await using StreamingRun run2 = await InProcessExecution.RunStreamingAsync(
            workflow,
            new List<ChatMessage> { new(ChatRole.User, taskPrompt) });

        await run2.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? lastResponseId = null;
        WorkflowOutputEvent? finalOutput = null;

        // 监听执行过程中的所有事件
        await foreach (WorkflowEvent workflowEvent in run2.WatchStreamAsync())
        {
            //Console.WriteLine($"[调试] 收到事件: {workflowEvent.GetType().Name}");

            switch (workflowEvent)
            {
                case AgentResponseUpdateEvent updateEvent:
                    AgentHelper.WriteStreamingUpdate(updateEvent, ref lastResponseId);
                    break;

                case MagenticPlanCreatedEvent planCreated:
                    Console.WriteLine();
                    Console.WriteLine($"[计划] {planCreated.FullTaskLedger.Text}");
                    var translated = await translatorAgent.RunAsync(
                        new ChatMessage(ChatRole.User, $"[计划] {planCreated.FullTaskLedger.Text}"));
                    Console.WriteLine(translated.Text);
                    break;

                case MagenticReplannedEvent replanned:
                    Console.WriteLine();
                    Console.WriteLine($"[重新计划] {replanned.FullTaskLedger.Text}");
                    break;
              
                case MagenticProgressLedgerUpdatedEvent progressUpdated:
                    Console.WriteLine();
                    Console.WriteLine($"进度更新:");
                    Console.WriteLine($"  请求已满足: {progressUpdated.ProgressLedger.IsRequestSatisfied}");
                    Console.WriteLine($"  正在循环: {progressUpdated.ProgressLedger.IsInLoop}");
                    Console.WriteLine($"  正在取得进展: {progressUpdated.ProgressLedger.IsProgressBeingMade}");
                    Console.WriteLine($"  下一个发言者: {progressUpdated.ProgressLedger.NextSpeaker}");
                    Console.WriteLine($"  指令: {progressUpdated.ProgressLedger.InstructionOrQuestion}");
                    break;

                
                case WorkflowOutputEvent outputEvent:
                    Console.WriteLine($"[调试] WorkflowOutputEvent 触发，类型: {outputEvent.Data?.GetType()?.Name ?? "null"}");
                    if (outputEvent.Is<List<ChatMessage>>())
                    {
                        finalOutput = outputEvent;
                        Console.WriteLine("✅ 最终输出已保存（List<ChatMessage>）");
                    }
                    else
                    {
                        Console.WriteLine($"输出内容: {outputEvent.Data}");
                    }
                    break;

                case WorkflowErrorEvent workflowError:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ 工作流错误: {workflowError.Exception?.Message}");
                    Console.WriteLine($"堆栈: {workflowError.Exception?.StackTrace}");
                    Console.ResetColor();
                    break;

                case ExecutorFailedEvent executorFailed:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ 执行器失败: {executorFailed.ExecutorId}");
                    Console.WriteLine($"数据: {executorFailed.Data}");
                    Console.ResetColor();
                    break;
                /*

[未知事件] WorkflowStartedEvent
[未知事件] SuperStepStartedEvent
[未知事件] ExecutorInvokedEvent
[未知事件] ExecutorCompletedEvent
[未知事件] ExecutorInvokedEvent
[未知事件] ExecutorCompletedEvent
[未知事件] SuperStepCompletedEvent
[未知事件] SuperStepStartedEvent
[未知事件] ExecutorInvokedEvent
[未知事件] ExecutorCompletedEvent
[未知事件] ExecutorInvokedEvent
[未知事件] ExecutorCompletedEvent
[未知事件] ExecutorInvokedEvent
[未知事件] ExecutorCompletedEvent
[未知事件] SuperStepCompletedEvent
                 */
                default:
                    Console.WriteLine($"[未知事件] {workflowEvent.GetType().Name}");
                    break;
            }
        }

        // 输出最终结果
        if (finalOutput?.As<List<ChatMessage>>() is { } transcript)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine("最终性能分析报告:");
            Console.WriteLine();

            foreach (ChatMessage message in transcript)
            {
                Console.WriteLine($"{message.AuthorName ?? message.Role.ToString()}: {message.Text}");
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("⚠️ 没有收到最终输出，查看上方的调试信息");
        }
    }
}