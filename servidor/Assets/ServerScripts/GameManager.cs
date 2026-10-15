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
    public float ballSpeed = 10f;
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

    [Header("Bumpers (estilo pinball) - 1 por quadrante")]
    public bool bumpersEnabled = true;
    [Tooltip("Raio de cada bumper.")]
    public float bumperRadius = 0.45f;
    [Tooltip("Distância do centro da arena até cada bumper, como fração de Arena Half Size.")]
    [Range(0.2f, 0.8f)] public float bumperPosition = 0.45f;
    [Tooltip("Velocidade extra que o bumper dá à bola no ricochete.")]
    public float bumperKick = 1.5f;
    [Tooltip("Velocidade máxima que a bola pode atingir.")]
    public float maxBallSpeed = 11f;

    [Header("Power-up de velocidade (turbo)")]
    public bool powerupsEnabled = true;
    public float powerupRadius = 0.3f;
    [Tooltip("Intervalo mínimo/máximo (s) entre o fim de um power-up e o próximo aparecer.")]
    public float powerupMinInterval = 8f;
    public float powerupMaxInterval = 14f;
    [Tooltip("Segundos que o power-up fica na arena antes de sumir.")]
    public float powerupLifetime = 10f;
    [Tooltip("Multiplicador da velocidade da bola enquanto o turbo está ativo.")]
    public float boostMultiplier = 1.5f;
    [Tooltip("Duração do turbo (s).")]
    public float boostDuration = 6f;

    [Header("Tipos de power-up (sorteados quando um aparece)")]
    [Tooltip("Turbo: bola mais rápida (para todos).")]
    public bool allowSpeed = true;
    [Tooltip("Raquete grande: aumenta a raquete de quem tocou a bola por último.")]
    public bool allowBigPaddle = true;
    [Tooltip("Escudo: quem tocou por último ignora a PRÓXIMA penalidade.")]
    public bool allowShield = true;
    [Tooltip("Bola fantasma: a bola some por alguns instantes (para todos).")]
    public bool allowGhost = true;
    [Tooltip("Congelar: as raquetes dos adversários de quem tocou por último param.")]
    public bool allowFreeze = true;
    public float bigPaddleMultiplier = 1.6f;
    public float bigPaddleDuration = 8f;
    public float ghostDuration = 1.5f;
    public float freezeDuration = 1.5f;

    [Header("Regras")]
    public int maxPenaltyPoints = 5;
    public bool startOnlyWithFourPlayers = true;
    [Tooltip("Segundos sem receber pacotes para considerar o jogador desconectado.")]
    public float disconnectTimeout = 5f;
    [Tooltip("Segundos após o fim de jogo para reiniciar automaticamente.")]
    public float restartDelay = 6f;

    public readonly ServerPlayer[] players = new ServerPlayer[4];
    public Vector2 ballPosition;
    public Vector2 ballVelocity;
    public bool gameStarted;
    public bool gameOver;
    public int loserId = -1;

    private float serveTimer;
    private float restartTimer;
    private const float ServeDelay = 0.8f;
    private const float InputStaleTime = 0.5f;

    // Canhões: 0 = topo-direita (+,+), 1 = baixo-direita (+,-), 2 = baixo-esquerda (-,-), 3 = topo-esquerda (-,+)
    private readonly float[] cannonAngle = new float[4];
    private readonly bool[] cannonArmed = { true, true, true, true };
    private int heldCannon = -1;
    private float holdTimer;
    private float aimStart;
    private float aimTarget;

    // Bumpers / power-up
    private readonly int[] bumperHits = new int[4];
    private bool powerupActive;
    private Vector2 powerupPosition;
    private float powerupLife;
    private float powerupSpawnTimer;
    private float boostTimer;

    // Tipos de power-up e efeitos por jogador.
    private const int PuSpeed = 0, PuBig = 1, PuShield = 2, PuGhost = 3, PuFreeze = 4;
    private int powerupType;
    private int lastHitter = -1;                       // última raquete que tocou a bola (-1 = ninguém)
    private readonly float[] bigTimer = new float[4];
    private readonly float[] freezeTimer = new float[4];
    private readonly bool[] shield = new bool[4];
    private float ghostTimer;
    private int pickupSeq;
    private int pickupType;
    private int pickupPlayer = -1;

    private float PaddleLen(int id) { return bigTimer[id] > 0f ? paddleLength * bigPaddleMultiplier : paddleLength; }

    private bool BoostActive { get { return boostTimer > 0f; } }
    // Velocidade "de cruzeiro" da bola agora (sobe com o turbo, nunca passa de maxBallSpeed).
    private float CurrentSpeed
    {
        get { return BoostActive ? Mathf.Min(ballSpeed * boostMultiplier, maxBallSpeed) : ballSpeed; }
    }

    private float Half { get { return arenaHalfSize; } }

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

    // A bola só se move se a partida começou, não acabou e (se exigido) os 4 estão presentes.
    public bool IsPlaying
    {
        get { return gameStarted && !gameOver && (!startOnlyWithFourPlayers || ConnectedCount() == 4); }
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
                Debug.Log("Cliente entrou: " + p.playerName + " (ID " + i + ") de " + endpoint);
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
        TickPowerups(dt);

        if (serveTimer > 0f)
        {
            serveTimer -= dt;
            return;
        }

        // Bola presa num canhão: ele mira e depois atira.
        if (heldCannon >= 0)
        {
            UpdateHeldBall(dt);
            return;
        }

        // Sub-passos: com turbo/bumper a bola chega a ~11 u/s; andar no máximo ~0,08 por passo
        // garante que ela nunca atravesse raquete, bumper ou power-up entre dois ticks.
        float speed = Mathf.Max(0.01f, ballVelocity.magnitude);
        int steps = Mathf.Clamp(Mathf.CeilToInt(speed * dt / 0.08f), 1, 8);
        float h = dt / steps;
        for (int s = 0; s < steps; s++)
        {
            StepBall(h);
            if (gameOver || heldCannon >= 0 || serveTimer > 0f) break;   // ponto, canhão ou novo saque
        }
    }

    private void StepBall(float dt)
    {
        ballPosition += ballVelocity * dt;

        // 1) raquetes  2) bumpers  3) canhões dos cantos  4) power-up  5) gols  6) paredes sem jogador.
        HandlePaddleCollisions();
        if (bumpersEnabled) CheckBumpers();
        if (cannonsEnabled) CheckCannons();
        if (heldCannon >= 0) return;

        CheckPowerupPickup();
        CheckGoals();

        if (!gameOver)
            HandleWallBounces();
    }

    // ------------------------------------------------------------------ bumpers

    // Mesma ordem dos cantos/canhões: 0 = (+,+), 1 = (+,-), 2 = (-,-), 3 = (-,+).
    private Vector2 BumperCenter(int i)
    {
        float o = Half * bumperPosition;
        return new Vector2(SignX(i) * o, SignY(i) * o);
    }

    private void CheckBumpers()
    {
        float minDist = bumperRadius + ballRadius;
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = BumperCenter(i);
            Vector2 d = ballPosition - c;
            float dist = d.magnitude;
            if (dist >= minDist || dist < 0.0001f) continue;

            Vector2 n = d / dist;                       // normal do ricochete
            ballPosition = c + n * minDist;             // empurra a bola para fora do bumper

            float vn = ballVelocity.x * n.x + ballVelocity.y * n.y;
            if (vn >= 0f) continue;                     // já está se afastando

            Vector2 r = new Vector2(ballVelocity.x - 2f * vn * n.x, ballVelocity.y - 2f * vn * n.y);
            float speed = Mathf.Min(CurrentSpeed + bumperKick, maxBallSpeed);
            ballVelocity = r.normalized * speed;        // reflete e ganha um "chute" de velocidade
            bumperHits[i]++;
        }
    }

    // ------------------------------------------------------------------ power-up / turbo

    private void ScheduleNextPowerup()
    {
        powerupSpawnTimer = Random.Range(powerupMinInterval, Mathf.Max(powerupMinInterval, powerupMaxInterval));
    }

    private void TickPowerups(float dt)
    {
        for (int i = 0; i < 4; i++)
        {
            if (bigTimer[i] > 0f) bigTimer[i] = Mathf.Max(0f, bigTimer[i] - dt);
            if (freezeTimer[i] > 0f) freezeTimer[i] = Mathf.Max(0f, freezeTimer[i] - dt);
        }
        if (ghostTimer > 0f) ghostTimer = Mathf.Max(0f, ghostTimer - dt);

        if (boostTimer > 0f)
        {
            boostTimer -= dt;
            if (boostTimer <= 0f)
            {
                boostTimer = 0f;   // acabou o turbo: volta à velocidade normal
                if (ballVelocity.magnitude > 0.01f) ballVelocity = ballVelocity.normalized * ballSpeed;
            }
        }

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
        int[] pool = new int[5];
        int n = 0;
        if (allowSpeed) pool[n++] = PuSpeed;
        if (allowBigPaddle) pool[n++] = PuBig;
        if (allowShield) pool[n++] = PuShield;
        if (allowGhost) pool[n++] = PuGhost;
        if (allowFreeze) pool[n++] = PuFreeze;
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
                    if ((pos - BumperCenter(i)).magnitude < bumperRadius + powerupRadius + 0.5f) ok = false;
            }
            if (ok) break;
        }

        powerupPosition = pos;
        powerupLife = powerupLifetime;
        powerupActive = true;
    }

    private void CheckPowerupPickup()
    {
        if (!powerupActive) return;
        if ((ballPosition - powerupPosition).magnitude > powerupRadius + ballRadius) return;

        // Power-up com "dono" (raquete grande, escudo, congelar) e ninguém tocou na bola ainda:
        // se o turbo estiver desligado ele não tem como virar turbo, então espera alguém tocar.
        bool needsOwner = powerupType == PuBig || powerupType == PuShield || powerupType == PuFreeze;
        if (lastHitter < 0 && needsOwner && !allowSpeed) return;

        powerupActive = false;
        ScheduleNextPowerup();
        ApplyPowerup(powerupType);
    }

    // O crédito vai para quem tocou na bola por último (lastHitter).
    private void ApplyPowerup(int type)
    {
        int who = lastHitter;

        // Efeitos que precisam de um "dono": se ninguém tocou na bola ainda, vira turbo.
        if (who < 0 && (type == PuBig || type == PuShield || type == PuFreeze))
            type = PuSpeed;

        switch (type)
        {
            case PuBig:
                bigTimer[who] = bigPaddleDuration;
                break;
            case PuShield:
                shield[who] = true;
                break;
            case PuGhost:
                ghostTimer = ghostDuration;
                break;
            case PuFreeze:
                for (int i = 0; i < 4; i++)
                    if (i != who) freezeTimer[i] = freezeDuration;
                break;
            default:
                type = PuSpeed;
                boostTimer = boostDuration;
                if (ballVelocity.magnitude > 0.01f) ballVelocity = ballVelocity.normalized * CurrentSpeed;
                break;
        }

        pickupSeq++;
        pickupType = type;
        pickupPlayer = who;
        Debug.Log("Power-up " + type + " pego por " + (who >= 0 ? "P" + (who + 1) : "ninguém") + ".");
    }

    private float PaddleLimit(int id) { return Half - cornerSize - PaddleLen(id) * 0.5f; }

    private void MovePaddles(float dt)
    {
        foreach (ServerPlayer p in players)
        {
            if (!p.connected) continue;
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

    // ------------------------------------------------------------------ canhões

    private static float SignX(int cannon) { return (cannon == 0 || cannon == 1) ? 1f : -1f; }
    private static float SignY(int cannon) { return (cannon == 0 || cannon == 3) ? 1f : -1f; }

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

    private void FireCannon(int cannon)
    {
        cannonAngle[cannon] = aimTarget;
        float rad = aimTarget * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        ballPosition = CannonCenter(cannon) + dir * cannonRadius;
        ballVelocity = dir * CurrentSpeed;
        cannonArmed[cannon] = false;   // só rearma depois que a bola sair da zona do canto
        heldCannon = -1;
        Debug.Log("Canhão " + cannon + " atirou.");
    }

    // ------------------------------------------------------------------ paredes / raquetes

    // Só quica em parede onde NÃO existe jogador conectado (lado sem raquete).
    // Onde há jogador, a bola que passa da raquete vira penalidade em CheckGoals.
    private void HandleWallBounces()
    {
        if (!players[1].connected && ballPosition.x > Half - ballRadius)
        {
            ballPosition.x = Half - ballRadius;
            ballVelocity.x = -Mathf.Abs(ballVelocity.x);
        }
        if (!players[3].connected && ballPosition.x < -Half + ballRadius)
        {
            ballPosition.x = -Half + ballRadius;
            ballVelocity.x = Mathf.Abs(ballVelocity.x);
        }
        if (!players[0].connected && ballPosition.y > Half - ballRadius)
        {
            ballPosition.y = Half - ballRadius;
            ballVelocity.y = -Mathf.Abs(ballVelocity.y);
        }
        if (!players[2].connected && ballPosition.y < -Half + ballRadius)
        {
            ballPosition.y = -Half + ballRadius;
            ballVelocity.y = Mathf.Abs(ballVelocity.y);
        }
    }

    private void HandlePaddleCollisions()
    {
        // 0 = topo, 2 = baixo (raquetes horizontais); 1 = direita, 3 = esquerda (verticais).
        TryHorizontalPaddleCollision(0, Half - paddleInset, true);
        TryHorizontalPaddleCollision(2, -Half + paddleInset, false);
        TryVerticalPaddleCollision(1, Half - paddleInset, true);
        TryVerticalPaddleCollision(3, -Half + paddleInset, false);
    }

    private void TryHorizontalPaddleCollision(int id, float y, bool top)
    {
        ServerPlayer p = players[id];
        if (!p.connected) return;

        float dir = top ? 1f : -1f;
        float innerFace = y - dir * paddleThickness * 0.5f;   // face voltada para o centro
        float outerFace = y + dir * paddleThickness * 0.5f;

        // A borda da bola tocou a face interna e o centro ainda não passou da face externa.
        bool touching = top
            ? ballVelocity.y > 0f && ballPosition.y + ballRadius >= innerFace && ballPosition.y <= outerFace
            : ballVelocity.y < 0f && ballPosition.y - ballRadius <= innerFace && ballPosition.y >= outerFace;

        if (!touching) return;

        if (Mathf.Abs(ballPosition.x - p.paddleX) <= PaddleLen(id) * 0.5f + ballRadius)
        {
            ballPosition.y = innerFace - dir * ballRadius;
            ballVelocity.y = top ? -Mathf.Abs(ballVelocity.y) : Mathf.Abs(ballVelocity.y);
            AddBounceAngle(p, true);
            lastHitter = id;
        }
    }

    private void TryVerticalPaddleCollision(int id, float x, bool right)
    {
        ServerPlayer p = players[id];
        if (!p.connected) return;

        float dir = right ? 1f : -1f;
        float innerFace = x - dir * paddleThickness * 0.5f;
        float outerFace = x + dir * paddleThickness * 0.5f;

        bool touching = right
            ? ballVelocity.x > 0f && ballPosition.x + ballRadius >= innerFace && ballPosition.x <= outerFace
            : ballVelocity.x < 0f && ballPosition.x - ballRadius <= innerFace && ballPosition.x >= outerFace;

        if (!touching) return;

        if (Mathf.Abs(ballPosition.y - p.paddleY) <= PaddleLen(id) * 0.5f + ballRadius)
        {
            ballPosition.x = innerFace - dir * ballRadius;
            ballVelocity.x = right ? -Mathf.Abs(ballVelocity.x) : Mathf.Abs(ballVelocity.x);
            AddBounceAngle(p, false);
            lastHitter = id;
        }
    }

    private void AddBounceAngle(ServerPlayer p, bool horizontalPaddle)
    {
        // Altera o ângulo conforme o ponto de contato na raquete.
        float offset = horizontalPaddle
            ? (ballPosition.x - p.paddleX) / (PaddleLen(p.playerId) * 0.5f)
            : (ballPosition.y - p.paddleY) / (PaddleLen(p.playerId) * 0.5f);
        offset = Mathf.Clamp(offset, -1f, 1f);

        if (horizontalPaddle) ballVelocity.x += offset * 1.5f;
        else ballVelocity.y += offset * 1.5f;

        ballVelocity = ballVelocity.normalized * CurrentSpeed;

        // Evita a bola ficar quase paralela à raquete (vai e volta "para sempre").
        float minAlong = CurrentSpeed * 0.35f;
        if (horizontalPaddle)
        {
            float sign = Mathf.Sign(ballVelocity.y);
            if (Mathf.Abs(ballVelocity.y) < minAlong)
            {
                ballVelocity.y = sign * minAlong;
                ballVelocity = ballVelocity.normalized * CurrentSpeed;
            }
        }
        else
        {
            float sign = Mathf.Sign(ballVelocity.x);
            if (Mathf.Abs(ballVelocity.x) < minAlong)
            {
                ballVelocity.x = sign * minAlong;
                ballVelocity = ballVelocity.normalized * CurrentSpeed;
            }
        }
    }

    // Gol/penalidade: a bola chegou na parede de um lado que TEM jogador.
    // A linha do gol fica ANTES do limite de quique das paredes (bug original: o
    // quique em halfWidth - ballRadius acontecia antes da linha do gol e nunca pontuava).
    private void CheckGoals()
    {
        if (players[0].connected && ballVelocity.y > 0f && ballPosition.y >= Half - ballRadius)
            ScoreFor(0);
        else if (players[1].connected && ballVelocity.x > 0f && ballPosition.x >= Half - ballRadius)
            ScoreFor(1);
        else if (players[2].connected && ballVelocity.y < 0f && ballPosition.y <= -Half + ballRadius)
            ScoreFor(2);
        else if (players[3].connected && ballVelocity.x < 0f && ballPosition.x <= -Half + ballRadius)
            ScoreFor(3);
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
            gameOver = true;
            loserId = playerId;
            restartTimer = restartDelay;
            Debug.Log("Fim de jogo. P" + (playerId + 1) + " perdeu.");
            return;
        }

        ResetBall();
    }

    private void RestartMatch()
    {
        foreach (ServerPlayer p in players) p.score = 0;
        gameOver = false;
        loserId = -1;
        gameStarted = false;
        powerupActive = false;
        ScheduleNextPowerup();
        for (int i = 0; i < 4; i++) { bigTimer[i] = 0f; freezeTimer[i] = 0f; shield[i] = false; }
        ghostTimer = 0f;
        Debug.Log("Reiniciando partida.");
        TryStartGame();
        if (!gameStarted) ResetBall();
    }

    private void ResetBall()
    {
        boostTimer = 0f;   // cada novo saque começa com a velocidade normal
        lastHitter = -1;   // ninguém "tocou" ainda no novo saque
        ballPosition = Vector2.zero;
        float x = Random.value < 0.5f ? -1f : 1f;
        float y = Random.Range(-0.65f, 0.65f);
        if (Mathf.Abs(y) < 0.2f) y = 0.35f;
        ballVelocity = new Vector2(x, y).normalized * CurrentSpeed;
        serveTimer = ServeDelay;

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

    public NetMessage CreateState(int assignedPlayerId)
    {
        NetMessage state = new NetMessage();
        state.type = "STATE";
        state.playerId = assignedPlayerId;
        state.ballX = ballPosition.x;
        state.ballY = ballPosition.y;

        // Velocidade 0 enquanto a bola está parada (saque/pausa/canhão), para o cliente não extrapolar.
        bool moving = IsPlaying && serveTimer <= 0f && heldCannon < 0;
        state.ballVx = moving ? ballVelocity.x : 0f;
        state.ballVy = moving ? ballVelocity.y : 0f;

        // "gameStarted" para o cliente = partida realmente rodando (ou terminada).
        state.gameStarted = gameOver || IsPlaying;
        state.gameOver = gameOver;
        state.loserId = loserId;

        state.arenaHalf = arenaHalfSize;
        state.paddleLength = paddleLength;
        state.paddleThickness = paddleThickness;
        state.ballRadius = ballRadius;
        state.cornerSize = cannonsEnabled ? cornerSize : 0f;
        state.cannonRadius = cannonRadius;
        state.heldCannon = heldCannon;

        state.bumperRadius = bumpersEnabled ? bumperRadius : 0f;
        state.bumperOffset = Half * bumperPosition;
        state.powerupRadius = powerupRadius;
        state.powerupActive = powerupsEnabled && powerupActive;
        state.powerupX = powerupPosition.x;
        state.powerupY = powerupPosition.y;
        state.boostActive = BoostActive;
        state.powerupType = powerupType;
        state.lastHitter = lastHitter;
        state.ghost = ghostTimer > 0f;
        state.pickupSeq = pickupSeq;
        state.pickupType = pickupType;
        state.pickupPlayer = pickupPlayer;

        for (int i = 0; i < 4; i++)
        {
            state.scores[i] = players[i].score;
            state.paddleX[i] = players[i].paddleX;
            state.paddleY[i] = players[i].paddleY;
            state.connected[i] = players[i].connected;
            state.cannonAngle[i] = cannonAngle[i];
            state.bumperHits[i] = bumperHits[i];
            state.paddleLen[i] = PaddleLen(i);
            state.shield[i] = shield[i];
            state.frozen[i] = freezeTimer[i] > 0f;
        }

        return state;
    }
}
