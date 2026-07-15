// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

// This sample shows how to use dependency injection to register an AIAgent and use it from a hosted service with a user input chat loop.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// 创建一个主机生成器，我们将使用它来注册服务然后运行。
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

const string instructions = "你是一位江湖说书人，擅长用幽默、接地气的方式讲笑话和故事。";
// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
// 给代理添加Options
builder.Services.AddSingleton(new ChatClientAgentOptions()
{
    Name = "Joker",
    ChatOptions = new()
    {
        Instructions = instructions
    }
});
// 添加聊天客户端到服务集合中。
builder.Services.AddKeyedChatClient("AzureOpenAI",
    (sp) => new AzureOpenAIClient(
        new Uri(endpoint),
        new AzureCliCredential())
            .GetChatClient(deploymentName)
            .AsIChatClient());

// 添加AI代理到服务集合中。
builder.Services.AddSingleton<AIAgent>((sp) =>
    new ChatClientAgent(
        chatClient: sp.GetRequiredKeyedService<IChatClient>("AzureOpenAI"),
        options: sp.GetRequiredService<ChatClientAgentOptions>())
    );
// 添加一个Sample服务，它将使用代理来响应用户输入。
builder.Services.AddHostedService<SampleService>();

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================

//var foundryAgent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
//    .AsAIAgent(model: deploymentName, name: "Joker", instructions: instructions);

//builder.Services.AddSingleton(foundryAgent);
//builder.Services.AddHostedService<SampleService>();


// 构建和运行主机。
using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

internal sealed class SampleService(AIAgent agent, IHostApplicationLifetime appLifetime) : IHostedService
{
    private AgentSession? _session;
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        this._session = await agent.CreateSessionAsync(cancellationToken);
        _ = this.RunAsync(appLifetime.ApplicationStopping);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {

        await Task.Delay(100, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("\n代理：帮我讲一个发生在茶馆的故事。要退出，请按Ctrl+C或直接回车。\n");
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                appLifetime.StopApplication();
                break;
            }

            await foreach (var update in agent.RunStreamingAsync(input, this._session, cancellationToken: cancellationToken))
            {
                Console.Write(update);
            }

            Console.WriteLine();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}