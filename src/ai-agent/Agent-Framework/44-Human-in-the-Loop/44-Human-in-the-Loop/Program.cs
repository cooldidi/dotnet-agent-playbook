// Copyright (c) Microsoft. All rights reserved.

using _44_Human_in_the_Loop;
using Microsoft.Agents.AI.Workflows;
using System.Text;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

//创建工作流
var workflow = WorkflowFactory.BuildWorkflow();

// 执行工作流
await using StreamingRun handle = await InProcessExecution.RunStreamingAsync(workflow, NumberSignal.Init);
await foreach (WorkflowEvent evt in handle.WatchStreamAsync())
{
    switch (evt)
    {
        case RequestInfoEvent requestInputEvt:
            // 处理来自工作流的 `RequestInfoEvent`
            ExternalResponse response = HandleExternalRequest(requestInputEvt.Request);
            await handle.SendResponseAsync(response);
            break;

        case WorkflowOutputEvent outputEvt:
            // 工作流已生成输出
            Console.WriteLine($"工作流完成，结果为: {outputEvt.Data}");
            return;

        case WorkflowErrorEvent workflowError:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "发生未知的工作流错误。");
            Console.ResetColor();
            return;

        case ExecutorFailedEvent executorFailed:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"执行器 '{executorFailed.ExecutorId}' 失败，原因: {(executorFailed.Data == null ? "未知错误" : $"异常 {executorFailed.Data}")}.");
            Console.ResetColor();
            return;
    }
}
static ExternalResponse HandleExternalRequest(ExternalRequest request)
{
    if (request.TryGetDataAs<NumberSignal>(out var signal))
    {
        switch (signal)
        {
            case NumberSignal.Init:
                int initialGuess = ReadIntegerFromConsole("请输入您的初始猜测: ");
                return request.CreateResponse(initialGuess);
            case NumberSignal.Above:
                int lowerGuess = ReadIntegerFromConsole("您之前的猜测过大。请输入一个新的猜测: ");
                return request.CreateResponse(lowerGuess);
            case NumberSignal.Below:
                int higherGuess = ReadIntegerFromConsole("您之前的猜测过小。请输入一个新的猜测: ");
                return request.CreateResponse(higherGuess);
        }
    }

    throw new NotSupportedException($"请求 {request.PortInfo.RequestType} 不被支持");
}

static int ReadIntegerFromConsole(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int value))
        {
            return value;
        }
        Console.WriteLine("无效输入。请输入一个有效的整数。");
    }
}