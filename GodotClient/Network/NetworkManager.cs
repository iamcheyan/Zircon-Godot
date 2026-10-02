using System;
using System.Net.Sockets;
using Godot;
using Library.Network;

namespace ZirconClient.Network;

public partial class NetworkManager : Node
{
    public ServerConnection Connection { get; private set; }
    public string BuyAddress { get; set; } = string.Empty;
    public new bool IsConnected => Connection != null && Connection.Connected;

    public event Action<string> Log;

    private int _frameCount;
    private byte[] _recvBuffer = new byte[8 * 1024];
    private byte[] _rawData = Array.Empty<byte>();

    public override void _Ready()
    {
        // E5/B2: 数据层装载 (autoload 首位, 先于任何 FrameSet/MagicEffectTable 消费者)
        Scripts.DataLayer.LoadAll();
        DatabaseLoader.Load();
    }

    public override void _Process(double delta)
    {
        _frameCount++;
        if (Connection == null || !Connection.Connected) return;

        // 同步轮询接收（替代 BaseConnection 的异步 BeginReceive，Godot 环境下异步回调可能不触发）
        try
        {
            var client = typeof(BaseConnection).GetField("Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(Connection) as TcpClient;
            if (client != null && client.Connected && client.Available > 0)
            {
                GD.Print($"[Net] 收到数据 {client.Available} 字节");
                int read = client.GetStream().Read(_recvBuffer, 0, _recvBuffer.Length);
                if (read > 0)
                {
                    byte[] temp = _rawData;
                    _rawData = new byte[read + temp.Length];
                    Array.Copy(temp, 0, _rawData, 0, temp.Length);
                    Array.Copy(_recvBuffer, 0, _rawData, temp.Length, read);

                    Packet p;
                    while ((p = Packet.ReceivePacket(_rawData, out _rawData)) != null)
                    {
                        GD.Print($"[Net] 入队: {p.PacketType.Name}");
                        Connection.ReceiveList.Enqueue(p);
                    }
                }
            }
            else if (client != null && client.Client != null && !client.Connected)
            {
                string bufferedFrame = _rawData.Length >= 4
                    ? BitConverter.ToInt32(_rawData, 0).ToString()
                    : "incomplete-header";
                string reason = $"[Net] TCP 连接已断开（buffer={_rawData.Length} bytes, frameLength={bufferedFrame}）";
                GD.PrintErr(reason);
                Log?.Invoke(reason);
                Connection.NotifyDisconnected(closeTransport: true);
                return;
            }

            Connection.Process();
        }
        catch (Exception ex)
        {
            string reason = $"[Net] Process 异常（buffer={_rawData.Length} bytes）: {ex}";
            GD.PrintErr(reason);
            Log?.Invoke(reason);
            Connection.NotifyDisconnected(closeTransport: true);
        }
    }

    public bool Connect(string host, int port)
    {
        Disconnect();
        // 连接重建时不能复用上一次 TCP 连接留下的半包，否则新连接的
        // 第一个长度字段会被旧数据污染，表现为登录/选角随机卡死。
        _rawData = Array.Empty<byte>();
        Packet.IsClient = true;
        var client = new TcpClient();
        try { client.Connect(host, port); }
        catch (Exception ex)
        {
            Log?.Invoke($"[Net] 连接失败: {ex.Message}");
            return false;
        }

        Connection = new ServerConnection(client);
        Connection.OnException = (o, ex) => Log?.Invoke($"[Net] {ex.GetType().Name}: {ex.Message}");
        // 不调 StartReceive() — 改用同步轮询
        Connection.UpdateTimeOut();
        Log?.Invoke($"[Net] TCP 已连接 {host}:{port}");
        return true;
    }

    public void Disconnect()
    {
        if (Connection != null)
        {
            Connection.Disconnect();
            Connection = null;
        }
        _rawData = Array.Empty<byte>();
    }
}
