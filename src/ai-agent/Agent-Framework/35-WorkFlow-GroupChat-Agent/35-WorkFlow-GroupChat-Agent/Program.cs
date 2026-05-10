// Copyright (c) Microsoft. All rights reserved.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace WorkflowAgentsInWorkflowsSample;

/// <summary>
/// 本示例演示如何在工作流中将 AI 代理作为执行器使用，
/// 并通过 <see cref="AgentWorkflowBuilder"/> 将这些代理组合成几种常见模式之一。
/// </summary>
/// <remarks>
/// 前置条件：
/// - 必须已配置 Azure OpenAI 聊天补全部署。
/// </remarks>
public static class Program
{
    private static async Task Main()
    {

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        // 配置 Azure OpenAI 客户端。
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("未设置 AZURE_OPENAI_ENDPOINT。");
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential()).GetChatClient(deploymentName).AsIChatClient();

        var translationAgents = from lang in new[] { "法语", "中文", "西班牙语", "英语" } select GetTranslationAgent(lang, client);

        await RunWorkflowAsync(
            AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 5 })
                .AddParticipants(translationAgents)
                .WithName("翻译轮询工作流")
                .WithDescription("一个由四个翻译代理按轮询方式依次响应的工作流。")
                .Build(),
            [new(ChatRole.User, "Hello，World！")]);
    }

    static async Task<List<ChatMessage>> RunWorkflowAsync(Workflow workflow, List<ChatMessage> messages)
    {
        string? lastExecutorId = null;

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is AgentResponseUpdateEvent e)
            {
                if (e.ExecutorId != lastExecutorId)
                {
                    lastExecutorId = e.ExecutorId;
                    Console.WriteLine();
                    Console.WriteLine(e.ExecutorId);
                }

                Console.Write(e.Update.Text);
                if (e.Update.Contents.OfType<FunctionCallContent>().FirstOrDefault() is FunctionCallContent call)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  [正在调用函数“{call.Name}”，参数：{JsonSerializer.Serialize(call.Arguments)}]");
                }
            }
            else if (evt is WorkflowOutputEvent output)
            {
                Console.WriteLine();
                return output.As<List<ChatMessage>>()!;
            }
            else if (evt is WorkflowErrorEvent workflowError)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "发生未知工作流错误。");
                Console.ResetColor();
            }
            else if (evt is ExecutorFailedEvent executorFailed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"执行器“{executorFailed.ExecutorId}”失败，{(executorFailed.Data == null ? "错误未知" : $"异常信息：{executorFailed.Data}")}。");
                Console.ResetColor();
            }
        }

        return [];
    }

    /// <summary>为指定目标语言创建一个翻译代理。</summary>
    private static ChatClientAgent GetTranslationAgent(string targetLanguage, IChatClient chatClient) =>
        new(chatClient,
            @$"你是一个翻译助手。请将用户输入翻译为：{targetLanguage}。只输出翻译结果，不要输出解释或语言名称。");
}