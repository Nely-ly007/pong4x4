using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UDPServer : MonoBehaviour
{
    public int port = 7777;
    [Range(10, 120)] public int tickRate = 30;

    private UdpClient udp;
    private Thread receiveThread;
    private volatile bool running;
    private readonly ConcurrentQueue<IncomingPacket> incoming = new ConcurrentQueue<IncomingPacket>();
    private GameManager gameManager;
    private float tickAccumulator;
    private float nextConfigTime;
    private bool sizeLogged;

    private class IncomingPacket
    {
        public string json;
        public IPEndPoint endpoint;
        public IncomingPacket(string j, IPEndPoint e) { json = j; endpoint = e; }
    }

    private void Awake()
    {
        // Sem isso o Unity PAUSA o Update quando a janela do servidor perde o foco
        // (acontece o tempo todo ao testar com o Editor + várias builds no mesmo PC).
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        gameManager = GetComponent<GameManager>();
        if (gameManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            gameManager = FindFirstObjectByType<GameManager>();
#else
            gameManager = FindObjectOfType<GameManager>();
#endif
        }

        if (gameManager == null)
        {
            Debug.LogError("UDPServer precisa de um GameManager na cena.");
            enabled = false;
            return;
        }

        try
        {
            udp = new UdpClient(port);

            // Windows: ICMP "porta inacessível" de um cliente que fechou derruba o Receive
            // com WSAECONNRESET (10054). Desliga esse comportamento.
            try { udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0 }, null); }
            catch { }

            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
            Debug.Log("Servidor UDP ouvindo na porta " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Não foi possível iniciar o servidor UDP (porta " + port + " em uso?): " + e.Message);
            enabled = false;
        }
    }

    private void Update()
    {
        while (incoming.TryDequeue(out IncomingPacket packet))
            HandlePacket(packet);

        if (gameManager == null || !running) return;

        gameManager.CheckTimeouts();

        float interval = 1f / Mathf.Max(10, tickRate);
        tickAccumulator = Mathf.Min(tickAccumulator + Time.deltaTime, 0.25f);

        bool ticked = false;
        while (tickAccumulator >= interval)
        {
            gameManager.Tick(interval);
            tickAccumulator -= interval;
            ticked = true;
        }

        // Envia o estado UMA vez por tick (antes enviava a cada frame, sem limite).
        if (ticked) SendStateToAll();

        // A configuração da arena vai de novo a cada segundo (barata e evita depender de um pacote só).
        if (Time.realtimeSinceStartup >= nextConfigTime)
        {
            nextConfigTime = Time.realtimeSinceStartup + 1f;
            SendConfigToAll();
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref remote);
                string json = Encoding.UTF8.GetString(data);
                incoming.Enqueue(new IncomingPacket(json, new IPEndPoint(remote.Address, remote.Port)));
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (!running) break; }
            catch (Exception e) { if (running) Debug.LogWarning("Erro UDP: " + e.Message); }
        }
    }

    private void HandlePacket(IncomingPacket packet)
    {
        NetMessage message;
        try { message = JsonUtility.FromJson<NetMessage>(packet.json); }
        catch { return; }

        if (message == null || string.IsNullOrEmpty(message.type)) return;

        if (message.type == "JOIN")
        {
            int id = gameManager.AddOrReconnectPlayer(message.clientName, packet.endpoint);
            NetMessage response = gameManager.CreateState(id);
            response.type = id >= 0 ? "WELCOME" : "FULL";
            Send(response, packet.endpoint);
            if (id >= 0) SendConfig(packet.endpoint);
        }
        else if (message.type == "INPUT")
        {
            int id = FindPlayerByEndpoint(packet.endpoint);
            if (id >= 0) gameManager.ApplyInput(id, message.axis);
        }
    }

    private int FindPlayerByEndpoint(IPEndPoint endpoint)
    {
        for (int i = 0; i < gameManager.players.Length; i++)
        {
            ServerPlayer p = gameManager.players[i];
            if (p.connected && p.endpoint != null &&
                p.endpoint.Address.Equals(endpoint.Address) && p.endpoint.Port == endpoint.Port)
                return i;
        }
        return -1;
    }

    private void SendStateToAll()
    {
        for (int i = 0; i < gameManager.players.Length; i++)
        {
            ServerPlayer p = gameManager.players[i];
            if (p.connected && p.endpoint != null)
                Send(gameManager.CreateState(i), p.endpoint);
        }
    }

    private void SendConfigToAll()
    {
        for (int i = 0; i < gameManager.players.Length; i++)
        {
            ServerPlayer p = gameManager.players[i];
            if (p.connected && p.endpoint != null) SendConfig(p.endpoint);
        }
    }

    private void SendConfig(IPEndPoint endpoint)
    {
        if (udp == null || endpoint == null) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(gameManager.CreateConfig()));
            udp.Send(data, data.Length, endpoint);
        }
        catch (Exception e) { Debug.LogWarning("Falha ao enviar configuração UDP: " + e.Message); }
    }

    private void Send(NetMessage message, IPEndPoint endpoint)
    {
        if (udp == null || endpoint == null) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            if (!sizeLogged && message.type == "STATE")
            {
                sizeLogged = true;
                Debug.Log("Tamanho de um pacote de estado: " + data.Length + " bytes" + (data.Length > 1400 ? " (acima de 1400: pode fragmentar na rede)" : " (ok)"));
            }
            udp.Send(data, data.Length, endpoint);
        }
        catch (Exception e) { Debug.LogWarning("Falha ao enviar UDP: " + e.Message); }
    }

    private void OnApplicationQuit() { StopServer(); }
    private void OnDestroy() { StopServer(); }

    public void StopServer()
    {
        if (!running) return;
        running = false;
        try { udp?.Close(); } catch { }
        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Join(200);
        }
        catch { }
        Debug.Log("Servidor UDP encerrado.");
    }
}
