using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Arena QUADRADA (unidades Unity)")]
    [Tooltip("Metade do lado da arena. Os 4 lados são sempre iguais (arena = 2x este valor).")]
    public float arenaHalfSize = 5f;
    public float paddleLength = 1.8f;
    public float paddleThickness = 0.22f;
    public float paddleSpeed = 6f;
    public float ballRadius = 0.18f;
    public float ballSpeed = 5f;
    [Tooltip("Distância do centro da raquete até a parede.")]
    public float paddleInset = 0.35f;

    [Header("Cantos / Canhões")]
    public bool cannonsEnabled = true;
    [Tooltip("Lado do quadrado livre em cada canto (as raquetes não entram nele). Também é a zona que ativa o canhão.")]
    public float cornerSize = 1f;
    [Tooltip("Raio visual do canhão (e distância em que a bola sai dele).")]
    public float cannonRadius = 0.45f;
    [Tooltip("Segundos que o canhão segura a bola enquanto mira.")]
    public float cannonAimTime = 0.8f;
    [Tooltip("O ângulo de tiro varia até +/- este valor (graus) em torno da diagonal que aponta para o centro.")]
    [Range(5f, 44f)] public float cannonSpread = 35f;
    [Tooltip("MARQUE para habilitar o tiro duplo: o canhão atira a bola e mais uma bola EXTRA em outro ângulo. A extra também pode dar penalidade.")]
    public bool cannonDoubleShot = false;
    [Tooltip("Quantas bolas extras podem existir ao mesmo tempo (1 a 3).")]
    [Range(1, 3)] public int maxExtraBalls = 2;
    [Tooltip("Segundos que a bola extra dura (some sozinha).")]
    public float extraBallLifetime = 8f;

    [Header("Bumpers (estilo pinball) - 1 por quadrante")]
    public bool bumpersEnabled = true;
    [Tooltip("Raio de cada bumper.")]
    public float bumperRadius = 0.45f;
    [Tooltip("Posição inicial: distância do centro da arena até cada bumper, como fração de Arena Half Size.")]
    [Range(0.2f, 0.8f)] public float bumperPosition = 0.45f;
    [Tooltip("Velocidade que CADA batida num bumper acrescenta à bola (acumula).")]
    public float bumperKick = 1.5f;
    [Tooltip("Quanto da velocidade ganha nos bumpers se perde por segundo.")]
    public float bumperBonusDecay = 0.3f;
    [Tooltip("Máximo de velocidade extra acumulada pelos bumpers.")]
    public float maxBumperBonus = 4f;
    [Tooltip("Velocidade máxima que a bola pode atingir.")]
    public float maxBallSpeed = 11f;
    [Tooltip("A cada quantos segundos os bumpers mudam de lugar (0 = nunca).")]
    public float bumperMoveInterval = 10f;
    [Tooltip("Velocidade com que os bumpers deslizam para o novo lugar.")]
    public float bumperMoveSpeed = 4f;

    [Header("Power-ups (um aparece de tempos em tempos)")]
    public bool powerupsEnabled = true;
    public float powerupRadius = 0.3f;
    [Tooltip("Intervalo mínimo/máximo (s) entre o fim de um power-up e o próximo aparecer.")]
    public float powerupMinInterval = 8f;
    public float powerupMaxInterval = 14f;
    [Tooltip("Segundos que o power-up fica na arena antes de sumir.")]
    public float powerupLifetime = 10f;

    [Header("Tipos de power-up (sorteados quando um aparece)")]
    [Tooltip("Turbo: bola mais rápida (para todos).")]
    public bool allowSpeed = true;
    [Tooltip("Raquete grande: aumenta a raquete de quem tocou a bola por último.")]
    public bool allowBigPaddle = true;
    [Tooltip("Escudo: quem tocou por último ignora a PRÓXIMA penalidade.")]
    public bool allowShield = true;
    [Tooltip("Encolher: as raquetes dos ADVERSÁRIOS de quem tocou por último ficam menores.")]
    public bool allowMini = true;
    [Tooltip("Congelar: as raquetes dos adversários de quem tocou por último param.")]
    public bool allowFreeze = true;
    [Tooltip("Barreira: cria um muro curto na frente do gol de quem tocou por último.")]
    public bool allowBarrier = true;
    [Tooltip("Multiplicador da velocidade da bola enquanto o turbo está ativo.")]
    public float boostMultiplier = 1.5f;
    public float boostDuration = 6f;
    public float bigPaddleMultiplier = 1.6f;
    public float bigPaddleDuration = 8f;
    [Tooltip("Tamanho das raquetes dos adversários com o power-up Encolher (0.55 = 55%).")]
    public float miniPaddleMultiplier = 0.55f;
    public float miniPaddleDuration = 6f;
    public float freezeDuration = 1.5f;
    public float barrierDuration = 6f;
    public float barrierLength = 3.2f;
    public float barrierThickness = 0.16f;
    [Tooltip("Distância da parede até a barreira (precisa ser maior que a raquete + a bola).")]
    public float barrierInset = 1.0f;

    [Header("Regras")]
    [Tooltip("Ao chegar neste número de penalidades o jogador é ELIMINADO (o lado dele vira parede). O último de pé ganha.")]
    public int maxPenaltyPoints = 5;
    public bool startOnlyWithFourPlayers = true;
    [Tooltip("Contagem regressiva (3-2-1) antes de cada saque, em segundos (0 = sem contagem).")]
    [Range(0, 5)] public int countdownSeconds = 3;
    [Tooltip("Segundos sem receber pacotes para considerar o jogador desconectado.")]
    public float disconnectTimeout = 5f;
    [Tooltip("Segundos após o fim de jogo para reiniciar automaticamente.")]
    public float restartDelay = 8f;

    public readonly ServerPlayer[] players = new ServerPlayer[4];
    public Vector2 ballPosition;
    public Vector2 ballVelocity;
    public bool gameStarted;
    public bool gameOver;
    public int loserId = -1;     // último eliminado
    public int winnerId = -1;    // último de pé

    private float serveTimer;
    private float restartTimer;
    private const float InputStaleTime = 0.5f;
    private const int MaxExtraSlots = 3;

    // Eliminação
    private readonly bool[] eliminated = new bool[4];

    // Canhões: 0 = topo-direita (+,+), 1 = baixo-direita (+,-), 2 = baixo-esquerda (-,-), 3 = topo-esquerda (-,+)
    private readonly float[] cannonAngle = new float[4];
    private readonly bool[] cannonArmed = { true, true, true, true };
    private readonly int[] cannonShots = new int[4];
    private int heldCannon = -1;
    private float holdTimer;
    private float aimStart;
    private float aimTarget;

    // Bolas extras (tiro duplo)
    private class ExtraBall { public Vector2 pos; public Vector2 vel; public float life; }
    private readonly List<ExtraBall> extras = new List<ExtraBall>();

    // Bumpers (mesma ordem dos cantos)
    private readonly Vector2[] bumperPos = new Vector2[4];
    private readonly Vector2[] bumperTarget = new Vector2[4];
    private readonly int[] bumperHits = new int[4];
    private float bumperMoveTimer;
    private float speedBonus;                          // velocidade acumulada nos bumpers

    // Power-up e efeitos por jogador
    private bool powerupActive;
    private Vector2 powerupPosition;
    private float powerupLife;
    private float powerupSpawnTimer;
    private float boostTimer;
    private const int PuSpeed = 0, PuBig = 1, PuShield = 2, PuMini = 3, PuFreeze = 4, PuBarrier = 5, PuTypeCount = 6;
    private int powerupType;
    private int lastHitter = -1;                       // última raquete que tocou a bola (-1 = ninguém)
    private readonly float[] bigTimer = new float[4];
    private readonly float[] miniTimer = new float[4];
    private readonly float[] freezeTimer = new float[4];
    private readonly float[] barrierTimer = new float[4];
    private readonly bool[] shield = new bool[4];
    private int pickupSeq;
    private int pickupType;
    private int pickupPlayer = -1;

    // Contadores de eventos para o cliente (som / partículas)
    private readonly int[] paddleHits = new int[4];
    private readonly int[] barrierHits = new int[4];

    private float Half { get { return arenaHalfSize; } }

    private float PaddleLen(int id)
    {
        float m = 1f;
        if (bigTimer[id] > 0f) m *= bigPaddleMultiplier;
        if (miniTimer[id] > 0f) m *= miniPaddleMultiplier;
        return paddleLength * m;
    }

    private bool BoostActive { get { return boostTimer > 0f; } }

    // Velocidade "de cruzeiro" da bola agora: base + bônus dos bumpers, vezes o turbo, nunca acima de maxBallSpeed.
    private float CurrentSpeed
    {
        get
        {
            float s = ballSpeed + speedBonus;
            if (BoostActive) s *= boostMultiplier;
            return Mathf.Min(s, maxBallSpeed);
        }
    }

    // O lado tem um jogador ativo (conectado e não eliminado)? Se não, é parede.
    private bool Defended(int id) { return players[id].connected && !eliminated[id]; }

    private void Awake()
    {
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new ServerPlayer(i);
            // Posiciona cada raquete na borda correta
            // mesmo antes de os clientes se conectarem.
            SetPaddleStartPosition(players[i]);
            cannonAngle[i] = BaseAngle(i);
        }

        ResetBumpers();
        ResetBall();
        ScheduleNextPowerup();
    }

    public int ConnectedCount()
    {
        int count = 0;
        foreach (ServerPlayer p in players)
            if (p.connected) count++;
        return count;
    }

    // A bola só se move se a partida começou, não acabou e (se exigido) todos os jogadores que ainda
    // estão na partida estão presentes. Vaga vazia de jogador JÁ ELIMINADO não pausa o jogo dos outros.
    public bool IsPlaying
    {
        get
        {
            if (!gameStarted || gameOver) return false;
            if (!startOnlyWithFourPlayers) return true;
            for (int i = 0; i < players.Length; i++)
                if (!players[i].connected && !eliminated[i]) return false;
            return true;
        }
    }

    public int AddOrReconnectPlayer(string name, System.Net.IPEndPoint endpoint)
    {
        // Mesmo endpoint (JOIN repetido): devolve o mesmo ID.
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].connected && EndpointEquals(players[i].endpoint, endpoint))
            {
                if (!string.IsNullOrWhiteSpace(name)) players[i].playerName = name;
                players[i].lastSeen = Time.realtimeSinceStartup;
                return i;
            }
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (!players[i].connected)
            {
                ServerPlayer p = players[i];
                p.connected = true;
                p.playerName = string.IsNullOrWhiteSpace(name) ? "Player " + (i + 1) : name;
                p.endpoint = endpoint;
                p.axis = 0f;
                p.lastSeen = Time.realtimeSinceStartup;
                SetPaddleStartPosition(p);
                Debug.Log("Cliente entrou: " + p.playerName + " (ID " + i + ") de " + endpoint
                          + (eliminated[i] ? " [vaga de jogador eliminado: assiste até a próxima partida]" : ""));
                TryStartGame();
                return i;
            }
        }

        return -1;
    }

    public void ApplyInput(int playerId, float axis)
    {
        if (playerId < 0 || playerId >= players.Length)
            return;

        players[playerId].lastSeen = Time.realtimeSinceStartup;

        if (gameOver) return;
        players[playerId].axis = Mathf.Clamp(axis, -1f, 1f);
    }

    // Libera vagas de clientes que sumiram (fecharam o jogo, reiniciaram com outra porta etc.).
    public void CheckTimeouts()
    {
        float now = Time.realtimeSinceStartup;
        foreach (ServerPlayer p in players)
        {
            if (!p.connected) continue;

            float silence = now - p.lastSeen;
            if (silence > InputStaleTime) p.axis = 0f;

            if (silence > disconnectTimeout)
            {
                Debug.Log("Cliente " + p.playerName + " (ID " + p.playerId + ") caiu por timeout.");
                p.connected = false;
                p.endpoint = null;
                p.axis = 0f;
                SetPaddleStartPosition(p);
            }
        }
    }

    private void TryStartGame()
    {
        if (gameStarted || gameOver) return;

        int count = ConnectedCount();
        if (count >= 1 && (!startOnlyWithFourPlayers || count == 4))
        {
            gameStarted = true;
            Debug.Log("Partida iniciada.");
            ResetBall();
        }
    }

    // ------------------------------------------------------------------ loop principal

    public void Tick(float dt)
    {
        if (gameOver)
        {
            restartTimer -= dt;
            if (restartTimer <= 0f) RestartMatch();
            return;
        }

        if (!gameStarted)
        {
            TryStartGame();
            if (!gameStarted) return;
        }

        // Alguém caiu no meio da partida: pausa até voltar a ter 4 jogadores.
        if (!IsPlaying) return;

        MovePaddles(dt);

        // Contagem 3-2-1: bola parada no centro, as raquetes já se mexem.
        // Os temporizadores dos power-ups ficam congelados para a contagem não "gastar" barreira/raquete grande.
        if (serveTimer > 0f)
        {
            serveTimer -= dt;
            return;
        }

        TickPowerups(dt);
        GlideBumpers(dt);
        TickBumperRelocation(dt);
        TickSpeedBonus(dt);

        if (heldCannon >= 0)
            UpdateHeldBall(dt);                       // bola presa num canhão: ele mira e depois atira
        else if (!MoveMainBall(dt))
            return;                                   // alguém levou penalidade: rodada acabou

        MoveExtraBalls(dt);
    }

    private void TickSpeedBonus(float dt)
    {
        if (speedBonus > 0f) speedBonus = Mathf.Max(0f, speedBonus - bumperBonusDecay * dt);
    }

    private void NormalizeVelocity(ref Vector2 v)
    {
        if (v.magnitude > 0.01f) v = v.normalized * CurrentSpeed;
    }

    // Anda a bola em sub-passos: com turbo/bumper ela chega a ~11 u/s; andar no máximo ~0,08 por passo
    // garante que nunca atravesse raquete, bumper ou barreira entre dois ticks.
    // Retorna false se a rodada terminou (penalidade).
    private bool MoveMainBall(float dt)
    {
        NormalizeVelocity(ref ballVelocity);
        float speed = Mathf.Max(0.01f, ballVelocity.magnitude);
        int steps = Mathf.Clamp(Mathf.CeilToInt(speed * dt / 0.08f), 1, 10);
        float h = dt / steps;

        for (int s = 0; s < steps; s++)
        {
            int goal = StepBall(ref ballPosition, ref ballVelocity, h, true);
            if (goal >= 0)
            {
                ScoreFor(goal);
                return false;
            }
            if (heldCannon >= 0) return true;         // o canhão capturou a bola
        }
        return true;
    }

    private void MoveExtraBalls(float dt)
    {
        for (int k = extras.Count - 1; k >= 0; k--)
        {
            if (k >= extras.Count) continue;
            ExtraBall e = extras[k];
            e.life -= dt;
            if (e.life <= 0f)
            {
                extras.RemoveAt(k);
                continue;
            }

            NormalizeVelocity(ref e.vel);
            float speed = Mathf.Max(0.01f, e.vel.magnitude);
            int steps = Mathf.Clamp(Mathf.CeilToInt(speed * dt / 0.08f), 1, 10);
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                int goal = StepBall(ref e.pos, ref e.vel, h, false);
                if (goal >= 0)
                {
                    ScoreFor(goal);                   // ResetBall limpa as extras: pare aqui
                    return;
                }
            }
        }
    }

    // Um passo de física para UMA bola (principal ou extra). Retorna o jogador que levou o gol (-1 = nenhum).
    private int StepBall(ref Vector2 pos, ref Vector2 vel, float dt, bool isMain)
    {
        pos += vel * dt;

        // 1) raquetes  2) barreiras  3) bumpers  4) canhões (só a principal)  5) power-up (só a principal)  6) gol  7) paredes sem jogador
        HandlePaddleCollisions(ref pos, ref vel, isMain);
        HandleBarriers(ref pos, ref vel);
        if (bumpersEnabled) CheckBumpers(ref pos, ref vel);

        if (isMain)
        {
            if (cannonsEnabled) CheckCannons();
            if (heldCannon >= 0) return -1;
            CheckPowerupPickup(pos);
        }

        // A bola principal é capturada pelo canhão ao entrar no canto. A extra não é: no quadrado do canto
        // (onde nenhuma raquete alcança) ela quica nas paredes em vez de dar penalidade injusta.
        bool inCorner = !isMain && cannonsEnabled && InAnyCorner(pos);

        int goal = inCorner ? -1 : FindGoal(pos, vel);
        if (goal >= 0) return goal;

        HandleWallBounces(ref pos, ref vel, inCorner);
        return -1;
    }

    private bool InAnyCorner(Vector2 pos)
    {
        float edge = Half - cornerSize;
        return Mathf.Abs(pos.x) >= edge && Mathf.Abs(pos.y) >= edge;
    }

    // ------------------------------------------------------------------ raquetes

    private float PaddleLimit(int id) { return Half - cornerSize - PaddleLen(id) * 0.5f; }

    private void MovePaddles(float dt)
    {
        foreach (ServerPlayer p in players)
        {
            if (!p.connected || eliminated[p.playerId]) continue;
            if (freezeTimer[p.playerId] > 0f) continue;   // congelado: a raquete não se move

            if (p.playerId == 0 || p.playerId == 2)
            {
                p.paddleX += p.axis * paddleSpeed * dt;
                p.paddleX = Mathf.Clamp(p.paddleX, -PaddleLimit(p.playerId), PaddleLimit(p.playerId));
                p.paddleY = p.playerId == 0 ? Half - paddleInset : -Half + paddleInset;
            }
            else
            {
                p.paddleY += p.axis * paddleSpeed * dt;
                p.paddleY = Mathf.Clamp(p.paddleY, -PaddleLimit(p.playerId), PaddleLimit(p.playerId));
                p.paddleX = p.playerId == 1 ? Half - paddleInset : -Half + paddleInset;
            }
        }
    }

    private void HandlePaddleCollisions(ref Vector2 pos, ref Vector2 vel, bool credit)
    {
        // 0 = topo, 2 = baixo (raquetes horizontais); 1 = direita, 3 = esquerda (verticais).
        TryHorizontalPaddleCollision(0, Half - paddleInset, true, ref pos, ref vel, credit);
        TryHorizontalPaddleCollision(2, -Half + paddleInset, false, ref pos, ref vel, credit);
        TryVerticalPaddleCollision(1, Half - paddleInset, true, ref pos, ref vel, credit);
        TryVerticalPaddleCollision(3, -Half + paddleInset, false, ref pos, ref vel, credit);
    }

    private void TryHorizontalPaddleCollision(int id, float y, bool top, ref Vector2 pos, ref Vector2 vel, bool credit)
    {
        ServerPlayer p = players[id];
        if (!p.connected || eliminated[id]) return;

        float dir = top ? 1f : -1f;
        float innerFace = y - dir * paddleThickness * 0.5f;   // face voltada para o centro
        float outerFace = y + dir * paddleThickness * 0.5f;

        // A borda da bola tocou a face interna e o centro ainda não passou da face externa.
        bool touching = top
            ? vel.y > 0f && pos.y + ballRadius >= innerFace && pos.y <= outerFace
            : vel.y < 0f && pos.y - ballRadius <= innerFace && pos.y >= outerFace;

        if (!touching) return;

        if (Mathf.Abs(pos.x - p.paddleX) <= PaddleLen(id) * 0.5f + ballRadius)
        {
            pos.y = innerFace - dir * ballRadius;
            vel.y = top ? -Mathf.Abs(vel.y) : Mathf.Abs(vel.y);
            AddBounceAngle(p, true, pos, ref vel);
            paddleHits[id]++;
            if (credit) lastHitter = id;
        }
    }

    private void TryVerticalPaddleCollision(int id, float x, bool right, ref Vector2 pos, ref Vector2 vel, bool credit)
    {
        ServerPlayer p = players[id];
        if (!p.connected || eliminated[id]) return;

        float dir = right ? 1f : -1f;
        float innerFace = x - dir * paddleThickness * 0.5f;
        float outerFace = x + dir * paddleThickness * 0.5f;

        bool touching = right
            ? vel.x > 0f && pos.x + ballRadius >= innerFace && pos.x <= outerFace
            : vel.x < 0f && pos.x - ballRadius <= innerFace && pos.x >= outerFace;

        if (!touching) return;

        if (Mathf.Abs(pos.y - p.paddleY) <= PaddleLen(id) * 0.5f + ballRadius)
        {
            pos.x = innerFace - dir * ballRadius;
            vel.x = right ? -Mathf.Abs(vel.x) : Mathf.Abs(vel.x);
            AddBounceAngle(p, false, pos, ref vel);
            paddleHits[id]++;
            if (credit) lastHitter = id;
        }
    }

    private void AddBounceAngle(ServerPlayer p, bool horizontalPaddle, Vector2 pos, ref Vector2 vel)
    {
        // Altera o ângulo conforme o ponto de contato na raquete.
        float offset = horizontalPaddle
            ? (pos.x - p.paddleX) / (PaddleLen(p.playerId) * 0.5f)
            : (pos.y - p.paddleY) / (PaddleLen(p.playerId) * 0.5f);
        offset = Mathf.Clamp(offset, -1f, 1f);

        if (horizontalPaddle) vel.x += offset * 1.5f;
        else vel.y += offset * 1.5f;

        vel = vel.normalized * CurrentSpeed;

        // Evita a bola ficar quase paralela à raquete (vai e volta "para sempre").
        float minAlong = CurrentSpeed * 0.35f;
        if (horizontalPaddle)
        {
            float sign = Mathf.Sign(vel.y);
            if (Mathf.Abs(vel.y) < minAlong)
            {
                vel.y = sign * minAlong;
                vel = vel.normalized * CurrentSpeed;
            }
        }
        else
        {
            float sign = Mathf.Sign(vel.x);
            if (Mathf.Abs(vel.x) < minAlong)
            {
                vel.x = sign * minAlong;
                vel = vel.normalized * CurrentSpeed;
            }
        }
    }

    // ------------------------------------------------------------------ barreiras

    private void BarrierRect(int side, out Vector2 center, out float halfW, out float halfH)
    {
        float d = Half - barrierInset;
        float l = barrierLength * 0.5f;
        float t = barrierThickness * 0.5f;
        switch (side)
        {
            case 0: center = new Vector2(0f, d); halfW = l; halfH = t; break;
            case 1: center = new Vector2(d, 0f); halfW = t; halfH = l; break;
            case 2: center = new Vector2(0f, -d); halfW = l; halfH = t; break;
            default: center = new Vector2(-d, 0f); halfW = t; halfH = l; break;
        }
    }

    // A barreira é um muro: qualquer bola quica nela (círculo x retângulo).
    private void HandleBarriers(ref Vector2 pos, ref Vector2 vel)
    {
        for (int i = 0; i < 4; i++)
        {
            if (barrierTimer[i] <= 0f) continue;

            Vector2 c; float hw, hh;
            BarrierRect(i, out c, out hw, out hh);

            float cx = Mathf.Clamp(pos.x, c.x - hw, c.x + hw);
            float cy = Mathf.Clamp(pos.y, c.y - hh, c.y + hh);
            float dx = pos.x - cx;
            float dy = pos.y - cy;
            float d2 = dx * dx + dy * dy;
            if (d2 >= ballRadius * ballRadius) continue;

            Vector2 n;
            if (d2 > 0.000001f)
            {
                float d = Mathf.Sqrt(d2);
                n = new Vector2(dx / d, dy / d);
                pos = new Vector2(cx + n.x * ballRadius, cy + n.y * ballRadius);
            }
            else
            {
                // centro da bola dentro do retângulo: empurra pelo eixo de menor penetração
                float ox = hw - Mathf.Abs(pos.x - c.x);
                float oy = hh - Mathf.Abs(pos.y - c.y);
                if (ox < oy)
                {
                    float sx = pos.x >= c.x ? 1f : -1f;
                    n = new Vector2(sx, 0f);
                    pos = new Vector2(c.x + sx * (hw + ballRadius), pos.y);
                }
                else
                {
                    float sy = pos.y >= c.y ? 1f : -1f;
                    n = new Vector2(0f, sy);
                    pos = new Vector2(pos.x, c.y + sy * (hh + ballRadius));
                }
            }

            float vn = vel.x * n.x + vel.y * n.y;
            if (vn >= 0f) continue;                   // já está se afastando

            Vector2 r = new Vector2(vel.x - 2f * vn * n.x, vel.y - 2f * vn * n.y);
            vel = r.normalized * CurrentSpeed;
            barrierHits[i]++;
        }
    }

    // ------------------------------------------------------------------ bumpers

    private static float SignX(int cannon) { return (cannon == 0 || cannon == 1) ? 1f : -1f; }
    private static float SignY(int cannon) { return (cannon == 0 || cannon == 3) ? 1f : -1f; }

    private Vector2 BumperDefault(int i)
    {
        float o = Half * bumperPosition;
        return new Vector2(SignX(i) * o, SignY(i) * o);
    }

    private void ResetBumpers()
    {
        for (int i = 0; i < 4; i++)
        {
            bumperPos[i] = BumperDefault(i);
            bumperTarget[i] = bumperPos[i];
        }
        bumperMoveTimer = bumperMoveInterval;
    }

    private void CheckBumpers(ref Vector2 pos, ref Vector2 vel)
    {
        float minDist = bumperRadius + ballRadius;
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = bumperPos[i];
            Vector2 d = pos - c;
            float dist = d.magnitude;
            if (dist >= minDist || dist < 0.0001f) continue;

            Vector2 n = d / dist;                       // normal do ricochete
            pos = c + n * minDist;                      // empurra a bola para fora do bumper

            float vn = vel.x * n.x + vel.y * n.y;
            if (vn >= 0f) continue;                     // já está se afastando

            Vector2 r = new Vector2(vel.x - 2f * vn * n.x, vel.y - 2f * vn * n.y);
            speedBonus = Mathf.Min(speedBonus + bumperKick, maxBumperBonus);   // cada batida ACUMULA velocidade
            vel = r.normalized * CurrentSpeed;
            bumperHits[i]++;
        }
    }

    // Os bumpers deslizam suavemente até o novo lugar.
    private void GlideBumpers(float dt)
    {
        if (!bumpersEnabled) return;
        for (int i = 0; i < 4; i++)
        {
            Vector2 d = bumperTarget[i] - bumperPos[i];
            float dist = d.magnitude;
            if (dist < 0.001f) { bumperPos[i] = bumperTarget[i]; continue; }
            float step = Mathf.Max(0.1f, bumperMoveSpeed) * dt;
            bumperPos[i] = step >= dist ? bumperTarget[i] : bumperPos[i] + d / dist * step;
        }
    }

    private void TickBumperRelocation(float dt)
    {
        if (!bumpersEnabled || bumperMoveInterval <= 0f) return;
        bumperMoveTimer -= dt;
        if (bumperMoveTimer > 0f) return;

        bumperMoveTimer = bumperMoveInterval;
        PickBumperTargets();
    }

    // Sorteia um novo lugar para cada bumper (continua 1 por quadrante), longe da bola, do power-up,
    // das raquetes e dos canhões dos cantos.
    private void PickBumperTargets()
    {
        float minR = Half * 0.22f;
        float maxR = Mathf.Max(minR + 0.5f, Half - (cannonsEnabled ? cornerSize : 0.6f) - bumperRadius - 0.35f);
        float clearBall = bumperRadius + ballRadius + 0.7f;

        for (int i = 0; i < 4; i++)
        {
            Vector2 chosen = bumperTarget[i];
            for (int attempt = 0; attempt < 25; attempt++)
            {
                Vector2 cand = new Vector2(SignX(i) * Random.Range(minR, maxR), SignY(i) * Random.Range(minR, maxR));

                if ((cand - ballPosition).magnitude < clearBall) continue;
                bool ok = true;
                for (int k = 0; k < extras.Count; k++)
                    if ((cand - extras[k].pos).magnitude < clearBall) ok = false;
                if (powerupActive && (cand - powerupPosition).magnitude < bumperRadius + powerupRadius + 0.4f) ok = false;
                if (!ok) continue;

                chosen = cand;
                break;
            }
            bumperTarget[i] = chosen;
        }
    }

    // ------------------------------------------------------------------ power-ups

    private void ScheduleNextPowerup()
    {
        powerupSpawnTimer = Random.Range(powerupMinInterval, Mathf.Max(powerupMinInterval, powerupMaxInterval));
    }

    private void TickPowerups(float dt)
    {
        for (int i = 0; i < 4; i++)
        {
            if (bigTimer[i] > 0f) bigTimer[i] = Mathf.Max(0f, bigTimer[i] - dt);
            if (miniTimer[i] > 0f) miniTimer[i] = Mathf.Max(0f, miniTimer[i] - dt);
            if (freezeTimer[i] > 0f) freezeTimer[i] = Mathf.Max(0f, freezeTimer[i] - dt);
            if (barrierTimer[i] > 0f) barrierTimer[i] = Mathf.Max(0f, barrierTimer[i] - dt);
        }

        if (boostTimer > 0f) boostTimer = Mathf.Max(0f, boostTimer - dt);   // ao acabar, a velocidade volta ao normal sozinha

        if (!powerupsEnabled) { powerupActive = false; return; }

        if (powerupActive)
        {
            powerupLife -= dt;
            if (powerupLife <= 0f) { powerupActive = false; ScheduleNextPowerup(); }
        }
        else
        {
            powerupSpawnTimer -= dt;
            if (powerupSpawnTimer <= 0f) SpawnPowerup();
        }
    }

    // Sorteia entre os tipos ligados no Inspector (-1 = nenhum ligado).
    private int PickPowerupType()
    {
        int[] pool = new int[PuTypeCount];
        int n = 0;
        if (allowSpeed) pool[n++] = PuSpeed;
        if (allowBigPaddle) pool[n++] = PuBig;
        if (allowShield) pool[n++] = PuShield;
        if (allowMini) pool[n++] = PuMini;
        if (allowFreeze) pool[n++] = PuFreeze;
        if (allowBarrier) pool[n++] = PuBarrier;
        return n == 0 ? -1 : pool[Random.Range(0, n)];
    }

    private void SpawnPowerup()
    {
        int type = PickPowerupType();
        if (type < 0) { ScheduleNextPowerup(); return; }
        powerupType = type;

        float range = Half * 0.65f;
        Vector2 pos = Vector2.zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            pos = new Vector2(Random.Range(-range, range), Random.Range(-range, range));
            if (pos.magnitude < 1.2f) continue;          // não nasce em cima do ponto de saque

            bool ok = true;
            if (bumpersEnabled)
            {
                for (int i = 0; i < 4; i++)
                    if ((pos - bumperTarget[i]).magnitude < bumperRadius + powerupRadius + 0.5f) ok = false;
            }
            if (ok) break;
        }

        powerupPosition = pos;
        powerupLife = powerupLifetime;
        powerupActive = true;
    }

    private static bool NeedsOwner(int type)
    {
        return type == PuBig || type == PuShield || type == PuMini || type == PuFreeze || type == PuBarrier;
    }

    private void CheckPowerupPickup(Vector2 pos)
    {
        if (!powerupActive) return;
        if ((pos - powerupPosition).magnitude > powerupRadius + ballRadius) return;

        // Power-up com "dono" e ninguém tocou na bola ainda: se o turbo estiver desligado
        // ele não tem como virar turbo, então espera alguém tocar.
        if (lastHitter < 0 && NeedsOwner(powerupType) && !allowSpeed) return;

        powerupActive = false;
        ScheduleNextPowerup();
        ApplyPowerup(powerupType);
    }

    // O crédito vai para quem tocou na bola por último (lastHitter).
    private void ApplyPowerup(int type)
    {
        int who = lastHitter;

        // Efeitos que precisam de um "dono": se ninguém tocou na bola ainda, vira turbo.
        if (who < 0 && NeedsOwner(type)) type = PuSpeed;

        switch (type)
        {
            case PuBig:
                bigTimer[who] = bigPaddleDuration;
                break;
            case PuShield:
                shield[who] = true;
                break;
            case PuMini:
                for (int i = 0; i < 4; i++)
                    if (i != who && Defended(i)) miniTimer[i] = miniPaddleDuration;
                break;
            case PuFreeze:
                for (int i = 0; i < 4; i++)
                    if (i != who) freezeTimer[i] = freezeDuration;
                break;
            case PuBarrier:
                barrierTimer[who] = barrierDuration;
                break;
            default:
                type = PuSpeed;
                boostTimer = boostDuration;
                break;
        }

        pickupSeq++;
        pickupType = type;
        pickupPlayer = who;
        Debug.Log("Power-up " + type + " pego por " + (who >= 0 ? "P" + (who + 1) : "ninguém") + ".");
    }

    // ------------------------------------------------------------------ canhões

    // Diagonal que aponta do canto para o centro da arena (em graus).
    private float BaseAngle(int cannon)
    {
        return Mathf.Atan2(-SignY(cannon), -SignX(cannon)) * Mathf.Rad2Deg;
    }

    private Vector2 CannonCenter(int cannon)
    {
        float o = Half - cornerSize * 0.5f;
        return new Vector2(SignX(cannon) * o, SignY(cannon) * o);
    }

    private bool InCannonZone(int cannon)
    {
        float edge = Half - cornerSize;
        return SignX(cannon) * ballPosition.x >= edge && SignY(cannon) * ballPosition.y >= edge;
    }

    private void CheckCannons()
    {
        for (int i = 0; i < 4; i++)
        {
            if (!InCannonZone(i))
            {
                cannonArmed[i] = true;   // a bola saiu do canto: o canhão pode capturar de novo
                continue;
            }

            if (cannonArmed[i] && heldCannon < 0)
                CaptureBall(i);
        }
    }

    private void CaptureBall(int cannon)
    {
        heldCannon = cannon;
        holdTimer = Mathf.Max(0.05f, cannonAimTime);
        ballPosition = CannonCenter(cannon);
        ballVelocity = Vector2.zero;

        aimStart = cannonAngle[cannon];
        // Ângulo aleatório SEMPRE apontando para dentro da arena.
        aimTarget = BaseAngle(cannon) + Random.Range(-cannonSpread, cannonSpread);
        Debug.Log("Canhão " + cannon + " capturou a bola; mirando em " + aimTarget.ToString("F0") + "°");
    }

    private void UpdateHeldBall(float dt)
    {
        int c = heldCannon;
        ballPosition = CannonCenter(c);
        holdTimer -= dt;

        float k = 1f - Mathf.Clamp01(holdTimer / Mathf.Max(0.05f, cannonAimTime));
        float s = k * k * (3f - 2f * k);                       // suaviza o giro do cano
        cannonAngle[c] = aimStart + (aimTarget - aimStart) * s;

        if (holdTimer <= 0f)
            FireCannon(c);
    }

    private static Vector2 DirFromAngle(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    private void FireCannon(int cannon)
    {
        cannonAngle[cannon] = aimTarget;
        Vector2 dir = DirFromAngle(aimTarget);

        ballPosition = CannonCenter(cannon) + dir * cannonRadius;
        ballVelocity = dir * CurrentSpeed;
        cannonArmed[cannon] = false;   // só rearma depois que a bola sair da zona do canto
        heldCannon = -1;
        cannonShots[cannon]++;

        // Tiro duplo (opcional no Inspector): sai uma segunda bola, em outro ângulo.
        if (cannonDoubleShot && extras.Count < Mathf.Clamp(maxExtraBalls, 1, MaxExtraSlots))
        {
            Vector2 d2 = DirFromAngle(PickSecondAngle(cannon, aimTarget));
            ExtraBall e = new ExtraBall();
            e.pos = CannonCenter(cannon) + d2 * cannonRadius;
            e.vel = d2 * CurrentSpeed;
            e.life = extraBallLifetime;
            extras.Add(e);
        }

        Debug.Log("Canhão " + cannon + " atirou" + (cannonDoubleShot ? " (tiro duplo)." : "."));
    }

    // Segundo ângulo: também para dentro da arena, mas pelo menos 20° diferente do primeiro.
    private float PickSecondAngle(int cannon, float first)
    {
        float baseAngle = BaseAngle(cannon);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float a = baseAngle + Random.Range(-cannonSpread, cannonSpread);
            if (Mathf.Abs(a - first) >= 20f) return a;
        }
        return baseAngle + (first >= baseAngle ? -25f : 25f);
    }

    // ------------------------------------------------------------------ paredes / gols

    // Só quica em parede onde NÃO existe jogador ativo (lado sem raquete, ou jogador eliminado).
    // Onde há jogador, a bola que passa da raquete vira penalidade em FindGoal.
    private void HandleWallBounces(ref Vector2 pos, ref Vector2 vel, bool allWalls)
    {
        if ((allWalls || !Defended(1)) && pos.x > Half - ballRadius)
        {
            pos.x = Half - ballRadius;
            vel.x = -Mathf.Abs(vel.x);
        }
        if ((allWalls || !Defended(3)) && pos.x < -Half + ballRadius)
        {
            pos.x = -Half + ballRadius;
            vel.x = Mathf.Abs(vel.x);
        }
        if ((allWalls || !Defended(0)) && pos.y > Half - ballRadius)
        {
            pos.y = Half - ballRadius;
            vel.y = -Mathf.Abs(vel.y);
        }
        if ((allWalls || !Defended(2)) && pos.y < -Half + ballRadius)
        {
            pos.y = -Half + ballRadius;
            vel.y = Mathf.Abs(vel.y);
        }
    }

    // Gol/penalidade: a bola chegou na parede de um lado que TEM jogador ativo.
    private int FindGoal(Vector2 pos, Vector2 vel)
    {
        if (Defended(0) && vel.y > 0f && pos.y >= Half - ballRadius) return 0;
        if (Defended(1) && vel.x > 0f && pos.x >= Half - ballRadius) return 1;
        if (Defended(2) && vel.y < 0f && pos.y <= -Half + ballRadius) return 2;
        if (Defended(3) && vel.x < 0f && pos.x <= -Half + ballRadius) return 3;
        return -1;
    }

    private void ScoreFor(int playerId)
    {
        if (gameOver) return;

        if (shield[playerId])
        {
            // O escudo absorve esta penalidade: o ponto é anulado e a bola volta ao centro.
            shield[playerId] = false;
            Debug.Log("Escudo de P" + (playerId + 1) + " absorveu a penalidade.");
            ResetBall();
            return;
        }

        players[playerId].score++;
        Debug.Log("Penalidade para P" + (playerId + 1) + ": " + players[playerId].score);

        if (players[playerId].score >= maxPenaltyPoints)
        {
            EliminatePlayer(playerId);
            return;
        }

        ResetBall();
    }

    // Eliminação: o lado do jogador vira parede. O último de pé ganha.
    private void EliminatePlayer(int id)
    {
        eliminated[id] = true;
        loserId = id;
        shield[id] = false;
        bigTimer[id] = 0f; miniTimer[id] = 0f; freezeTimer[id] = 0f; barrierTimer[id] = 0f;
        Debug.Log("P" + (id + 1) + " foi ELIMINADO.");

        int alive = 0, last = -1;
        for (int i = 0; i < 4; i++)
        {
            if (players[i].connected && !eliminated[i]) { alive++; last = i; }
        }

        if (alive <= 1)
        {
            gameOver = true;
            winnerId = alive == 1 ? last : -1;
            restartTimer = restartDelay;
            Debug.Log(winnerId >= 0 ? "Fim de jogo. P" + (winnerId + 1) + " venceu!" : "Fim de jogo.");
            return;
        }

        ResetBall();
    }

    private void RestartMatch()
    {
        foreach (ServerPlayer p in players) p.score = 0;
        for (int i = 0; i < 4; i++)
        {
            eliminated[i] = false;
            shield[i] = false;
            bigTimer[i] = 0f; miniTimer[i] = 0f; freezeTimer[i] = 0f; barrierTimer[i] = 0f;
        }
        gameOver = false;
        loserId = -1;
        winnerId = -1;
        gameStarted = false;
        powerupActive = false;
        ScheduleNextPowerup();
        ResetBumpers();
        Debug.Log("Reiniciando partida.");
        TryStartGame();
        if (!gameStarted) ResetBall();
    }

    private void ResetBall()
    {
        boostTimer = 0f;       // cada novo saque começa com a velocidade normal
        speedBonus = 0f;
        lastHitter = -1;       // ninguém "tocou" ainda no novo saque
        extras.Clear();
        ballPosition = Vector2.zero;
        float x = Random.value < 0.5f ? -1f : 1f;
        float y = Random.Range(-0.65f, 0.65f);
        if (Mathf.Abs(y) < 0.2f) y = 0.35f;
        ballVelocity = new Vector2(x, y).normalized * CurrentSpeed;
        serveTimer = countdownSeconds;          // contagem 3-2-1 (0 = saca na hora)

        // Solta qualquer canhão que estivesse segurando a bola.
        heldCannon = -1;
        for (int i = 0; i < 4; i++) cannonArmed[i] = true;
    }

    private void SetPaddleStartPosition(ServerPlayer p)
    {
        if (p.playerId == 0) { p.paddleX = 0; p.paddleY = Half - paddleInset; }
        if (p.playerId == 1) { p.paddleX = Half - paddleInset; p.paddleY = 0; }
        if (p.playerId == 2) { p.paddleX = 0; p.paddleY = -Half + paddleInset; }
        if (p.playerId == 3) { p.paddleX = -Half + paddleInset; p.paddleY = 0; }
    }

    private bool EndpointEquals(System.Net.IPEndPoint a, System.Net.IPEndPoint b)
    {
        return a != null && b != null && a.Address.Equals(b.Address) && a.Port == b.Port;
    }

    // ------------------------------------------------------------------ rede

    public NetConfig CreateConfig()
    {
        NetConfig c = new NetConfig();
        c.arenaHalf = arenaHalfSize;
        c.paddleLength = paddleLength;
        c.paddleThickness = paddleThickness;
        c.ballRadius = ballRadius;
        c.cornerSize = cannonsEnabled ? cornerSize : 0f;
        c.cannonRadius = cannonRadius;
        c.doubleShot = cannonsEnabled && cannonDoubleShot;
        c.bumperRadius = bumpersEnabled ? bumperRadius : 0f;
        c.powerupRadius = powerupRadius;
        c.barrierLength = barrierLength;
        c.barrierThickness = barrierThickness;
        c.barrierInset = barrierInset;
        c.maxPenalty = maxPenaltyPoints;
        return c;
    }

    public NetMessage CreateState(int assignedPlayerId)
    {
        NetMessage s = new NetMessage();
        s.type = "STATE";
        s.playerId = assignedPlayerId;

        bool playing = IsPlaying;

        // Velocidade 0 enquanto a bola está parada (contagem/pausa/canhão), para o cliente não extrapolar.
        bool moving = playing && serveTimer <= 0f && heldCannon < 0;
        s.ballXc = NetMessage.C(ballPosition.x);
        s.ballYc = NetMessage.C(ballPosition.y);
        s.ballVxc = moving ? NetMessage.C(ballVelocity.x) : 0;
        s.ballVyc = moving ? NetMessage.C(ballVelocity.y) : 0;

        bool extrasMoving = playing && serveTimer <= 0f;
        s.extraCount = Mathf.Min(extras.Count, MaxExtraSlots);
        for (int k = 0; k < s.extraCount; k++)
        {
            s.extraXc[k] = NetMessage.C(extras[k].pos.x);
            s.extraYc[k] = NetMessage.C(extras[k].pos.y);
            s.extraVxc[k] = extrasMoving ? NetMessage.C(extras[k].vel.x) : 0;
            s.extraVyc[k] = extrasMoving ? NetMessage.C(extras[k].vel.y) : 0;
            s.extraLifeC[k] = NetMessage.C(extras[k].life);
        }

        // "gameStarted" para o cliente = partida realmente rodando (ou terminada).
        s.gameStarted = gameOver || playing;
        s.gameOver = gameOver;
        s.loserId = loserId;
        s.winnerId = winnerId;
        s.countdown = (playing && serveTimer > 0f) ? Mathf.CeilToInt(serveTimer) : 0;

        s.heldCannon = heldCannon;
        s.bumperMoveInC = (bumpersEnabled && bumperMoveInterval > 0f) ? NetMessage.C(bumperMoveTimer) : -1;

        s.powerupActive = powerupsEnabled && powerupActive;
        s.powerupXc = NetMessage.C(powerupPosition.x);
        s.powerupYc = NetMessage.C(powerupPosition.y);
        s.powerupType = powerupType;
        s.boostActive = BoostActive;
        s.lastHitter = lastHitter;
        s.pickupSeq = pickupSeq;
        s.pickupType = pickupType;
        s.pickupPlayer = pickupPlayer;

        for (int i = 0; i < 4; i++)
        {
            s.scores[i] = players[i].score;
            s.connected[i] = players[i].connected;
            s.eliminated[i] = eliminated[i];
            s.paddleXc[i] = NetMessage.C(players[i].paddleX);
            s.paddleYc[i] = NetMessage.C(players[i].paddleY);
            s.paddleLenC[i] = NetMessage.C(PaddleLen(i));
            s.shield[i] = shield[i];
            s.frozen[i] = freezeTimer[i] > 0f;
            s.barrierC[i] = NetMessage.C(barrierTimer[i]);
            s.cannonAngleD[i] = (int)Mathf.Round(cannonAngle[i] * 10f);
            s.bumperXc[i] = NetMessage.C(bumperPos[i].x);
            s.bumperYc[i] = NetMessage.C(bumperPos[i].y);
            s.bumperHits[i] = bumperHits[i];
            s.paddleHits[i] = paddleHits[i];
            s.barrierHits[i] = barrierHits[i];
            s.cannonShots[i] = cannonShots[i];
        }

        return s;
    }
}
