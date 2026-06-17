// 版权所有 (c) Microsoft。保留所有权利。

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// 设置 Azure OpenAI 客户端
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";

var chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();
// 创建两个智能体：一个用于垃圾邮件检测，另一个用于邮件生成
AIAgent spamDetectionAgent = GetSpamDetectionAgent(chatClient);
AIAgent emailAssistantAgent = GetEmailAssistantAgent(chatClient);

// 创建执行器
var spamDetectionExecutor = new SpamDetectionExecutor(spamDetectionAgent);
var emailAssistantExecutor = new EmailAssistantExecutor(emailAssistantAgent);
var sendEmailExecutor = new SendEmailExecutor();
var handleSpamExecutor = new HandleSpamExecutor();

// 通过添加执行器并连接它们来构建工作流
var workflow = new WorkflowBuilder(spamDetectionExecutor)
    .AddEdge(spamDetectionExecutor, emailAssistantExecutor, condition: GetCondition(expectedResult: false))
    .AddEdge(emailAssistantExecutor, sendEmailExecutor)
    .AddEdge(spamDetectionExecutor, handleSpamExecutor, condition: GetCondition(expectedResult: true))
    .WithOutputFrom(handleSpamExecutor, sendEmailExecutor)
    .Build();

// 从文本文件中读取一封邮件
string email = Resources.Read("normal.txt");
// 执行工作流
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, new ChatMessage(ChatRole.User, email));

await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent outputEvent)
    {
        Console.WriteLine($"{outputEvent}");
    }
    else if (evt is WorkflowErrorEvent workflowError)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "Unknown workflow error occurred.");
        Console.ResetColor();
    }
    else if (evt is ExecutorFailedEvent executorFailed)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Executor '{executorFailed.ExecutorId}' failed with {(executorFailed.Data == null ? "unknown error" : $"exception {executorFailed.Data}")}.");
        Console.ResetColor();
    }
}

static Func<object?, bool> GetCondition(bool expectedResult) =>
    detectionResult => detectionResult is DetectionResult result && result.IsSpam == expectedResult;

static ChatClientAgent GetSpamDetectionAgent(IChatClient chatClient) =>
    new(chatClient, new ChatClientAgentOptions()
    {
        ChatOptions = new()
        {
            Instructions = "你是一个垃圾邮件检测助手，负责识别垃圾邮件。用中文回答",
            ResponseFormat = ChatResponseFormat.ForJsonSchema<DetectionResult>()
        }
    });


static ChatClientAgent GetEmailAssistantAgent(IChatClient chatClient) =>
    new(chatClient, new ChatClientAgentOptions()
    {
        ChatOptions = new()
        {
            Instructions = "你是一个邮件助手，帮助用户专业地撰写邮件回复。用中文撰写",
            ResponseFormat = ChatResponseFormat.ForJsonSchema<EmailResponse>(),
        }
    });

internal static class EmailStateConstants
{
    public const string EmailStateScope = "EmailState";
}

public sealed class DetectionResult
{
    [JsonPropertyName("is_spam")]
    public bool IsSpam { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonIgnore]
    public string EmailId { get; set; } = string.Empty;
}

internal sealed class Email
{
    [JsonPropertyName("email_id")]
    public string EmailId { get; set; } = string.Empty;

    [JsonPropertyName("email_content")]
    public string EmailContent { get; set; } = string.Empty;
}

internal sealed class SpamDetectionExecutor : Executor<ChatMessage, DetectionResult>
{
    private readonly AIAgent _spamDetectionAgent;
    public SpamDetectionExecutor(AIAgent spamDetectionAgent) : base("SpamDetectionExecutor")
    {
        _spamDetectionAgent = spamDetectionAgent;
    }

    public override async ValueTask<DetectionResult> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var newEmail = new Email
        {
            EmailId = Guid.NewGuid().ToString("N"),
            EmailContent = message.Text
        };

        await context.QueueStateUpdateAsync(newEmail.EmailId, newEmail, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);
        var response = await _spamDetectionAgent.RunAsync(message, cancellationToken: cancellationToken);
        var detectionResult = JsonSerializer.Deserialize<DetectionResult>(response.Text);
        detectionResult!.EmailId = newEmail.EmailId;
        return detectionResult;
    }
}
public sealed class EmailResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
}

internal sealed class EmailAssistantExecutor : Executor<DetectionResult, EmailResponse>
{
    private readonly AIAgent _emailAssistantAgent;

    public EmailAssistantExecutor(AIAgent emailAssistantAgent) : base("EmailAssistantExecutor")
    {
        _emailAssistantAgent = emailAssistantAgent;
    }

    public override async ValueTask<EmailResponse> HandleAsync(DetectionResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.IsSpam)
        {
            throw new InvalidOperationException("执行器只能处理非垃圾邮件消息。");
        }

        // 从共享状态中检索邮件内容
        var email = await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken)
            ?? throw new InvalidOperationException("未找到邮件。");

        // 调用代理
        var response = await _emailAssistantAgent.RunAsync(email.EmailContent, cancellationToken: cancellationToken);
        var emailResponse = JsonSerializer.Deserialize<EmailResponse>(response.Text);

        return emailResponse!;
    }
}

[YieldsOutput(typeof(string))]
internal sealed class SendEmailExecutor() : Executor<EmailResponse>("SendEmailExecutor")
{
    /// <summary>
    /// 模拟发送邮件。
    /// </summary>
    public override async ValueTask HandleAsync(EmailResponse message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        await context.YieldOutputAsync($"邮件已发送: {message.Response}", cancellationToken);
}

[YieldsOutput(typeof(string))]
internal sealed class HandleSpamExecutor() : Executor<DetectionResult>("HandleSpamExecutor")
{
    public override async ValueTask HandleAsync(DetectionResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.IsSpam)
        {
            await context.YieldOutputAsync($"邮件被标记为垃圾邮件: {message.Reason}", cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("执行器只能处理垃圾邮件消息。");
        }
    }
}
