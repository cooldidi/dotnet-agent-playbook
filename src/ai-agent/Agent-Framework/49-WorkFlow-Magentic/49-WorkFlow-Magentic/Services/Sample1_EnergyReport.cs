using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;
using OpenAI;

/// <summary>
/// 样例一：能源效率报告 - 使用Magentic工作流协调研究员和编码器
/// </summary>
public class Sample1_EnergyReport
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _modelId;

    // ✅ 修改构造函数：直接接收 OpenAIClient 和 modelId
    public Sample1_EnergyReport(OpenAIClient openAIClient, string modelId)
    {
        _openAIClient = openAIClient;
        _modelId = modelId;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("样例一：能源效率报告");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        // 任务定义
        const string taskPrompt =
            "我正在准备一份关于不同机器学习模型架构的能源效率的报告。 " +
            "比较 ResNet-50、BERT-base 和 GPT-2 在标准数据集上的估计训练和推理能耗（例如，ResNet 使用 ImageNet，" +
            "BERT 使用 GLUE，GPT-2 使用 WebText）。 " +
            "然后，估算每个模型在 Azure Standard_NC6s_v3 虚拟机上训练 24 小时的 CO2 排放量。" +
            "提供清晰的表格，并推荐每种任务类型（图像分类、文本分类和文本生成）最节能的模型。用中文输出";

        // 创建AI客户端
        var client = _openAIClient.GetChatClient(_modelId).AsIChatClient();

        // 创建Agent
        var managerAgent = new ChatClientAgent(
            client,
            "你协调团队以高效完成复杂任务",
            "MagenticManager",
            "协调研究员和编码器的工作流程的组织者。");

        var researcherAgent = new ChatClientAgent(
            client,
            "你专注于研究和信息收集",
            "ResearcherAgent",
            "专注于研究和信息收集的专家。");

        var coderAgent = new ChatClientAgent(
            client,
            "你解决定量问题，通过编写和运行代码。清楚地展示分析和计算过程",
            "CoderAgent",
            "一个能够编写并执行代码以分析数据的得力助手。",
            tools: [new HostedCodeInterpreterTool()]);

        var translatorAgent = new ChatClientAgent(
            client,
            "你将研究员和编码器的对话翻译成中文，如果没有传入任何数据的时候不要返回任何值",
            "TranslatorAgent",
            "将研究员和编码器的对话翻译成中文的翻译者。");

        // 构建工作流
        Workflow workflow = new MagenticWorkflowBuilder(managerAgent)
            .AddParticipants([researcherAgent, coderAgent])
            .WithName("Magentic 协作流程")
            .WithDescription("协作协调研究员和编码器以解决复杂分析任务。")
            .RequirePlanSignoff(false)
            .WithMaxRounds(10)
            .WithMaxStalls(3)
            .WithMaxResets(2)
            .Build();

        Console.WriteLine("构建 Magentic 工作流...");
        Console.WriteLine();
        Console.WriteLine($"任务: {taskPrompt}");
        Console.WriteLine();
        Console.WriteLine("开始执行工作流...");
        Console.WriteLine();

        // 执行工作流
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new List<ChatMessage> { new(ChatRole.User, taskPrompt) });

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? lastResponseId = null;
        WorkflowOutputEvent? finalOutput = null;

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync())
        {
            switch (workflowEvent)
            {
                case AgentResponseUpdateEvent updateEvent:
                    AgentHelper.WriteStreamingUpdate(updateEvent, ref lastResponseId);
                    break;

                case MagenticPlanCreatedEvent planCreated:
                    await AgentHelper.WriteMagenticMessage("初始计划", planCreated.FullTaskLedger.Text, translatorAgent);
                    AgentHelper.PauseIfInteractive();
                    break;

                case MagenticReplannedEvent replanned:
                    await AgentHelper.WriteMagenticMessage("重新计划", replanned.FullTaskLedger.Text, translatorAgent);
                    AgentHelper.PauseIfInteractive();
                    break;

                case MagenticProgressLedgerUpdatedEvent progressUpdated:
                    await AgentHelper.WriteMagenticMessage("进度账本", AgentHelper.FormatProgressLedger(progressUpdated.ProgressLedger), translatorAgent);
                    AgentHelper.PauseIfInteractive();
                    break;

                case WorkflowOutputEvent outputEvent when outputEvent.Is<List<ChatMessage>>():
                    finalOutput = outputEvent;
                    break;

                case WorkflowErrorEvent workflowError:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "未知的工作流错误发生。");
                    Console.ResetColor();
                    break;

                case ExecutorFailedEvent executorFailed:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"执行器 '{executorFailed.ExecutorId}' 失败，原因: {(executorFailed.Data is null ? "未知错误" : $"异常 {executorFailed.Data}")}.");
                    Console.ResetColor();
                    break;
            }
        }

        // 输出最终结果
        if (finalOutput?.As<List<ChatMessage>>() is { } transcript)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine();
            Console.WriteLine("最终对话记录:");
            Console.WriteLine();

            foreach (ChatMessage message in transcript)
            {
                Console.WriteLine($"{message.AuthorName ?? message.Role.ToString()}: {message.Text}");
                Console.WriteLine();
            }
        }
    }
}