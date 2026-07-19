using System.Text;
using OpenAI;
using System.ClientModel;

// 设置控制台编码
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// 读取配置
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var modelId = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_ApiKey") ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

// 创建OpenAI客户端
var openAiClient = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions
    {
        Endpoint = new Uri(endpoint)
    }
);

// 主菜单
while (true)
{
    Console.Clear();
    Console.WriteLine("=".PadRight(60, '='));
    Console.WriteLine("Magentic 工作流示例");
    Console.WriteLine("=".PadRight(60, '='));
    Console.WriteLine();
    Console.WriteLine("请选择要运行的样例:");
    Console.WriteLine("  1. 能源效率报告 (Magentic 协作流程)");
    Console.WriteLine("  2. API性能分析");
    Console.WriteLine("  3. 全部运行 (先1后2)");
    Console.WriteLine("  0. 退出");
    Console.WriteLine();
    Console.Write("请输入选择 (0-3): ");

    var choice = Console.ReadLine();
    Console.WriteLine();

    try
    {
        switch (choice)
        {
            case "1":
                var sample1 = new Sample1_EnergyReport(openAiClient, modelId);
                await sample1.RunAsync();
                break;

            case "2":
                var sample2 = new Sample2_PerformanceAnalysis(openAiClient, modelId);
                await sample2.RunAsync();
                break;

            case "3":
                var s1 = new Sample1_EnergyReport(openAiClient, modelId);
                await s1.RunAsync();

                Console.WriteLine();
                Console.WriteLine(new string('=', 100));
                Console.WriteLine("按回车键继续运行样例二...");
                Console.ReadLine();
                Console.WriteLine();

                var s2 = new Sample2_PerformanceAnalysis(openAiClient, modelId);
                await s2.RunAsync();
                break;

            case "0":
                Console.WriteLine("退出程序");
                return;

            default:
                Console.WriteLine("无效选择，请重新输入");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"执行失败: {ex.Message}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"内部错误: {ex.InnerException.Message}");
        }
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.WriteLine("按回车键返回主菜单...");
    Console.ReadLine();
}