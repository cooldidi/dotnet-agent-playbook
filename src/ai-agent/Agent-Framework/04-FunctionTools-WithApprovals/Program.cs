#pragma warning disable MEAI001
// Copyright (c) Microsoft. All rights reserved.
// This sample demonstrates how to use a ChatClientAgent with function tools that require a human in the loop for approvals.
// It shows both non-streaming and streaming agent interactions using menu-related tools.
// If the agent is hosted in a service, with a remote user, combine this sample with the Persisted Conversations sample to persist the chat history
// while the agent is waiting for user input.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";


// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent openAIAgent = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(instructions: "你是一个乐于助人的助手", tools: [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(GetNews))]);

AgentSession openAISession = await openAIAgent.CreateSessionAsync();
var openAIResponse = await openAIAgent.RunAsync("美国的最新新闻是什么？", openAISession);
List<ToolApprovalRequestContent> openAIApprovalRequests = openAIResponse.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();

while (openAIApprovalRequests.Count > 0)
{
    List<ChatMessage> userInputResponses = openAIApprovalRequests
        .ConvertAll(functionApprovalRequest =>
        {
            Console.WriteLine($"代理想调用以下函数，请回复 Y 以批准：Name {((FunctionCallContent)functionApprovalRequest.ToolCall).Name}");
            return new ChatMessage(ChatRole.User, [functionApprovalRequest.CreateResponse(Console.ReadLine()?.Equals("Y", StringComparison.OrdinalIgnoreCase) ?? false)]);
        });
    openAIResponse = await openAIAgent.RunAsync(userInputResponses, openAISession);
    openAIApprovalRequests = openAIResponse.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
}
Console.WriteLine($"\nAgent: {openAIResponse}");

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================

AIAgent foundryAgent = new AIProjectClient(
    new Uri(endpoint),
    new DefaultAzureCredential())
    .AsAIAgent(model: deploymentName, instructions: "你是一个乐于助人的助手", tools: [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(GetNews))]);

AgentSession foundrySession = await foundryAgent.CreateSessionAsync();
AgentResponse foundryResponse = await foundryAgent.RunAsync("美国的最新新闻是什么？", foundrySession);
List<ToolApprovalRequestContent> foundryApprovalRequests = foundryResponse.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();


// 使用流式输出:
// var updates = await agent.RunStreamingAsync("美国的最新新闻是什么？", session).ToListAsync();
// approvalRequests = updates.SelectMany(x => x.Contents).OfType<ToolApprovalRequestContent>().ToList();
while (foundryApprovalRequests.Count > 0)
{
    List<ChatMessage> userInputResponses = foundryApprovalRequests
        .ConvertAll(functionApprovalRequest =>
        {
            Console.WriteLine($"代理想调用以下函数，请回复 Y 以批准：Name {((FunctionCallContent)functionApprovalRequest.ToolCall).Name}");
            return new ChatMessage(ChatRole.User, [functionApprovalRequest.CreateResponse(Console.ReadLine()?.Equals("Y", StringComparison.OrdinalIgnoreCase) ?? false)]);
        });
    // 将用户输入的响应传递回代理以进行进一步处理。
    foundryResponse = await foundryAgent.RunAsync(userInputResponses, foundrySession);
    foundryApprovalRequests = foundryResponse.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();

    // 使用流式输出:
    // updates = await agent.RunStreamingAsync(userInputResponses, session).ToListAsync();
    // approvalRequests = updates.SelectMany(x => x.Contents).OfType<ToolApprovalRequestContent>().ToList();
}

Console.WriteLine($"\nAgent: {foundryResponse}");

Console.ReadLine();

[Description("获取指定国家的最新新闻标题。")]
static string GetNews([Description("国家名称。")] string country)
    => $"来自 {country} 的头条新闻：AI 正在革新软件开发领域。";
