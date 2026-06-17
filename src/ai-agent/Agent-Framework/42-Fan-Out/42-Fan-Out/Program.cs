// Copyright (c) Microsoft. All rights reserved.

using _42_Fan_Out;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 这个例子介绍了多选路由，其中一个执行器可以触发多个下游执行器。
/// 扩展了前一个示例中的 switch-case 模式，工作流现在可以在满足特定条件时同时触发多个执行器。
///
/// 主要特性：
/// - 对于合法邮件：触发邮件助手（始终）+ 邮件摘要（如果邮件较长）
/// - 对于垃圾邮件：仅触发处理垃圾邮件执行器
/// - 对于不确定的邮件：仅触发处理不确定邮件执行器
/// - 数据库记录会同时发生在短邮件和摘要长邮件的情况下
///
/// 这种模式对于需要基于数据特征进行并行处理的工作流非常强大，
/// 例如触发不同的分析管道或多个通知系统。
/// </summary>
/// <remarks>
/// 前提条件：
/// - 应先完成基础示例。
/// - 本示例中使用共享状态在执行器之间持久化邮件数据。
/// </remarks>


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

const int LongEmailThreshold = 100;
// Set up the Azure OpenAI client
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
var chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential()).GetChatClient(deploymentName).AsIChatClient();

// 创建智能体
AIAgent emailAnalysisAgent = GetEmailAnalysisAgent(chatClient);
AIAgent emailAssistantAgent = GetEmailAssistantAgent(chatClient);
AIAgent emailSummaryAgent = GetEmailSummaryAgent(chatClient);

// 创建执行器
var emailAnalysisExecutor = new EmailAnalysisExecutor(emailAnalysisAgent);
var emailAssistantExecutor = new EmailAssistantExecutor(emailAssistantAgent);
var emailSummaryExecutor = new EmailSummaryExecutor(emailSummaryAgent);
var sendEmailExecutor = new SendEmailExecutor();
var handleSpamExecutor = new HandleSpamExecutor();
var handleUncertainExecutor = new HandleUncertainExecutor();
var databaseAccessExecutor = new DatabaseAccessExecutor();

// 构建工作流，通过添加执行器并连接它们
WorkflowBuilder builder = new(emailAnalysisExecutor);
builder.AddFanOutEdge(
    emailAnalysisExecutor,
    [
        handleSpamExecutor,
        emailAssistantExecutor,
        emailSummaryExecutor,
        handleUncertainExecutor,
    ],
    GetTargetAssigner()
)
// 邮件助手写入响应后，它将被发送到发送邮件执行器
.AddEdge(emailAssistantExecutor, sendEmailExecutor)
// 如果邮件较长或需要摘要，则保存分析结果到数据库
.AddEdge<AnalysisResult>(emailAnalysisExecutor, databaseAccessExecutor, condition: analysisResult => analysisResult?.EmailLength <= LongEmailThreshold)
// 如果邮件较长且需要摘要，则保存分析结果到数据库
.AddEdge(emailSummaryExecutor, databaseAccessExecutor)
.WithOutputFrom(handleUncertainExecutor, handleSpamExecutor, sendEmailExecutor);

var workflow = builder.Build();
string email = Resources.Read("email.txt");

await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, new ChatMessage(ChatRole.User, email));
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent outputEvent)
    {
        Console.WriteLine($"{outputEvent}");
    }
    else if (evt is DatabaseEvent databaseEvent)
    {
        Console.WriteLine($"{databaseEvent}");
    }
    else if (evt is WorkflowErrorEvent workflowError)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "未知的工作流错误发生。");
        Console.ResetColor();
    }
    else if (evt is ExecutorFailedEvent executorFailed)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"执行器 '{executorFailed.ExecutorId}' 失败，原因: {(executorFailed.Data == null ? "未知错误" : $"异常 {executorFailed.Data}")}.");
        Console.ResetColor();
    }
}
static Func<AnalysisResult?,int, IEnumerable<int>> GetTargetAssigner()
{
    return (analysisResult, targetCount) =>
    {
        if (analysisResult is not null)
        {
            if (analysisResult.spamDecision == SpamDecision.Spam)
            {
                return [0]; // 路由到垃圾邮件处理器   
            }
            else if (analysisResult.spamDecision == SpamDecision.NotSpam)
            {
                List<int> targets = [1]; //路由到邮件助手

                if (analysisResult.EmailLength > LongEmailThreshold)
                {
                    targets.Add(2); // 路由到邮件摘要器
                }

                return targets;
            }
            else
            {
                return [3];
            }
        }
        throw new InvalidOperationException("无效的分析结果。");
    };
}

static ChatClientAgent GetEmailAnalysisAgent(IChatClient chatClient) =>
   new(chatClient, new ChatClientAgentOptions()
   {
       ChatOptions = new()
       {
           Instructions = "你是一个垃圾邮件检测助手，负责识别垃圾邮件。",
           ResponseFormat = ChatResponseFormat.ForJsonSchema<AnalysisResult>()
       }
   });

static ChatClientAgent GetEmailAssistantAgent(IChatClient chatClient) =>
    new(chatClient, new ChatClientAgentOptions()
    {
        ChatOptions = new()
        {
            Instructions = "你是一个邮件助手，帮助用户专业地起草邮件回复。",
            ResponseFormat = ChatResponseFormat.ForJsonSchema<EmailResponse>()
        }
    });

static ChatClientAgent GetEmailSummaryAgent(IChatClient chatClient) =>
    new(chatClient, new ChatClientAgentOptions()
    {
        ChatOptions = new()
        {
            Instructions = "You are an assistant that helps users summarize emails.",
            ResponseFormat = ChatResponseFormat.ForJsonSchema<EmailSummary>()
        }
    });

internal static class EmailStateConstants
{
    public const string EmailStateScope = "EmailState";
}

public enum SpamDecision
{
    NotSpam,
    Spam,
    Uncertain
}


public sealed class AnalysisResult
{
    [JsonPropertyName("spam_decision")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpamDecision spamDecision { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonIgnore]
    public int EmailLength { get; set; }

    [JsonIgnore]
    public string EmailSummary { get; set; } = string.Empty;

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

internal sealed class EmailAnalysisExecutor : Executor<ChatMessage, AnalysisResult>
{
    private readonly AIAgent _emailAnalysisAgent;

    public EmailAnalysisExecutor(AIAgent emailAnalysisAgent) : base("EmailAnalysisExecutor")
    {
        this._emailAnalysisAgent = emailAnalysisAgent;
    }

    public override async ValueTask<AnalysisResult> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var newEmail = new Email
        {
            EmailId = Guid.NewGuid().ToString("N"),
            EmailContent = message.Text
        };
        await context.QueueStateUpdateAsync(newEmail.EmailId, newEmail, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);
        var response = await this._emailAnalysisAgent.RunAsync(message, cancellationToken: cancellationToken);
        var analysisResult = JsonSerializer.Deserialize<AnalysisResult>(response.Text);

        analysisResult!.EmailId = newEmail.EmailId;
        analysisResult!.EmailLength = newEmail.EmailContent.Length;

        return analysisResult;
    }
}
public sealed class EmailResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
}
internal sealed class EmailAssistantExecutor : Executor<AnalysisResult, EmailResponse>
{
    private readonly AIAgent _emailAssistantAgent;

    public EmailAssistantExecutor(AIAgent emailAssistantAgent) : base("EmailAssistantExecutor")
    {
        this._emailAssistantAgent = emailAssistantAgent;
    }

    public override async ValueTask<EmailResponse> HandleAsync(AnalysisResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.spamDecision == SpamDecision.Spam)
        {
            throw new InvalidOperationException("This executor should only handle non-spam messages.");
        }

        var email = await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);

        var response = await this._emailAssistantAgent.RunAsync(email!.EmailContent, cancellationToken: cancellationToken);
        var emailResponse = JsonSerializer.Deserialize<EmailResponse>(response.Text);

        return emailResponse!;
    }
}

[YieldsOutput(typeof(string))]
internal sealed class SendEmailExecutor() : Executor<EmailResponse>("SendEmailExecutor")
{
    public override async ValueTask HandleAsync(EmailResponse message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        await context.YieldOutputAsync($"邮件已发送: {message.Response}", cancellationToken);
}

[YieldsOutput(typeof(string))]
internal sealed class HandleSpamExecutor() : Executor<AnalysisResult>("HandleSpamExecutor")
{
    public override async ValueTask HandleAsync(AnalysisResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.spamDecision == SpamDecision.Spam)
        {
            await context.YieldOutputAsync($"邮件标记为垃圾邮件: {message.Reason}", cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("这个执行器只应处理垃圾邮件消息。");
        }
    }
}

[YieldsOutput(typeof(string))]
internal sealed class HandleUncertainExecutor() : Executor<AnalysisResult>("HandleUncertainExecutor")
{

    public override async ValueTask HandleAsync(AnalysisResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (message.spamDecision == SpamDecision.Uncertain)
        {
            var email = await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);
            await context.YieldOutputAsync($"邮件标记为不确定: {message.Reason}. 邮件内容: {email?.EmailContent}", cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("这个执行器只应处理不确定的垃圾邮件决策。");
        }
    }
}

public sealed class EmailSummary
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

internal sealed class EmailSummaryExecutor : Executor<AnalysisResult, AnalysisResult>
{
    private readonly AIAgent _emailSummaryAgent;

    public EmailSummaryExecutor(AIAgent emailSummaryAgent) : base("EmailSummaryExecutor")
    {
        this._emailSummaryAgent = emailSummaryAgent;
    }

    public override async ValueTask<AnalysisResult> HandleAsync(AnalysisResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Read the email content from the shared states
        var email = await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);

        // Invoke the agent
        var response = await this._emailSummaryAgent.RunAsync(email!.EmailContent, cancellationToken: cancellationToken);
        var emailSummary = JsonSerializer.Deserialize<EmailSummary>(response.Text);
        message.EmailSummary = emailSummary!.Summary;

        return message;
    }
}

internal sealed class DatabaseEvent(string message) : WorkflowEvent(message) { }

internal sealed class DatabaseAccessExecutor() : Executor<AnalysisResult>("DatabaseAccessExecutor")
{
    public override async ValueTask HandleAsync(AnalysisResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // 1. 从共享状态中读取邮件内容
        await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);

        await Task.Delay(100, cancellationToken); // 模拟数据库访问延迟

        // 2. 保存分析结果
        await Task.Delay(100, cancellationToken); // 模拟数据库访问延迟

        // 不使用 `WorkflowCompletedEvent` 因为这不是工作流的结束。工作流的结束由 `SendEmailExecutor` 或 `HandleUnknownExecutor` 发出信号。
        // 结束工作流的信号由 `SendEmailExecutor` 或 `HandleUnknownExecutor` 发出。
        await context.AddEventAsync(new DatabaseEvent($"Email {message.EmailId} saved to database."), cancellationToken);
    }
}