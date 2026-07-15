// Copyright (c) Microsoft. All rights reserved.

// This sample ports the Python Magentic orchestration sample to .NET.
// A Magentic workflow coordinates a researcher and a coder, streams orchestration
// events as the plan evolves, and prints the final conversation transcript.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;
using System.Text;

const string TaskPrompt =
   "我正在准备一份关于不同机器学习模型架构的能源效率的报告。 " +
   "比较 ResNet-50、BERT-base 和 GPT-2 在标准数据集上的估计训练和推理能耗（例如，ResNet 使用 ImageNet，BERT 使用 GLUE，GPT-2 使用 WebText）。 " +
   "然后，估算每个模型在 Azure Standard_NC6s_v3 虚拟机上训练 24 小时的 CO2 排放量。提供清晰的表格，并推荐每种任务类型（图像分类、文本分类和文本生成）最节能的模型。用中文输出";


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
// 配置 Azure OpenAI 客户端。
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("未设置 AZURE_OPENAI_ENDPOINT。");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";


var client = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential()).GetChatClient(deploymentName).AsIChatClient();

var researcherAgent = new ChatClientAgent(client, "你专注于研究和信息收集", "ResearcherAgent", "专注于研究和信息收集的专家。");

var coderAgent = new ChatClientAgent(client, "你解决定量问题，通过编写和运行代码。清楚地展示分析和计算过程", "CoderAgent", "一个能够编写并执行代码以分析数据的得力助手。", tools: [new HostedCodeInterpreterTool()]);

var managerAgent = new ChatClientAgent(client, "你协调团队以高效完成复杂任务", "MagenticManager", "协调研究员和编码器的工作流程的组织者。");

var translatorAgent = new ChatClientAgent(client, "你将研究员和编码器的对话翻译成中文，如果没有传入任何数据的时候不要返回任何值", "TranslatorAgent", "将研究员和编码器的对话翻译成中文的翻译者。");


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
Console.WriteLine($"任务: {TaskPrompt}");
Console.WriteLine();
Console.WriteLine("开始执行工作流...");

await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
    workflow,
    new List<ChatMessage> { new(ChatRole.User, TaskPrompt) });

await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

string? lastResponseId = null;
WorkflowOutputEvent? finalOutput = null;

await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync())
{
    switch (workflowEvent)
    {
        case AgentResponseUpdateEvent updateEvent:
            WriteStreamingUpdate(updateEvent, ref lastResponseId);
            break;

        case MagenticPlanCreatedEvent planCreated:
            WriteMagenticMessage("初始计划", planCreated.FullTaskLedger.Text, translatorAgent);
            PauseIfInteractive();
            break;

        case MagenticReplannedEvent replanned:
            WriteMagenticMessage("重新计划", replanned.FullTaskLedger.Text, translatorAgent);
            PauseIfInteractive();
            break;

        case MagenticProgressLedgerUpdatedEvent progressUpdated:
            WriteMagenticMessage("进度账本", FormatProgressLedger(progressUpdated.ProgressLedger), translatorAgent);
            PauseIfInteractive();
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
static void WriteStreamingUpdate(AgentResponseUpdateEvent updateEvent, ref string? lastResponseId)
{
    string responseId = updateEvent.Update.ResponseId ?? updateEvent.Update.MessageId ?? updateEvent.ExecutorId;
    if (!string.Equals(responseId, lastResponseId, StringComparison.Ordinal))
    {
        if (lastResponseId is not null)
        {
            Console.WriteLine();
            Console.WriteLine();
        }

        Console.Write($"- {updateEvent.ExecutorId}: ");
        lastResponseId = responseId;
    }

    if (!string.IsNullOrEmpty(updateEvent.Update.Text))
    {
        Console.Write(updateEvent.Update.Text);
    }
}

static void WriteMagenticMessage(string title, string? content, ChatClientAgent translatorAgent)
{
    Console.WriteLine();
    Console.WriteLine($"[Magentic {title}]");
    Console.WriteLine(translatorAgent.RunAsync(new ChatMessage(ChatRole.User, content)).Result.Text);
}

static string FormatProgressLedger(MagenticProgressLedger ledger) =>
   string.Join(Environment.NewLine,
       $"请求满足: {ledger.IsRequestSatisfied}",
       $"循环中: {ledger.IsInLoop}",
       $"正在取得进展: {ledger.IsProgressBeingMade}",
       $"下一个发言者: {ledger.NextSpeaker}",
       $"指令: {ledger.InstructionOrQuestion}");

static void PauseIfInteractive()
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        return;
    }

    Console.Write("按回车键继续...");
    Console.ReadLine();
    Console.WriteLine();
}