// Copyright (c) Microsoft. All rights reserved.
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using System.Diagnostics;
using System.Text;
using System.Text.Json;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// --- Configuration ---
string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";



var skillsProvider = new AgentSkillsProvider(
    Path.Combine(AppContext.BaseDirectory, "skills"),
    RunAsync);
#pragma warning disable MAAI001
AIAgent agent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "UnitConverterAgent",
        ChatOptions = new()
        {
            ModelId = deploymentName,
            Instructions = "你是一个可以调用工具进行单位转换的助手。",
        },
        AIContextProviders = [skillsProvider],
    })
    .AsBuilder()
    .UseToolApproval(new ToolApprovalAgentOptions
    {
        // 注意：为了简化演示，本示例会自动批准所有 Skill 工具的调用。
        // 在实际生产环境中，应在执行脚本之前先向用户请求授权。
         AutoApprovalRules = [AgentSkillsProvider.AllToolsAutoApprovalRule],
    })
    .Build();

// --- Example: Unit conversion ---
Console.WriteLine("正在使用基于文件的技能进行单位转换");
Console.WriteLine(new string('-', 60));

AgentResponse response = await agent.RunAsync(
    "请严格用脚本计算。马拉松（26.2 英里）等于多少公里？另外，75 千克等于多少磅？");

Console.WriteLine($"Agent: {response.Text}");

static async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
{
    if (!File.Exists(script.FullPath))
    {
        return $"错误: 找不到脚本文件: {script.FullPath}";
    }

    string extension = Path.GetExtension(script.FullPath);
    string? interpreter = extension switch
    {
        ".py" => "python3",
        ".js" => "node",
        ".sh" => "bash",
        ".ps1" => "pwsh",
        _ => null,
    };

    var startInfo = new ProcessStartInfo
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(script.FullPath) ?? ".",
    };

    if (interpreter is not null)
    {
        startInfo.FileName = interpreter;
        startInfo.ArgumentList.Add(script.FullPath);
    }
    else
    {
        startInfo.FileName = script.FullPath;
    }

    if (arguments is { ValueKind: JsonValueKind.Array } json)
    {
        // Positional CLI arguments
        foreach (var element in json.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"错误: 文件型技能脚本只接受字符串类型的命令行参数，但收到的 JSON 元素类型为 '{element.ValueKind}'。 " +
                    "所有数组元素必须是 JSON 字符串。");
            }
            startInfo.ArgumentList.Add(element.GetString()!);
        }
    }
    else if (arguments is not null && arguments.Value.ValueKind != JsonValueKind.Null && arguments.Value.ValueKind != JsonValueKind.Undefined)
    {
        throw new InvalidOperationException(
            $"错误: 预期一个 JSON 数组作为命令行参数，但收到的类型为 {arguments.Value.ValueKind}。 " +
            "文件型技能脚本期望位置参数为 JSON 字符串数组。");
    }

    Process? process = null;
    try
    {
        process = Process.Start(startInfo);
        if (process is null)
        {
            return $"错误: 无法启动脚本 '{script.Name}' 的进程。";
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);

        if (!string.IsNullOrEmpty(error))
        {
            output += $"\n标准错误输出:\n{error}";
        }

        if (process.ExitCode != 0)
        {
            output += $"\n脚本以代码 {process.ExitCode} 退出";
        }

        return string.IsNullOrEmpty(output) ? "(无输出)" : output.Trim();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Kill the process on cancellation to avoid leaving orphaned subprocesses.
        process?.Kill(entireProcessTree: true);
        throw;
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        return $"错误: 执行脚本 '{script.Name}' 失败: {ex.Message}";
    }
    finally
    {
        process?.Dispose();
    }
}


