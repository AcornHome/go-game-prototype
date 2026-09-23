using System;
using System.Threading;
using System.Threading.Tasks;
using GoGame.Net;

namespace GoGame;

/// <summary>
/// 联机对弈冒烟测试：
///   1) NetMessage 序列化与解析（HELLO / MOVE / PASS / RESIGN / BYE / ERROR）
///   2) LanHostSession 监听端口 → LanClientSession 连入 → 双向 HELLO 握手
///   3) 双向 MOVE / PASS 消息回环一致性
///   4) 异常路径：畸形消息不抛、解析返回 null
/// </summary>
public static class LanSessionSmoke
{
    private const int TestPort = 19999;

    public static async Task<int> Run()
    {
        int fails = 0;
        fails += TestSerialization();
        fails += await TestHandshakeAndRoundTrip();
        fails += TestMalformedMessages();

        Console.WriteLine($"\n[lan-smoke] 失败 {fails} 个");
        return fails == 0 ? 0 : 1;
    }

    // === 1) 序列化 / 解析 ===
    private static int TestSerialization()
    {
        Console.WriteLine("\n[lan-smoke] ===== 测试 1: NetMessage 序列化 =====");
        int fails = 0;

        var hello = NetMessage.Hello(19, 'B', true);
        var s1 = hello.ToString();
        if (s1 != "HELLO|size=19|color=B|host=true")
        { Console.WriteLine($"[lan-smoke] ✗ HELLO 串不对: {s1}"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ HELLO 序列化: {s1}");

        var mv = NetMessage.Move(3, 15);
        var s2 = mv.ToString();
        if (s2 != "MOVE|3,15")
        { Console.WriteLine($"[lan-smoke] ✗ MOVE 串不对: {s2}"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ MOVE  序列化: {s2}");

        if (NetMessage.Pass().ToString() != "PASS") { Console.WriteLine("[lan-smoke] ✗ PASS 串不对"); fails++; }
        else Console.WriteLine("[lan-smoke] ✓ PASS  序列化: PASS");
        if (NetMessage.Resign().ToString() != "RESIGN") { Console.WriteLine("[lan-smoke] ✗ RESIGN 串不对"); fails++; }
        else Console.WriteLine("[lan-smoke] ✓ RESIGN 序列化: RESIGN");
        if (NetMessage.Bye().ToString() != "BYE") { Console.WriteLine("[lan-smoke] ✗ BYE 串不对"); fails++; }
        else Console.WriteLine("[lan-smoke] ✓ BYE    序列化: BYE");
        var err = NetMessage.Error("color conflict");
        if (!err.ToString().StartsWith("ERROR|color conflict")) { Console.WriteLine($"[lan-smoke] ✗ ERROR 串不对: {err}"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ ERROR  序列化: {err}");

        // 反向解析
        var p = NetMessage.Parse("HELLO|size=13|color=W|host=false");
        if (p == null || p.Type != NetMsgType.Hello || p.Size != 13 || p.Color != 'W' || p.IsHost)
        { Console.WriteLine($"[lan-smoke] ✗ HELLO 解析错"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ HELLO 解析: size={p.Size} color={p.Color} host={p.IsHost}");

        var p2 = NetMessage.Parse("MOVE|7,8");
        if (p2 == null || p2.Type != NetMsgType.Move || p2.X != 7 || p2.Y != 8)
        { Console.WriteLine($"[lan-smoke] ✗ MOVE 解析错: {(p2 == null ? "null" : p2.Type.ToString())}"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ MOVE  解析: ({p2.X},{p2.Y})");

        var p3 = NetMessage.Parse("ERROR|some error text|with|pipe");
        if (p3 == null || p3.Type != NetMsgType.Error || p3.Text != "some error text|with|pipe")
        { Console.WriteLine($"[lan-smoke] ✗ ERROR 解析错: {p3?.Text}"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ ERROR  解析: {p3.Text}");

        return fails;
    }

    // === 2) 端到端：主机 + 客户端 + HELLO + 消息回环 ===
    private static async Task<int> TestHandshakeAndRoundTrip()
    {
        Console.WriteLine("\n[lan-smoke] ===== 测试 2: 主机/客户端握手 + 双向消息 =====");
        int fails = 0;
        LanHostSession host = null;
        LanClientSession client = null;
        var hostReceived = new System.Collections.Generic.List<NetMessage>();
        var clientReceived = new System.Collections.Generic.List<NetMessage>();
        var hostDisconnected = new System.Collections.Generic.List<string>();
        var clientDisconnected = new System.Collections.Generic.List<string>();

        try
        {
            host = new LanHostSession(TestPort);
            Console.WriteLine($"[lan-smoke] 主机监听 {host.LocalEndpoint}");

            // 启动客户端任务
            var clientTask = Task.Run(async () =>
            {
                client = new LanClientSession("127.0.0.1", TestPort);
                var myHello = NetMessage.Hello(19, 'W', false);
                var hostHello = await client.ConnectAndHandshakeAsync(myHello);

                client.MessageReceived += msg => clientReceived.Add(msg);
                client.Disconnected += reason => clientDisconnected.Add(reason);
                client.StartReceiveLoop();

                // 客户端发送 3 条消息
                await client.SendAsync(NetMessage.Move(3, 4));
                await client.SendAsync(NetMessage.Pass());
                await client.SendAsync(NetMessage.Move(15, 15));

                // 等收到主机发来的 MOVE + RESIGN
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (clientReceived.Count < 2 && DateTime.UtcNow < deadline)
                    await Task.Delay(50);

                return hostHello;
            });

            // 主机接受握手
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clientHello = await host.AcceptAndHandshakeAsync(cts.Token);
            Console.WriteLine($"[lan-smoke] 主机握手成功，客户端 size={clientHello.Size} color={clientHello.Color}");

            // 回发我的 HELLO
            await host.SendAsync(NetMessage.Hello(19, 'B', true));

            host.MessageReceived += msg => hostReceived.Add(msg);
            host.Disconnected += reason => hostDisconnected.Add(reason);
            host.StartReceiveLoop();

            // 主机回发 MOVE + RESIGN
            await Task.Delay(150);   // 让客户端的 StartReceiveLoop 准备好
            await host.SendAsync(NetMessage.Move(16, 16));
            await host.SendAsync(NetMessage.Resign());

            var hostHelloFromClient = await clientTask;

            // === 校验 ===
            // 主机收到的：客户端的 HELLO 是握手阶段直接 read 的（不进 receive loop），
            //              所以 hostReceived 只包含：客户端发的 3 条消息（不含 HELLO）
            Console.WriteLine($"[lan-smoke] 主机共收到 {hostReceived.Count} 条消息（期望 3）");
            if (hostReceived.Count != 3)
            { Console.WriteLine($"[lan-smoke] ✗ 主机消息数不对"); fails++; }
            else
            {
                if (hostReceived[0].Type != NetMsgType.Move || hostReceived[0].X != 3 || hostReceived[0].Y != 4)
                { Console.WriteLine($"[lan-smoke] ✗ 第 1 条不是 MOVE(3,4)"); fails++; }
                else Console.WriteLine($"[lan-smoke] ✓ 第 1 条: MOVE(3,4)");
                if (hostReceived[1].Type != NetMsgType.Pass)
                { Console.WriteLine($"[lan-smoke] ✗ 第 2 条不是 PASS"); fails++; }
                else Console.WriteLine($"[lan-smoke] ✓ 第 2 条: PASS");
                if (hostReceived[2].Type != NetMsgType.Move || hostReceived[2].X != 15 || hostReceived[2].Y != 15)
                { Console.WriteLine($"[lan-smoke] ✗ 第 3 条不是 MOVE(15,15)"); fails++; }
                else Console.WriteLine($"[lan-smoke] ✓ 第 3 条: MOVE(15,15)");
            }

            // 客户端收到的：主机的 HELLO 是握手阶段直接 read 的（不进 receive loop），
            //              所以 clientReceived 只包含：主机发的 2 条消息（不含 HELLO）
            Console.WriteLine($"[lan-smoke] 客户端共收到 {clientReceived.Count} 条消息（期望 2）");
            if (clientReceived.Count < 2)
            { Console.WriteLine($"[lan-smoke] ✗ 客户端消息数不对"); fails++; }
            else
            {
                if (clientReceived[0].Type != NetMsgType.Move || clientReceived[0].X != 16 || clientReceived[0].Y != 16)
                { Console.WriteLine($"[lan-smoke] ✗ 第 1 条不是 MOVE(16,16)"); fails++; }
                else Console.WriteLine($"[lan-smoke] ✓ 第 1 条: MOVE(16,16)");
                if (clientReceived[1].Type != NetMsgType.Resign)
                { Console.WriteLine($"[lan-smoke] ✗ 第 2 条不是 RESIGN"); fails++; }
                else Console.WriteLine($"[lan-smoke] ✓ 第 2 条: RESIGN");
            }

            // 握手返回的 HELLO 校验
            if (hostHelloFromClient.Type != NetMsgType.Hello || hostHelloFromClient.Size != 19 ||
                hostHelloFromClient.Color != 'B' || hostHelloFromClient.IsHost != true)
            { Console.WriteLine($"[lan-smoke] ✗ 客户端读到的 HELLO 不对"); fails++; }
            else Console.WriteLine($"[lan-smoke] ✓ 客户端收到主机 HELLO: size=19 color=B host=true");

            // 干净关闭
            await host.SendByeAsync();
            await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lan-smoke] ✗ 端到端测试异常: {ex.GetType().Name}: {ex.Message}");
            fails++;
        }
        finally
        {
            try { host?.Dispose(); } catch { }
            try { client?.Dispose(); } catch { }
        }

        return fails;
    }

    // === 3) 畸形消息处理 ===
    private static int TestMalformedMessages()
    {
        Console.WriteLine("\n[lan-smoke] ===== 测试 3: 畸形消息 =====");
        int fails = 0;

        if (NetMessage.Parse("") != null) { Console.WriteLine("[lan-smoke] ✗ 空串应返回 null"); fails++; } else Console.WriteLine("[lan-smoke] ✓ 空串 → null");
        if (NetMessage.Parse("UNKNOWN|blah") != null) { Console.WriteLine("[lan-smoke] ✗ 未知命令应返回 null"); fails++; } else Console.WriteLine("[lan-smoke] ✓ 未知命令 → null");
        if (NetMessage.Parse("MOVE|abc,def") != null) { Console.WriteLine("[lan-smoke] ✗ 非数字坐标应返回 null"); fails++; } else Console.WriteLine("[lan-smoke] ✓ 非数字坐标 → null");
        if (NetMessage.Parse("MOVE|12") != null) { Console.WriteLine("[lan-smoke] ✗ 缺坐标逗号应返回 null"); fails++; } else Console.WriteLine("[lan-smoke] ✓ 缺坐标逗号 → null");
        // HELLO 容错处理（缺字段走默认值）
        var partial = NetMessage.Parse("HELLO|size=9");
        if (partial == null || partial.Type != NetMsgType.Hello || partial.Size != 9 || partial.Color != 'B')
        { Console.WriteLine($"[lan-smoke] ✗ 部分 HELLO 应有默认 color=B"); fails++; }
        else Console.WriteLine($"[lan-smoke] ✓ 部分 HELLO 取默认 color=B size=9");

        return fails;
    }
}
