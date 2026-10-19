using System;

// Mensagem principal (JOIN / INPUT / WELCOME / FULL / STATE).
// Posições e tempos viajam como INTEIROS em "centi-unidades" (valor x 100) para o pacote UDP
// ficar pequeno (< 1,4 KB). Use NetMessage.F(int) para converter de volta para float.
[Serializable]
public class NetMessage
{
    public string type;
    public string clientName;
    public int playerId = -1;
    public float axis;

    // ---- bola principal
    public int ballXc;
    public int ballYc;
    public int ballVxc;
    public int ballVyc;

    // ---- bolas extras (tiro duplo do canhão), no máximo 3
    public int extraCount;
    public int[] extraXc = new int[3];
    public int[] extraYc = new int[3];
    public int[] extraVxc = new int[3];
    public int[] extraVyc = new int[3];
    public int[] extraLifeC = new int[3];        // segundos de vida restantes x 100 (o cliente pisca no fim)

    // ---- partida
    public bool gameStarted;
    public bool gameOver;
    public int loserId = -1;      // último eliminado
    public int winnerId = -1;     // último de pé (-1 = ninguém)
    public int countdown;         // 3, 2, 1 antes do saque (0 = bola em jogo)

    // ---- jogadores (índice 0 = topo, 1 = direita, 2 = baixo, 3 = esquerda)
    public int[] scores = new int[4];             // penalidades
    public bool[] connected = new bool[4];
    public bool[] eliminated = new bool[4];
    public int[] paddleXc = new int[4];
    public int[] paddleYc = new int[4];
    public int[] paddleLenC = new int[4];
    public bool[] shield = new bool[4];
    public bool[] frozen = new bool[4];
    public int[] barrierC = new int[4];           // segundos restantes da barreira x 100 (0 = sem barreira)

    // ---- canhões: 0 = topo-direita, 1 = baixo-direita, 2 = baixo-esquerda, 3 = topo-esquerda
    public int[] cannonAngleD = new int[4];       // décimos de grau
    public int heldCannon = -1;                   // canhão que está segurando a bola (-1 = nenhum)

    // ---- bumpers (mesma ordem dos cantos)
    public int[] bumperXc = new int[4];
    public int[] bumperYc = new int[4];
    public int[] bumperHits = new int[4];
    public int bumperMoveInC;                     // segundos até os bumpers mudarem de lugar x 100 (-1 = nunca)

    // ---- contadores de eventos (o cliente toca som/partículas quando mudam)
    public int[] paddleHits = new int[4];
    public int[] barrierHits = new int[4];
    public int[] cannonShots = new int[4];

    // ---- power-up
    public bool powerupActive;
    public int powerupXc;
    public int powerupYc;
    public int powerupType;                       // 0 turbo, 1 raquete+, 2 escudo, 3 encolher, 4 congelar, 5 barreira
    public bool boostActive;
    public int lastHitter = -1;                   // última raquete que tocou a bola
    public int pickupSeq;                         // sobe a cada power-up coletado
    public int pickupType;
    public int pickupPlayer = -1;

    public static int C(float v) { return (int)Math.Round(v * 100.0); }
    public static float F(int c) { return c / 100f; }
}

// Configuração da arena. O servidor manda logo depois do WELCOME e de novo a cada segundo,
// então o cliente não precisa de nenhum valor "casado na mão".
[Serializable]
public class NetConfig
{
    public string type = "CONFIG";
    public float arenaHalf;
    public float paddleLength;
    public float paddleThickness;
    public float ballRadius;
    public float cornerSize;          // 0 = canhões desligados
    public float cannonRadius;
    public bool doubleShot;           // canhões com tiro duplo
    public float bumperRadius;        // 0 = bumpers desligados
    public float powerupRadius;
    public float barrierLength;
    public float barrierThickness;
    public float barrierInset;
    public int maxPenalty;
}
