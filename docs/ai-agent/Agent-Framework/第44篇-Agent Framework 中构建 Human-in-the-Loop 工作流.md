# Agent Framework 中构建 Human-in-the-Loop 工作流

在前面的文章中，我们介绍了顺序执行、条件边、Switch、Fan-Out 以及 Loop 等多种 Workflow 编排模式。

这些 Workflow 有一个共同特点：整个执行过程都发生在 Workflow 内部，节点之间通过消息不断传递，直到流程结束。

但在真实业务系统中，很多流程并不能完全依赖系统自动完成，而是需要等待用户或者外部系统参与。

对于这类场景，Workflow 需要具备一种能力：**执行过程中暂停，等待外部输入，然后继续执行。**

Agent Framework 为此提供了 **RequestPort**。

RequestPort 可以把 Workflow 与外部世界连接起来，使 Workflow 在需要时主动向外发起请求，并在收到响应后继续执行后续流程。

## 示例场景

我们继续沿用上一篇文章中的例子来演示 RequestPort 的工作方式。

系统预先保存了一个目标数字：

```text
42
```

Workflow 并不会自动生成猜测结果，而是把猜数字这个动作交给外部用户完成。

整个交互过程如下：

```text
Workflow
    ↓
RequestPort 请求输入
    ↓
用户输入数字
    ↓
JudgeExecutor 判断
    ↓
返回 Above / Below
    ↓
RequestPort 再次请求输入
```

如果用户猜测过大，Workflow 会提示重新输入更小的数字；如果猜测过小，则提示输入更大的数字。

整个过程会不断重复，直到用户猜中目标数字。

与上一篇 Loop Workflow 最大的区别在于：

- 上一篇文章中，循环是由 Workflow 内部两个 Executor 自动完成的。
- 而本示例中，每一次循环都需要等待外部用户参与，因此 Workflow 会在每一轮判断结束后暂停，直到收到新的输入后再继续执行。

## 核心代码实现

整个 Workflow 的定义非常简单：

```csharp
RequestPort numberRequestPort =
    RequestPort.Create<NumberSignal, int>("GuessNumber");

JudgeExecutor judgeExecutor = new(42);

return new WorkflowBuilder(numberRequestPort)
    .AddEdge(numberRequestPort, judgeExecutor)
    .AddEdge(judgeExecutor, numberRequestPort)
    .WithOutputFrom(judgeExecutor)
    .Build();
```

虽然只有几行代码，却构建了一个完整的人机交互工作流。

### RequestPort

首先创建的是：

```csharp
RequestPort.Create<NumberSignal, int>("GuessNumber")
```

这里创建了一个名为 `GuessNumber` 的 RequestPort。

与普通 Executor 不同，它本身并不会执行业务逻辑，而是作为 Workflow 的输入端口。

泛型参数表示：

- Workflow 向外发送 `NumberSignal`
- 外部返回 `int`

也就是说，当 Workflow 执行到 RequestPort 时，它并不会继续向下执行，而是主动向外发起一次请求。

## Workflow 如何等待外部输入

在前面的示例中，Workflow 的所有节点都会自动执行。一个 Executor 执行完成后，消息会继续传递给下一个 Executor，直到整个 Workflow 结束。

而本示例最大的不同在于，Workflow 并不知道用户会输入什么数字，因此执行到 RequestPort 时，无法继续向下执行。

此时，RequestPort 会主动向外部发起一次输入请求。

Workflow 启动后：

```csharp
await using StreamingRun handle =
    await InProcessExecution.RunStreamingAsync(workflow, NumberSignal.Init);
```

程序随后开始持续监听 Workflow 产生的各种事件：

```csharp
await foreach (WorkflowEvent evt in handle.WatchStreamAsync())
{
    ...
}
```

当 Workflow 执行到 RequestPort 时，并不会立即进入下一个节点，而是产生一个 `RequestInfoEvent`。

可以把它理解成：

> Workflow 正在向外部发送一个请求：“我现在需要用户输入，请获取数据后再继续执行。”

例如第一次运行时，就会产生如下请求： 

```text
请输入一个数字作为初始猜测：
```

此时，Workflow 会暂停执行，并等待外部返回结果。

这里需要注意的是，Workflow 并没有结束，而只是进入了等待状态。只有收到外部输入之后，它才会继续向下执行。

整个过程可以简单理解为：

```text
Workflow
    ↓
RequestPort 发起请求
    ↓
等待外部输入
```

### 外部如何让 Workflow 继续执行

当监听到 `RequestInfoEvent` 后，程序会进入下面这段代码：

```csharp
case RequestInfoEvent requestInputEvt:
    ExternalResponse response =
        HandleExternalRequest(requestInputEvt.Request);

    await handle.SendResponseAsync(response);
    break;
```

这里的 `HandleExternalRequest` 就代表了外部世界。

在当前示例中，它只是简单地读取控制台输入：

```csharp
ReadIntegerFromConsole(...)
```

随后，通过：

```csharp
request.CreateResponse(...)
```

将用户输入封装成 `ExternalResponse`，再发送回 Workflow：

```csharp
await handle.SendResponseAsync(response);
```

Workflow 收到 `ExternalResponse` 后，就会从刚才暂停的位置继续执行。

整个交互过程如下：

```text
Workflow
        │
        ▼
RequestPort 发起请求
        │
        ▼
用户输入数字
        │
        ▼
创建 ExternalResponse
        │
        ▼
Workflow 恢复执行
```

虽然本示例使用的是控制台输入，但在真实项目中，这里的数据完全可以来自浏览器页面、移动端 App、企业审批系统、Teams、Slack，甚至其他微服务。

对于 Workflow 来说，它并不关心数据来自哪里，只要最终收到一个 `ExternalResponse`，就会继续执行后续流程。

### JudgeExecutor 如何决定下一步

当 Workflow 收到用户输入后，请求的数据会传递给 JudgeExecutor。

它负责判断当前猜测是否正确：

```csharp
if (message == this._targetNumber)
{
    await context.YieldOutputAsync(...);
}
else if (message < this._targetNumber)
{
    await context.SendMessageAsync(NumberSignal.Below);
}
else
{
    await context.SendMessageAsync(NumberSignal.Above);
}
```

如果猜中了目标数字，Workflow 会调用 `YieldOutputAsync` 输出最终结果，整个流程结束。

如果数字偏小，则发送 `Below`；如果数字偏大，则发送 `Above`。

收到这两个信号后，Workflow 会再次回到 RequestPort，等待用户输入新的数字。

因此，整个 Workflow 实际形成了下面这样的执行过程：

```text
RequestPort
        │
        ▼
等待用户输入
        │
        ▼
JudgeExecutor
        │
        ├── 猜中
        │      │
        │      ▼
        │   输出结果并结束
        │
        └── 未猜中
               │
               ▼
        返回 RequestPort
```

可以看到，这个 Workflow 同样形成了一个循环。

不同的是，上一篇文章中的 Loop 是 Workflow 内部两个 Executor 自动循环执行；而本示例中的循环则需要等待外部用户参与，每完成一次判断，Workflow 都会暂停，直到收到新的输入后再继续执行。

## RequestPort 与普通 Executor 的区别

很多开发者第一次接触 RequestPort 时，容易把它理解成一个特殊的 Executor。

实际上，两者承担的职责完全不同。

普通 Executor 负责处理业务逻辑，输入消息后立即执行并返回结果。

而 RequestPort 并不会处理任何业务，它只是 Workflow 与外部世界之间的一座桥梁。

当 Workflow 执行到 RequestPort 时，它负责向外发起请求；收到外部响应后，再把响应重新送回 Workflow，继续后续执行。

因此可以简单理解为：

```text
Executor
负责执行业务

RequestPort
负责等待外部输入
```

## 小结

本示例介绍了 Agent Framework 中 RequestPort 的使用方式。

通过 RequestPort，Workflow 可以在执行过程中主动向外请求数据，并等待外部返回结果后继续执行。

整个过程中，Workflow 并没有因为等待用户输入而结束，而是在 RequestPort 暂停，在收到 ExternalResponse 后恢复执行。

这种模式使 Workflow 不再局限于系统内部自动流转，而能够与用户、第三方系统以及各种外部服务进行交互。

在人机协同、审批流程、客服系统以及 AI Agent 等场景中，RequestPort 往往都是构建 Human-in-the-Loop 工作流的核心能力。

