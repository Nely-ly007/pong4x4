using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UDPClient : MonoBehaviour
{
    [Header("Servidor")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7777;
    public string clientName = "Player";

    [Header("Conexão")]
    [Tooltip("Intervalo entre tentativas de JOIN enquanto não conectado.")]
    public float joinRetryInterval = 1f;
    [Tooltip("Segundos sem receber nada do servidor para considerar a conexão perdida.")]
    public float serverTimeout = 3f;

    public int PlayerId { get; private set; } = -1;
    public bool IsConnected { get; private set; }

    private UdpClient udp;
    private Thread receiveThread;
    private volatile bool running;
    private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    private IPEndPoint serverEndpoint;
    private float nextJoinTime;
    private float lastReceiveTime;

    public event Action<NetMessage> StateReceived;
    public event Action<NetConfig> ConfigReceived;      // medidas da arena (o servidor manda após o WELCOME e a cada 1 s)
    public NetConfig LatestConfig { get; private set; }
    public event Action<string> StatusChanged;

    private void Awake()
    {
        // Sem isso, as janelas que não estão em foco PARAM de enviar/receber (testando 4 clientes no mesmo PC).
        Application.runInBackground = true;
    }

    private void Start()
    {
        Connect();
    }

    public void Connect()
    {
        if (running) return;

        try
        {
            IPAddress address;
            if (!IPAddress.TryParse(serverIP, out address))
            {
                IPAddress[] list = Dns.GetHostAddresses(serverIP);
                address = null;
                foreach (IPAddress a in list)
                    if (a.AddressFamily == AddressFamily.InterNetwork) { address = a; break; }
                if (address == null) throw new Exception("IP/host inválido: " + serverIP);
            }

            serverEndpoint = new IPEndPoint(address, serverPort);
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Connect(serverEndpoint);

            // Windows: evita WSAECONNRESET quando o servidor ainda não está no ar.
            try { udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0 }, null); }
            catch { }

            running = true;
            lastReceiveTime = Time.realtimeSinceStartup;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();

            SendJoin();
            StatusChanged?.Invoke("Solicitando entrada em " + serverEndpoint + "...");
        }
        catch (Exception e)
        {
            StatusChanged?.Invoke("Falha de conexão: " + e.Message);
            Debug.LogError("UDPClient: " + e.Message);
        }
    }

    private void SendJoin()
    {
        NetMessage join = new NetMessage();
        join.type = "JOIN";
        join.clientName = clientName;
        Send(join);
        nextJoinTime = Time.realtimeSinceStartup + Mathf.Max(0.2f, joinRetryInterval);
    }

    private void ReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref remote);
                incoming.Enqueue(Encoding.UTF8.GetString(data));
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (!running) break; }
            catch (Exception e) { if (running) Debug.LogWarning("Recepção UDP: " + e.Message); }
        }
    }

    private void Update()
    {
        if (!running) return;

        while (incoming.TryDequeue(out string json))
        {
            NetMessage message;
            try { message = JsonUtility.FromJson<NetMessage>(json); }
            catch { continue; }

            if (message == null) continue;
            lastReceiveTime = Time.realtimeSinceStartup;

            if (message.type == "CONFIG")
            {
                NetConfig config;
                try { config = JsonUtility.FromJson<NetConfig>(json); }
                catch { continue; }
                if (config != null)
                {
                    LatestConfig = config;
                    ConfigReceived?.Invoke(config);
                }
                continue;
            }

            if (message.type == "WELCOME")
            {
                bool wasConnected = IsConnected;
                PlayerId = message.playerId;
                IsConnected = PlayerId >= 0;
                if (!wasConnected)
                    StatusChanged?.Invoke(IsConnected ? "Conectado como P" + (PlayerId + 1) : "Não foi possível entrar");
            }
            else if (message.type == "FULL")
            {
                IsConnected = false;
                StatusChanged?.Invoke("Servidor cheio (4 jogadores).");
            }

            // Só repassa estado depois de entrar (ignora STATE antigo/atrasado).
            if ((message.type == "STATE" && IsConnected) || message.type == "WELCOME")
                StateReceived?.Invoke(message);
        }

        float now = Time.realtimeSinceStartup;

        // Servidor sumiu / reiniciou: volta para o estado "não conectado" e tenta de novo.
        if (IsConnected && now - lastReceiveTime > serverTimeout)
        {
            IsConnected = false;
            PlayerId = -1;
            StatusChanged?.Invoke("Conexão perdida. Tentando reconectar...");
        }

        // UDP não garante entrega: repete o JOIN até receber WELCOME.
        if (!IsConnected && now >= nextJoinTime)
            SendJoin();
    }

    public void SendInput(float axis)
    {
        if (!running || !IsConnected) return;
        NetMessage message = new NetMessage();
        message.type = "INPUT";
        message.axis = Mathf.Clamp(axis, -1f, 1f);
        Send(message);
    }

    private void Send(NetMessage message)
    {
        if (udp == null) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            udp.Send(data, data.Length);
        }
        catch (Exception e) { Debug.LogWarning("Envio UDP: " + e.Message); }
    }

    private void OnApplicationQuit() { Disconnect(); }
    private void OnDestroy() { Disconnect(); }

    public void Disconnect()
    {
        if (!running) return;
        running = false;
        IsConnected = false;
        try { udp?.Close(); } catch { }
        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Join(200);
        }
        catch { }
        StatusChanged?.Invoke("Desconectado.");
    }
}
