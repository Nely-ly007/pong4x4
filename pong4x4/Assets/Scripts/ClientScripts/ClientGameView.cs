using UnityEngine;
using UnityEngine.UI;

// Desenha o estado recebido do servidor. Tudo é feito com SpriteRenderer (2D) e shader "Sprites/Default"
// (sem luz): aparece em qualquer template 2D (Built-in, URP, URP 2D) e não precisa de prefab nenhum.
// As medidas da arena vêm do servidor (mensagem CONFIG): não há valores para "casar" entre os dois projetos.
public class ClientGameView : MonoBehaviour
{
    [Header("Referências (deixe vazio: são preenchidas sozinhas)")]
    public UDPClient udpClient;
    public Camera targetCamera;
    [Tooltip("Opcional. Sprite de 1x1 unidade (ex.: Circle). A escala é ajustada pelo script.")]
    public GameObject ballPrefab;
    [Tooltip("Opcional. Sprite de 1x1 unidade (ex.: Square). A escala é ajustada pelo script.")]
    public GameObject paddlePrefab;
    [Tooltip("Opcional. Se vazio, o texto é desenhado via OnGUI.")]
    public Text statusText;
    public Text scoreText;
    public bool createArenaOnStart = true;

    [Header("Acabamento")]
    [Tooltip("Sons gerados por código. Tecla M liga/desliga durante o jogo.")]
    public bool soundEnabled = true;
    [Range(0f, 1f)] public float volume = 0.7f;
    [Tooltip("Partículas, rastro da bola, tremor de tela, zoom e câmera lenta.")]
    public bool effectsEnabled = true;

    private static readonly string[] SideNames = { "TOPO", "DIREITA", "BAIXO", "ESQUERDA" };
    private static readonly string[] KeyHints = { "A/D ou ←/→", "W/S ou ↑/↓", "A/D ou ←/→", "W/S ou ↑/↓" };

    // Tipos de power-up (iguais ao servidor): 0 turbo, 1 raquete+, 2 escudo, 3 encolher, 4 congelar, 5 barreira.
    private static readonly string[] PowerupNames = { "TURBO", "RAQUETE +", "ESCUDO", "ENCOLHER", "CONGELAR", "BARREIRA" };
    private static readonly Color[] PowerupColors = {
        new Color(1f, 0.82f, 0.15f),
        new Color(0.30f, 0.90f, 0.40f),
        new Color(0.30f, 0.60f, 1f),
        new Color(1f, 0.38f, 0.30f),
        new Color(0.55f, 1f, 1f),
        new Color(0.92f, 0.92f, 1f)
    };

    private readonly Color[] colors = {
        new Color(1f, 0.22f, 0.58f),
        new Color(0.12f, 0.88f, 0.62f),
        new Color(0.15f, 0.65f, 1f),
        new Color(1f, 0.78f, 0.15f)
    };
    private static readonly Color BarrelColor = new Color(0.62f, 0.66f, 0.74f);
    private static readonly Color CannonBaseColor = new Color(0.30f, 0.33f, 0.40f);
    private static readonly Color BumperColor = new Color(0.55f, 0.25f, 0.95f);
    private static readonly Color ExtraBallColor = new Color(0.70f, 0.95f, 1f);

    // ------------------------------------------------------------------ configuração (vem do servidor)

    private float arenaHalf = 5f;
    private float paddleLength = 1.8f;
    private float paddleThickness = 0.22f;
    private float ballRadius = 0.18f;
    private float cornerSize = 1f;
    private float cannonRadius = 0.45f;
    private bool doubleShot;
    private float bumperRadius;
    private float powerupRadius = 0.3f;
    private float barrierLength = 3.2f;
    private float barrierThickness = 0.16f;
    private float barrierInset = 1f;
    private int maxPenalty = 5;

    private bool built;
    private string builtSignature = "";
    private GameObject root;
    private ClientFX fx;
    private ClientAudio audioFx;

    // ------------------------------------------------------------------ objetos da cena

    private Sprite squareSprite;
    private Sprite circleSprite;
    private Material spriteMaterial;

    private class BallView
    {
        public Transform t;
        public SpriteRenderer sr;
        public SpriteRenderer aura;
        public Transform trailT;
        public TrailRenderer trail;
        public Vector2 target, vel;
        public float stateTime;
        public bool hasPos;
        public bool active = true;

        public void SetActive(bool a)
        {
            if (active == a) return;
            active = a;
            t.gameObject.SetActive(a);
            if (trailT != null) trailT.gameObject.SetActive(a);
            if (!a) hasPos = false;
        }

        public void Push(Vector2 pos, Vector2 v, float now)
        {
            target = pos;
            vel = v;
            stateTime = now;
            if (!hasPos) { hasPos = true; Teleport(pos); }
        }

        private void Teleport(Vector2 p)
        {
            t.position = new Vector3(p.x, p.y, 0f);
            if (trailT != null) trailT.position = t.position;
            if (trail != null) trail.Clear();
        }

        // Extrapola um pouco com a velocidade do servidor (0 quando a bola está parada) e suaviza.
        public void Follow(float dt, float now)
        {
            if (!active || !hasPos) return;

            float age = Mathf.Min(now - stateTime, 0.1f);
            Vector2 predicted = target + vel * age;
            Vector3 current = t.position;
            Vector3 goal = new Vector3(predicted.x, predicted.y, 0f);

            if ((goal - current).sqrMagnitude > 2.25f)
                Teleport(predicted);                                         // saque / canhão: salta
            else
                t.position = Vector3.Lerp(current, goal, 1f - Mathf.Exp(-35f * dt));

            if (trailT != null) trailT.position = t.position;
        }
    }

    private BallView mainBall;
    private readonly BallView[] extraBalls = new BallView[3];

    private readonly PlayerController[] paddles = new PlayerController[4];
    private readonly SpriteRenderer[] paddleRenderers = new SpriteRenderer[4];
    private readonly GameObject[] shieldRings = new GameObject[4];
    private readonly GameObject[] myMarkers = new GameObject[4];
    private readonly GameObject[] eliminatedWalls = new GameObject[4];

    private readonly Transform[] cannonPivots = new Transform[4];
    private readonly SpriteRenderer[] cannonBases = new SpriteRenderer[4];
    private readonly SpriteRenderer[][] cannonBarrels = new SpriteRenderer[4][];
    private readonly float[] cannonTargetAngle = new float[4];
    private readonly float[] cannonCurrentAngle = new float[4];
    private int heldCannon = -1;

    private readonly Transform[] bumperTransforms = new Transform[4];
    private readonly SpriteRenderer[] bumperOuters = new SpriteRenderer[4];
    private readonly SpriteRenderer[] bumperCores = new SpriteRenderer[4];
    private readonly Vector2[] bumperTarget = new Vector2[4];
    private readonly float[] bumperFlash = new float[4];
    private float bumperMoveIn = -1f;
    private bool bumpersSnapped;

    private readonly GameObject[] barrierObjs = new GameObject[4];
    private readonly SpriteRenderer[] barrierRenderers = new SpriteRenderer[4];
    private readonly float[] barrierRemain = new float[4];
    private readonly float[] barrierPopAt = new float[4];

    private GameObject powerupObject;
    private SpriteRenderer powerupBody;
    private int powerupType;
    private bool powerupActive;
    private Vector2 powerupPos;
    private Vector2 lastPowerupPos;

    // ------------------------------------------------------------------ estado / HUD

    private string statusMessage = "Conectando...";
    private string hudStatus = "";
    private int hudMyId = -1;
    private int hudCountdown;
    private bool hudPlaying;
    private bool hudGameOver;
    private int hudWinner = -1;
    private bool boostActive;
    private int lastHitter = -1;
    private readonly int[] hudScores = new int[4];
    private readonly bool[] hudEliminated = new bool[4];
    private readonly bool[] hudConnected = new bool[4];
    private readonly float[] pipFlash = new float[4];

    private string toast = "";
    private Color toastColor = Color.white;
    private float toastStart;
    private float toastUntil;

    private float countdownShownAt;
    private float goUntil;

    // ------------------------------------------------------------------ rastreio de eventos (para som/partículas)

    private bool evInit;
    private readonly int[] pScore = new int[4];
    private readonly bool[] pElim = new bool[4];
    private readonly bool[] pShield = new bool[4];
    private readonly int[] pPaddleHits = new int[4];
    private readonly int[] pBarrierHits = new int[4];
    private readonly int[] pCannonShots = new int[4];
    private readonly int[] pBumperHits = new int[4];
    private readonly float[] pBarrierRemain = new float[4];
    private readonly float[] pPaddleLen = new float[4];
    private int pCountdown;
    private int pHeld = -1;
    private int pPickupSeq;
    private bool pGameOver;
    private int pExtraCount;
    private readonly Vector2[] pExtraPos = new Vector2[3];
    private readonly float[] pExtraLife = new float[3];
    private Vector2 pBallPos;
    private readonly Vector2[] paddlePos = new Vector2[4];

    // ------------------------------------------------------------------ ciclo de vida

    private void Awake()
    {
        if (udpClient == null) udpClient = GetComponent<UDPClient>();
        if (targetCamera == null) targetCamera = Camera.main;

        if (soundEnabled)
        {
            audioFx = GetComponent<ClientAudio>();
            if (audioFx == null) audioFx = gameObject.AddComponent<ClientAudio>();
            audioFx.masterVolume = volume;
        }
    }

    private void Start()
    {
        // A arena só é desenhada quando a configuração do servidor chegar.
        if (udpClient != null)
        {
            udpClient.StateReceived += OnStateReceived;
            udpClient.StatusChanged += OnStatusChanged;
            udpClient.ConfigReceived += OnConfigReceived;
            if (udpClient.LatestConfig != null) OnConfigReceived(udpClient.LatestConfig);
        }
    }

    private void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.StateReceived -= OnStateReceived;
            udpClient.StatusChanged -= OnStatusChanged;
            udpClient.ConfigReceived -= OnConfigReceived;
        }
    }

    // ------------------------------------------------------------------ construção

    private void OnConfigReceived(NetConfig c)
    {
        if (c == null || c.arenaHalf < 0.5f) return;

        string sig = c.arenaHalf + "|" + c.paddleLength + "|" + c.paddleThickness + "|" + c.ballRadius + "|" +
                     c.cornerSize + "|" + c.cannonRadius + "|" + c.doubleShot + "|" + c.bumperRadius + "|" +
                     c.powerupRadius + "|" + c.barrierLength + "|" + c.barrierThickness + "|" + c.barrierInset + "|" +
                     c.maxPenalty;
        if (built && sig == builtSignature) return;     // o servidor repete a config a cada 1 s

        if (built) TearDown();                          // servidor reiniciado com outras medidas: refaz a cena

        arenaHalf = c.arenaHalf;
        if (c.paddleLength > 0.01f) paddleLength = c.paddleLength;
        if (c.paddleThickness > 0.01f) paddleThickness = c.paddleThickness;
        if (c.ballRadius > 0.01f) ballRadius = c.ballRadius;
        cornerSize = Mathf.Max(0f, c.cornerSize);       // 0 = canhões desligados
        if (c.cannonRadius > 0.01f) cannonRadius = c.cannonRadius;
        doubleShot = c.doubleShot;
        bumperRadius = Mathf.Max(0f, c.bumperRadius);   // 0 = bumpers desligados
        if (c.powerupRadius > 0.01f) powerupRadius = c.powerupRadius;
        if (c.barrierLength > 0.01f) barrierLength = c.barrierLength;
        if (c.barrierThickness > 0.01f) barrierThickness = c.barrierThickness;
        barrierInset = c.barrierInset;
        maxPenalty = Mathf.Max(1, c.maxPenalty);

        builtSignature = sig;
        BuildView();
    }

    private void TearDown()
    {
        if (root != null) Destroy(root);
        root = null;
        built = false;
        evInit = false;
        bumpersSnapped = false;
        System.Array.Clear(paddles, 0, paddles.Length);
        System.Array.Clear(paddleRenderers, 0, paddleRenderers.Length);
    }

    private void BuildView()
    {
        if (built) return;
        built = true;

        CreateSharedSpriteAssets();
        root = new GameObject("ArenaRoot");

        if (targetCamera != null)
        {
            targetCamera.orthographic = true;
            targetCamera.transform.position = new Vector3(0f, 0f, -10f);
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0.055f, 0.07f, 0.12f);
            if (soundEnabled && targetCamera.GetComponent<AudioListener>() == null)
                targetCamera.gameObject.AddComponent<AudioListener>();   // sem listener não sai som
        }

        fx = root.AddComponent<ClientFX>();
        fx.enabledFx = effectsEnabled;
        fx.Init(circleSprite, spriteMaterial, targetCamera, root.transform, arenaHalf);
        FitCamera();

        if (createArenaOnStart) CreateArena();
        CreateEliminatedWalls();
        if (cornerSize > 0.01f) CreateCannons();
        if (bumperRadius > 0.01f) CreateBumpers();
        CreateBarriers();
        CreatePowerup();
        CreateBalls();
        CreatePaddles();
    }

    private void CreateSharedSpriteAssets()
    {
        if (squareSprite != null) return;

        Texture2D whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] white = new Color[16];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        whiteTex.SetPixels(white);
        whiteTex.Apply();
        whiteTex.filterMode = FilterMode.Point;
        squareSprite = Sprite.Create(whiteTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

        const int size = 64;
        Texture2D circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d);   // borda suavizada
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        circleTex.SetPixels(px);
        circleTex.Apply();
        circleTex.filterMode = FilterMode.Bilinear;
        circleSprite = Sprite.Create(circleTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) spriteMaterial = new Material(shader);
    }

    private GameObject CreateSpriteObject(string objectName, Sprite sprite, Color color, int sortingOrder)
    {
        GameObject obj = new GameObject(objectName);
        if (root != null) obj.transform.SetParent(root.transform, false);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
        return obj;
    }

    private void CreateRect(string objectName, Vector2 position, Vector2 size, Color color, int order)
    {
        GameObject obj = CreateSpriteObject(objectName, squareSprite, color, order);
        obj.transform.position = new Vector3(position.x, position.y, 0f);
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private void CreateArena()
    {
        float h = arenaHalf;
        float t = 0.12f;
        // Cada parede tem a cor do jogador que defende aquele lado.
        CreateRect("Wall_Top_P1", new Vector2(0, h + t * 0.5f), new Vector2(h * 2f + t * 2f, t), colors[0], -10);
        CreateRect("Wall_Right_P2", new Vector2(h + t * 0.5f, 0), new Vector2(t, h * 2f + t * 2f), colors[1], -10);
        CreateRect("Wall_Bottom_P3", new Vector2(0, -h - t * 0.5f), new Vector2(h * 2f + t * 2f, t), colors[2], -10);
        CreateRect("Wall_Left_P4", new Vector2(-h - t * 0.5f, 0), new Vector2(t, h * 2f + t * 2f), colors[3], -10);

        Color faint = new Color(1f, 1f, 1f, 0.12f);
        CreateRect("VerticalDivider", Vector2.zero, new Vector2(0.03f, h * 2f), faint, -10);
        CreateRect("HorizontalDivider", Vector2.zero, new Vector2(h * 2f, 0.03f), faint, -10);
    }

    // Lado de um jogador ELIMINADO vira parede sólida (barra grossa na cor dele, bem na linha da raquete).
    private void CreateEliminatedWalls()
    {
        float h = arenaHalf;
        float inset = 0.35f;
        float len = h * 2f;
        float th = paddleThickness * 1.1f;
        for (int i = 0; i < 4; i++)
        {
            Color c = new Color(colors[i].r * 0.55f, colors[i].g * 0.55f, colors[i].b * 0.55f, 1f);
            Vector2 center = SideCenter(i) + SideInward(i) * inset;
            bool horizontal = i == 0 || i == 2;
            GameObject w = CreateSpriteObject("SolidWall_P" + (i + 1), squareSprite, c, 0);
            w.transform.position = new Vector3(center.x, center.y, 0f);
            w.transform.localScale = horizontal ? new Vector3(len, th, 1f) : new Vector3(th, len, 1f);
            w.SetActive(false);
            eliminatedWalls[i] = w;
        }
    }

    // Canhões: 0 = topo-direita, 1 = baixo-direita, 2 = baixo-esquerda, 3 = topo-esquerda (igual ao servidor).
    private Vector2 CannonCenter(int i)
    {
        float sx = (i == 0 || i == 1) ? 1f : -1f;
        float sy = (i == 0 || i == 3) ? 1f : -1f;
        float offset = arenaHalf - cornerSize * 0.5f;
        return new Vector2(sx * offset, sy * offset);
    }

    private void CreateCannons()
    {
        for (int i = 0; i < 4; i++)
        {
            float sx = (i == 0 || i == 1) ? 1f : -1f;
            float sy = (i == 0 || i == 3) ? 1f : -1f;
            Vector2 c2 = CannonCenter(i);
            Vector3 center = new Vector3(c2.x, c2.y, 0f);

            // Zona do canto (onde o canhão captura a bola).
            GameObject zone = CreateSpriteObject("CannonZone_" + i, squareSprite, new Color(1f, 1f, 1f, 0.07f), -9);
            zone.transform.position = center;
            zone.transform.localScale = new Vector3(cornerSize, cornerSize, 1f);

            GameObject baseObj = CreateSpriteObject("CannonBase_" + i, circleSprite, CannonBaseColor, -5);
            baseObj.transform.position = center;
            baseObj.transform.localScale = new Vector3(cannonRadius * 2f, cannonRadius * 2f, 1f);
            cannonBases[i] = baseObj.GetComponent<SpriteRenderer>();

            // Cano(s): o pivô gira; cada cano é filho dele. Com tiro duplo são dois canos em "V".
            GameObject pivot = new GameObject("CannonPivot_" + i);
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.position = center;
            cannonPivots[i] = pivot.transform;

            if (doubleShot)
            {
                cannonBarrels[i] = new SpriteRenderer[2];
                cannonBarrels[i][0] = CreateBarrel(pivot.transform, i, 0, 13f, 0.85f);
                cannonBarrels[i][1] = CreateBarrel(pivot.transform, i, 1, -13f, 0.85f);
            }
            else
            {
                cannonBarrels[i] = new SpriteRenderer[1];
                cannonBarrels[i][0] = CreateBarrel(pivot.transform, i, 0, 0f, 1f);
            }

            float baseAngle = Mathf.Atan2(-sy, -sx) * Mathf.Rad2Deg;   // diagonal que aponta ao centro da arena
            cannonCurrentAngle[i] = baseAngle;
            cannonTargetAngle[i] = baseAngle;
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
        }
    }

    private SpriteRenderer CreateBarrel(Transform pivot, int cannon, int index, float angleDeg, float lengthScale)
    {
        float length = cannonRadius * 1.7f * lengthScale;
        float thickness = cannonRadius * (doubleShot ? 0.38f : 0.55f);
        float rad = angleDeg * Mathf.Deg2Rad;

        GameObject barrel = CreateSpriteObject("CannonBarrel_" + cannon + "_" + index, squareSprite, BarrelColor, -4);
        barrel.transform.SetParent(pivot, false);
        barrel.transform.localPosition = new Vector3(Mathf.Cos(rad) * length * 0.5f, Mathf.Sin(rad) * length * 0.5f, 0f);
        barrel.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
        barrel.transform.localScale = new Vector3(length, thickness, 1f);
        return barrel.GetComponent<SpriteRenderer>();
    }

    // Bumpers: um por quadrante (mesma ordem dos cantos). A posição vem do servidor (eles mudam de lugar).
    private void CreateBumpers()
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject outer = CreateSpriteObject("Bumper_" + i, circleSprite, BumperColor, -6);
            outer.transform.localScale = new Vector3(bumperRadius * 2f, bumperRadius * 2f, 1f);
            bumperTransforms[i] = outer.transform;
            bumperOuters[i] = outer.GetComponent<SpriteRenderer>();

            GameObject core = CreateSpriteObject("BumperCore_" + i, circleSprite, new Color(0.85f, 0.70f, 1f), -5);
            core.transform.SetParent(outer.transform, false);
            core.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
            bumperCores[i] = core.GetComponent<SpriteRenderer>();

            float sx = (i == 0 || i == 1) ? 1f : -1f;
            float sy = (i == 0 || i == 3) ? 1f : -1f;
            bumperTarget[i] = new Vector2(sx * arenaHalf * 0.45f, sy * arenaHalf * 0.45f);
            outer.transform.position = new Vector3(bumperTarget[i].x, bumperTarget[i].y, 0f);
        }
    }

    // Barreira de cada jogador (power-up): muro curto na frente do gol.
    private void CreateBarriers()
    {
        float d = arenaHalf - barrierInset;
        for (int i = 0; i < 4; i++)
        {
            bool horizontal = i == 0 || i == 2;
            Vector2 center = SideCenter(i);
            center = horizontal ? new Vector2(0f, center.y > 0f ? d : -d) : new Vector2(center.x > 0f ? d : -d, 0f);

            Color c = new Color(0.5f + colors[i].r * 0.5f, 0.5f + colors[i].g * 0.5f, 0.5f + colors[i].b * 0.5f, 0.95f);
            GameObject bar = CreateSpriteObject("Barrier_P" + (i + 1), squareSprite, c, 3);
            bar.transform.position = new Vector3(center.x, center.y, 0f);
            Vector3 full = horizontal ? new Vector3(barrierLength, barrierThickness, 1f) : new Vector3(barrierThickness, barrierLength, 1f);
            bar.transform.localScale = full;

            // brilho (filho: escala relativa)
            GameObject glow = CreateSpriteObject("BarrierGlow_P" + (i + 1), squareSprite, new Color(colors[i].r, colors[i].g, colors[i].b, 0.28f), 2);
            glow.transform.SetParent(bar.transform, false);
            glow.transform.localScale = horizontal ? new Vector3(1.04f, 4f, 1f) : new Vector3(4f, 1.04f, 1f);

            bar.SetActive(false);
            barrierObjs[i] = bar;
            barrierRenderers[i] = bar.GetComponent<SpriteRenderer>();
        }
    }

    private void CreatePowerup()
    {
        powerupObject = CreateSpriteObject("Powerup", circleSprite, PowerupColors[0], 0);
        powerupObject.transform.localScale = new Vector3(powerupRadius * 2f, powerupRadius * 2f, 1f);
        powerupBody = powerupObject.GetComponent<SpriteRenderer>();

        GameObject core = CreateSpriteObject("PowerupCore", circleSprite, Color.white, 1);
        core.transform.SetParent(powerupObject.transform, false);
        core.transform.localScale = new Vector3(0.45f, 0.45f, 1f);

        powerupObject.SetActive(false);   // só aparece quando o servidor mandar
    }

    private BallView MakeBall(string objectName, GameObject prefab, float scale, Color auraColor, bool auraOn)
    {
        GameObject go = prefab != null ? Instantiate(prefab) : CreateSpriteObject(objectName, circleSprite, Color.white, 4);
        go.name = objectName;
        if (prefab != null && root != null) go.transform.SetParent(root.transform, false);

        BallView b = new BallView();
        b.t = go.transform;
        b.t.localScale = new Vector3(ballRadius * 2f * scale, ballRadius * 2f * scale, 1f);
        b.sr = go.GetComponent<SpriteRenderer>();
        if (b.sr == null) b.sr = go.GetComponentInChildren<SpriteRenderer>();

        // Aura (filha da bola: a escala é relativa a ela).
        GameObject aura = CreateSpriteObject(objectName + "Aura", circleSprite, auraColor, 3);
        aura.transform.SetParent(go.transform, false);
        aura.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
        b.aura = aura.GetComponent<SpriteRenderer>();
        aura.SetActive(auraOn);

        // Rastro: objeto separado (largura em unidades do mundo, sem depender da escala da bola).
        if (spriteMaterial != null)
        {
            GameObject tg = new GameObject(objectName + "Trail");
            tg.transform.SetParent(root.transform, false);
            TrailRenderer tr = tg.AddComponent<TrailRenderer>();
            tr.sharedMaterial = spriteMaterial;
            tr.time = 0.22f;
            tr.startWidth = ballRadius * 1.7f * scale;
            tr.endWidth = 0f;
            tr.minVertexDistance = 0.03f;
            tr.numCapVertices = 4;
            tr.sortingOrder = 2;
            tr.autodestruct = false;
            tr.emitting = effectsEnabled;
            b.trailT = tg.transform;
            b.trail = tr;
        }
        return b;
    }

    private void CreateBalls()
    {
        mainBall = MakeBall("NetworkBall", ballPrefab, 1f, new Color(1f, 0.82f, 0.2f, 0.45f), false);
        for (int k = 0; k < extraBalls.Length; k++)
        {
            extraBalls[k] = MakeBall("ExtraBall_" + k, null, 0.85f, new Color(ExtraBallColor.r, ExtraBallColor.g, ExtraBallColor.b, 0.35f), true);
            extraBalls[k].SetActive(false);
        }
    }

    private void CreatePaddles()
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject paddle = paddlePrefab != null
                ? Instantiate(paddlePrefab)
                : CreateSpriteObject("Paddle_P" + (i + 1), squareSprite, colors[i], 1);
            if (paddlePrefab != null && root != null) paddle.transform.SetParent(root.transform, false);

            paddle.name = "Paddle_P" + (i + 1);

            PlayerController controller = paddle.GetComponent<PlayerController>();
            if (controller == null) controller = paddle.AddComponent<PlayerController>();
            controller.playerId = i;
            controller.SetOrientation(i == 0 || i == 2, paddleLength, paddleThickness);
            paddles[i] = controller;

            SpriteRenderer sr = paddle.GetComponent<SpriteRenderer>();
            if (sr == null) sr = paddle.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.color = colors[i];
            paddleRenderers[i] = sr;

            bool horizontalPaddle = i == 0 || i == 2;

            // Halo do escudo (filho da raquete; eixos invertidos nas raquetes verticais).
            GameObject ring = CreateSpriteObject("ShieldRing_" + i, squareSprite, new Color(0.35f, 0.70f, 1f, 0.55f), 0);
            ring.transform.SetParent(paddle.transform, false);
            ring.transform.localScale = horizontalPaddle ? new Vector3(1.12f, 2.8f, 1f) : new Vector3(2.8f, 1.12f, 1f);
            ring.SetActive(false);
            shieldRings[i] = ring;

            // Marcador da MINHA raquete (contorno claro atrás dela).
            GameObject mark = CreateSpriteObject("MyMarker_" + i, squareSprite, new Color(1f, 1f, 1f, 0.28f), 0);
            mark.transform.SetParent(paddle.transform, false);
            mark.transform.localScale = horizontalPaddle ? new Vector3(1.05f, 2.1f, 1f) : new Vector3(2.1f, 1.05f, 1f);
            mark.SetActive(false);
            myMarkers[i] = mark;
        }
    }

    private void FitCamera()
    {
        if (targetCamera == null) return;
        float aspect = Mathf.Max(0.1f, targetCamera.aspect);
        float byHeight = arenaHalf + 1.2f;
        float byWidth = (arenaHalf + 1.2f) / aspect;
        float zoom = fx != null ? fx.ZoomFactor : 1f;
        targetCamera.orthographicSize = Mathf.Max(byHeight, byWidth) * zoom;
    }

    // ------------------------------------------------------------------ geometria dos lados

    private Vector2 SideCenter(int i)
    {
        switch (i)
        {
            case 0: return new Vector2(0f, arenaHalf);
            case 1: return new Vector2(arenaHalf, 0f);
            case 2: return new Vector2(0f, -arenaHalf);
            default: return new Vector2(-arenaHalf, 0f);
        }
    }

    private Vector2 SideInward(int i)
    {
        switch (i)
        {
            case 0: return new Vector2(0f, -1f);
            case 1: return new Vector2(-1f, 0f);
            case 2: return new Vector2(0f, 1f);
            default: return new Vector2(1f, 0f);
        }
    }

    private void SideEnds(int i, out Vector2 a, out Vector2 b)
    {
        float h = arenaHalf;
        float e = 0.2f;
        switch (i)
        {
            case 0: a = new Vector2(-h, h - e); b = new Vector2(h, h - e); break;
            case 1: a = new Vector2(h - e, -h); b = new Vector2(h - e, h); break;
            case 2: a = new Vector2(-h, -h + e); b = new Vector2(h, -h + e); break;
            default: a = new Vector2(-h + e, -h); b = new Vector2(-h + e, h); break;
        }
    }

    // Ponto da parede do jogador "i" mais próximo de p (onde a bola passou).
    private Vector2 ClampToSide(Vector2 p, int i)
    {
        float h = arenaHalf - 0.1f;
        switch (i)
        {
            case 0: return new Vector2(Mathf.Clamp(p.x, -h, h), h);
            case 1: return new Vector2(h, Mathf.Clamp(p.y, -h, h));
            case 2: return new Vector2(Mathf.Clamp(p.x, -h, h), -h);
            default: return new Vector2(-h, Mathf.Clamp(p.y, -h, h));
        }
    }

    // ------------------------------------------------------------------ atualização visual (todo frame)

    private void Update()
    {
        if (!built) return;

        float now = Time.realtimeSinceStartup;
        float dt = Time.deltaTime;
        float fdt = fx != null ? fx.FxDelta : dt;

        if (audioFx != null) audioFx.masterVolume = volume;
        FitCamera();

        // Canhões: giram até o ângulo do servidor; a base acende enquanto mira.
        for (int i = 0; i < 4; i++)
        {
            if (cannonPivots[i] == null) continue;
            float t = 1f - Mathf.Exp(-18f * dt);
            cannonCurrentAngle[i] = Mathf.LerpAngle(cannonCurrentAngle[i], cannonTargetAngle[i], t);
            cannonPivots[i].localRotation = Quaternion.Euler(0f, 0f, cannonCurrentAngle[i]);

            bool charging = heldCannon == i;
            if (cannonBases[i] != null)
            {
                float pulse = charging ? 0.75f + 0.25f * Mathf.Sin(now * 30f) : 0f;
                cannonBases[i].color = charging
                    ? new Color(1f, 0.45f + 0.15f * pulse, 0.12f)
                    : CannonBaseColor;
            }
        }

        // Bumpers: deslizam até a posição do servidor, piscam quando acertados e avisam antes de mudar de lugar.
        bool warn = bumperMoveIn >= 0f && bumperMoveIn <= 1.2f;
        for (int i = 0; i < 4; i++)
        {
            if (bumperTransforms[i] == null) continue;

            Vector3 cur = bumperTransforms[i].position;
            Vector3 goal = new Vector3(bumperTarget[i].x, bumperTarget[i].y, 0f);
            bumperTransforms[i].position = (!bumpersSnapped || (goal - cur).sqrMagnitude > 4f)
                ? goal
                : Vector3.Lerp(cur, goal, 1f - Mathf.Exp(-22f * dt));

            bumperFlash[i] = Mathf.Max(0f, bumperFlash[i] - fdt);
            float f = bumperFlash[i] / 0.2f;
            float size = bumperRadius * 2f * (1f + 0.25f * f);
            bumperTransforms[i].localScale = new Vector3(size, size, 1f);

            if (bumperCores[i] != null)
                bumperCores[i].color = new Color(0.85f + 0.15f * f, 0.70f + 0.30f * f, 1f);
            if (bumperOuters[i] != null)
            {
                float blink = warn ? 0.55f + 0.45f * Mathf.Sin(now * 28f) : 1f;
                bumperOuters[i].color = new Color(BumperColor.r + 0.4f * f, BumperColor.g + 0.4f * f, BumperColor.b, blink);
            }
        }

        // Barreiras: aparecem com um "pop", piscam no fim.
        for (int i = 0; i < 4; i++)
        {
            if (barrierObjs[i] == null) continue;
            bool on = barrierRemain[i] > 0.01f;
            if (barrierObjs[i].activeSelf != on) barrierObjs[i].SetActive(on);
            if (!on) continue;

            bool horizontal = i == 0 || i == 2;
            float pop = Mathf.Clamp01((now - barrierPopAt[i]) / 0.25f);
            float k = pop * pop * (3f - 2f * pop);
            float overshoot = 1f + 0.25f * Mathf.Sin(pop * 3.1416f);
            float along = barrierLength * k;
            float thick = barrierThickness * overshoot;
            barrierObjs[i].transform.localScale = horizontal ? new Vector3(along, thick, 1f) : new Vector3(thick, along, 1f);

            if (barrierRenderers[i] != null)
            {
                Color c = barrierRenderers[i].color;
                float a = barrierRemain[i] < 1.5f ? 0.45f + 0.45f * Mathf.Sin(now * 22f) : 0.95f;
                barrierRenderers[i].color = new Color(c.r, c.g, c.b, a);
            }
        }

        // Power-up pulsa para chamar atenção.
        if (powerupObject != null && powerupObject.activeSelf)
        {
            float pulse = 1f + 0.15f * Mathf.Sin(now * 6f);
            float size = powerupRadius * 2f * pulse;
            powerupObject.transform.localScale = new Vector3(size, size, 1f);
        }

        // Bolas.
        if (mainBall != null) mainBall.Follow(dt, now);
        for (int k = 0; k < extraBalls.Length; k++)
        {
            if (extraBalls[k] == null) continue;
            extraBalls[k].Follow(dt, now);
        }

        for (int i = 0; i < 4; i++) pipFlash[i] = Mathf.Max(0f, pipFlash[i] - dt);
    }

    // ------------------------------------------------------------------ estado recebido

    private static bool Valid(NetMessage s)
    {
        return s.scores != null && s.scores.Length >= 4 && s.connected != null && s.connected.Length >= 4 &&
               s.eliminated != null && s.eliminated.Length >= 4 &&
               s.paddleXc != null && s.paddleXc.Length >= 4 && s.paddleYc != null && s.paddleYc.Length >= 4 &&
               s.paddleLenC != null && s.paddleLenC.Length >= 4 && s.shield != null && s.shield.Length >= 4 &&
               s.frozen != null && s.frozen.Length >= 4 && s.barrierC != null && s.barrierC.Length >= 4 &&
               s.cannonAngleD != null && s.cannonAngleD.Length >= 4 &&
               s.bumperXc != null && s.bumperXc.Length >= 4 && s.bumperYc != null && s.bumperYc.Length >= 4 &&
               s.bumperHits != null && s.bumperHits.Length >= 4 && s.paddleHits != null && s.paddleHits.Length >= 4 &&
               s.barrierHits != null && s.barrierHits.Length >= 4 && s.cannonShots != null && s.cannonShots.Length >= 4 &&
               s.extraXc != null && s.extraXc.Length >= 3 && s.extraYc != null && s.extraYc.Length >= 3 &&
               s.extraVxc != null && s.extraVxc.Length >= 3 && s.extraVyc != null && s.extraVyc.Length >= 3 &&
               s.extraLifeC != null && s.extraLifeC.Length >= 3;
    }

    private void OnStateReceived(NetMessage s)
    {
        if (!built || s == null || !Valid(s)) return;

        float now = Time.realtimeSinceStartup;
        Vector2 ballPos = new Vector2(NetMessage.F(s.ballXc), NetMessage.F(s.ballYc));
        Vector2 ballVel = new Vector2(NetMessage.F(s.ballVxc), NetMessage.F(s.ballVyc));
        float speed = ballVel.magnitude;

        for (int i = 0; i < 4; i++)
            paddlePos[i] = new Vector2(NetMessage.F(s.paddleXc[i]), NetMessage.F(s.paddleYc[i]));

        // ---- eventos (som / partículas): comparam este estado com o anterior
        if (evInit) DetectEvents(s, ballPos, speed, now);

        // ---- bolas
        lastHitter = s.lastHitter;
        boostActive = s.boostActive;

        if (mainBall != null)
        {
            mainBall.Push(ballPos, ballVel, now);
            Color bc = (lastHitter >= 0 && lastHitter < 4) ? colors[lastHitter] : Color.white;
            if (mainBall.sr != null) mainBall.sr.color = bc;
            if (mainBall.aura != null) mainBall.aura.gameObject.SetActive(boostActive);
            ConfigureTrail(mainBall, bc, speed, boostActive);
        }

        int extraCount = Mathf.Clamp(s.extraCount, 0, 3);
        for (int k = 0; k < extraBalls.Length; k++)
        {
            BallView e = extraBalls[k];
            if (e == null) continue;

            bool on = k < extraCount;
            e.SetActive(on);
            if (!on) continue;

            Vector2 ep = new Vector2(NetMessage.F(s.extraXc[k]), NetMessage.F(s.extraYc[k]));
            Vector2 ev = new Vector2(NetMessage.F(s.extraVxc[k]), NetMessage.F(s.extraVyc[k]));
            e.Push(ep, ev, now);

            // pisca no último segundo de vida
            float life = NetMessage.F(s.extraLifeC[k]);
            float alpha = life < 1.2f ? 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(now * 26f)) : 1f;
            if (e.sr != null) e.sr.color = new Color(ExtraBallColor.r, ExtraBallColor.g, ExtraBallColor.b, alpha);
            ConfigureTrail(e, ExtraBallColor, ev.magnitude, false);
        }

        // ---- canhões
        heldCannon = s.heldCannon;
        for (int i = 0; i < 4; i++) cannonTargetAngle[i] = s.cannonAngleD[i] / 10f;

        for (int i = 0; i < 4; i++)
        {
            if (cannonBarrels[i] == null) continue;
            bool holdingColor = heldCannon == i && lastHitter >= 0 && lastHitter < 4;
            for (int b = 0; b < cannonBarrels[i].Length; b++)
                if (cannonBarrels[i][b] != null)
                    cannonBarrels[i][b].color = holdingColor ? colors[lastHitter] : BarrelColor;
        }

        // ---- bumpers
        for (int i = 0; i < 4; i++)
            bumperTarget[i] = new Vector2(NetMessage.F(s.bumperXc[i]), NetMessage.F(s.bumperYc[i]));
        bumperMoveIn = s.bumperMoveInC < 0 ? -1f : NetMessage.F(s.bumperMoveInC);
        if (!bumpersSnapped)
        {
            for (int i = 0; i < 4; i++)
                if (bumperTransforms[i] != null)
                    bumperTransforms[i].position = new Vector3(bumperTarget[i].x, bumperTarget[i].y, 0f);
            bumpersSnapped = true;
        }

        // ---- power-up
        powerupActive = s.powerupActive;
        powerupType = Mathf.Clamp(s.powerupType, 0, PowerupNames.Length - 1);
        powerupPos = new Vector2(NetMessage.F(s.powerupXc), NetMessage.F(s.powerupYc));
        if (powerupObject != null)
        {
            powerupObject.SetActive(powerupActive);
            if (powerupActive) powerupObject.transform.position = new Vector3(powerupPos.x, powerupPos.y, 0f);
        }
        if (powerupBody != null) powerupBody.color = PowerupColors[powerupType];
        if (powerupActive) lastPowerupPos = powerupPos;

        // ---- barreiras
        for (int i = 0; i < 4; i++) barrierRemain[i] = NetMessage.F(s.barrierC[i]);

        // ---- jogadores / raquetes
        hudMyId = udpClient != null ? udpClient.PlayerId : -1;
        for (int i = 0; i < 4; i++)
        {
            hudScores[i] = s.scores[i];
            hudEliminated[i] = s.eliminated[i];
            hudConnected[i] = s.connected[i];

            bool out_ = s.eliminated[i];
            float len = NetMessage.F(s.paddleLenC[i]);

            if (paddles[i] != null)
            {
                if (paddles[i].gameObject.activeSelf == out_) paddles[i].gameObject.SetActive(!out_);
                paddles[i].SetServerPosition(paddlePos[i]);
                if (len > 0.1f) paddles[i].SetOrientation(i == 0 || i == 2, len, paddleThickness);   // raquete grande / mini
            }
            if (eliminatedWalls[i] != null && eliminatedWalls[i].activeSelf != out_) eliminatedWalls[i].SetActive(out_);
            if (shieldRings[i] != null) shieldRings[i].SetActive(s.shield[i] && !out_);
            if (myMarkers[i] != null) myMarkers[i].SetActive(i == hudMyId && !out_);

            if (paddleRenderers[i] != null)
            {
                Color c = colors[i];
                if (len < paddleLength * 0.9f)   // encolhida: avermelha
                    c = new Color(c.r * 0.55f + 0.45f, c.g * 0.55f + 0.45f * 0.3f, c.b * 0.55f + 0.45f * 0.25f, 1f);
                if (s.frozen[i])                 // congelada: azul-gelo
                    c = new Color(c.r * 0.3f + 0.7f * 0.75f, c.g * 0.3f + 0.7f * 0.95f, c.b * 0.3f + 0.7f, 1f);
                c.a = s.connected[i] ? 1f : 0.3f;   // raquete de quem não entrou fica apagada
                paddleRenderers[i].color = c;
            }
        }

        // ---- texto
        hudCountdown = s.countdown;
        hudPlaying = s.gameStarted && !s.gameOver;
        hudGameOver = s.gameOver;
        hudWinner = s.winnerId;

        if (s.gameOver)
        {
            if (s.winnerId >= 0) hudStatus = "P" + (s.winnerId + 1) + " VENCEU! Nova partida em instantes...";
            else hudStatus = "FIM DE JOGO";
        }
        else if (s.gameStarted)
        {
            hudStatus = boostActive ? "PARTIDA EM ANDAMENTO  -  BOLA TURBO!" : "PARTIDA EM ANDAMENTO";
        }
        else
        {
            int count = 0;
            for (int i = 0; i < 4; i++) if (s.connected[i]) count++;
            hudStatus = "Aguardando jogadores... (" + count + "/4)";
        }

        if (statusText != null) statusText.text = hudStatus;
        if (scoreText != null)
        {
            scoreText.text = "P1: " + s.scores[0] + "/" + maxPenalty + "   P2: " + s.scores[1] + "/" + maxPenalty +
                             "   P3: " + s.scores[2] + "/" + maxPenalty + "   P4: " + s.scores[3] + "/" + maxPenalty;
        }

        RecordBaseline(s, ballPos);
        evInit = true;
    }

    private void ConfigureTrail(BallView b, Color color, float speed, bool turbo)
    {
        if (b.trail == null) return;
        b.trail.emitting = effectsEnabled && b.active;
        float k = Mathf.InverseLerp(3f, 11f, speed);                // quanto mais rápida, maior o rastro
        b.trail.time = Mathf.Lerp(0.14f, 0.5f, k) * (turbo ? 1.25f : 1f);
        b.trail.startColor = new Color(color.r, color.g, color.b, 0.85f);
        b.trail.endColor = new Color(color.r, color.g, color.b, 0f);
    }

    private void RecordBaseline(NetMessage s, Vector2 ballPos)
    {
        for (int i = 0; i < 4; i++)
        {
            pScore[i] = s.scores[i];
            pElim[i] = s.eliminated[i];
            pShield[i] = s.shield[i];
            pPaddleHits[i] = s.paddleHits[i];
            pBarrierHits[i] = s.barrierHits[i];
            pCannonShots[i] = s.cannonShots[i];
            pBumperHits[i] = s.bumperHits[i];
            pBarrierRemain[i] = NetMessage.F(s.barrierC[i]);
            pPaddleLen[i] = NetMessage.F(s.paddleLenC[i]);
        }
        pCountdown = s.countdown;
        pHeld = s.heldCannon;
        pPickupSeq = s.pickupSeq;
        pGameOver = s.gameOver;
        pExtraCount = Mathf.Clamp(s.extraCount, 0, 3);
        for (int k = 0; k < 3; k++)
        {
            pExtraPos[k] = new Vector2(NetMessage.F(s.extraXc[k]), NetMessage.F(s.extraYc[k]));
            pExtraLife[k] = NetMessage.F(s.extraLifeC[k]);
        }
        pBallPos = ballPos;
    }

    // ------------------------------------------------------------------ eventos: som, partículas, tremor

    private void Sound(ClientAudio.Sfx sfx, float pitch = 1f, float vol = 1f)
    {
        if (soundEnabled && audioFx != null) audioFx.Play(sfx, pitch, vol);
    }

    private void ShowToast(string text, Color color, float seconds)
    {
        toast = text;
        toastColor = color;
        toastStart = Time.realtimeSinceStartup;
        toastUntil = toastStart + seconds;
    }

    // Bola (principal ou extra) mais próxima de um ponto: é onde a batida aconteceu.
    private Vector2 NearestBall(NetMessage s, Vector2 mainPos, Vector2 point)
    {
        Vector2 best = mainPos;
        float bestD = (mainPos - point).sqrMagnitude;
        int n = Mathf.Clamp(s.extraCount, 0, 3);
        for (int k = 0; k < n; k++)
        {
            Vector2 p = new Vector2(NetMessage.F(s.extraXc[k]), NetMessage.F(s.extraYc[k]));
            float d = (p - point).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private void DetectEvents(NetMessage s, Vector2 ballPos, float speed, float now)
    {
        float sp01 = Mathf.InverseLerp(4f, 11f, speed);

        // Nova partida: limpa efeitos que sobraram (confete, câmera lenta...).
        if (pGameOver && !s.gameOver && fx != null) fx.StopAll();

        // ---- rebates nas raquetes
        for (int i = 0; i < 4; i++)
        {
            if (s.paddleHits[i] == pPaddleHits[i]) continue;
            Vector2 at = NearestBall(s, ballPos, paddlePos[i]);
            if (fx != null)
            {
                fx.Burst(at, colors[i], 8 + (int)(speed * 0.8f), 3f + speed * 0.35f, 0.35f, 0.09f);
                fx.AddShake(Mathf.Lerp(0.04f, 0.16f, sp01));
            }
            Sound(ClientAudio.Sfx.PaddleHit, Mathf.Lerp(0.9f, 1.4f, sp01), i == hudMyId ? 1f : 0.8f);
        }

        // ---- rebates nas barreiras
        for (int i = 0; i < 4; i++)
        {
            if (s.barrierHits[i] == pBarrierHits[i]) continue;
            Vector2 center = SideCenter(i) + SideInward(i) * barrierInset;
            Vector2 at = NearestBall(s, ballPos, center);
            if (fx != null)
            {
                fx.Burst(at, new Color(0.9f, 0.95f, 1f), 9, 3.5f, 0.3f, 0.08f);
                fx.AddShake(0.07f);
            }
            Sound(ClientAudio.Sfx.BarrierHit, Random.Range(0.95f, 1.1f));
        }

        // ---- bumpers
        for (int i = 0; i < 4; i++)
        {
            if (s.bumperHits[i] == pBumperHits[i]) continue;
            bumperFlash[i] = 0.2f;
            Vector2 c = bumperTarget[i];
            if (fx != null)
            {
                fx.Burst(c, new Color(0.85f, 0.55f, 1f), 14, 4.5f, 0.45f, 0.1f);
                fx.Ring(c, new Color(0.8f, 0.5f, 1f), bumperRadius * 1.6f, bumperRadius * 4f, 0.35f);
                fx.AddShake(0.09f);
            }
            Sound(ClientAudio.Sfx.BumperHit, Mathf.Lerp(0.95f, 1.35f, sp01) * Random.Range(0.96f, 1.05f));
        }

        // ---- canhões
        if (s.heldCannon >= 0 && pHeld < 0)
        {
            Sound(ClientAudio.Sfx.CannonCharge);
            if (fx != null) fx.Ring(CannonCenter(s.heldCannon), new Color(1f, 0.6f, 0.2f), cannonRadius * 3f, cannonRadius * 0.6f, 0.6f);
        }
        for (int i = 0; i < 4; i++)
        {
            if (s.cannonShots[i] == pCannonShots[i]) continue;
            float ang = s.cannonAngleD[i] / 10f * Mathf.Deg2Rad;
            Vector2 tip = CannonCenter(i) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * cannonRadius * 1.6f;
            if (fx != null)
            {
                fx.Burst(tip, new Color(1f, 0.7f, 0.25f), 20, 6f, 0.45f, 0.11f);
                fx.Ring(tip, new Color(1f, 0.8f, 0.4f), 0.2f, 1.6f, 0.35f);
                fx.AddShake(0.22f);
            }
            Sound(ClientAudio.Sfx.CannonFire, Random.Range(0.95f, 1.08f));
        }

        // ---- contagem 3-2-1 e "VAI!"
        if (s.countdown != pCountdown)
        {
            if (s.countdown > 0)
            {
                countdownShownAt = now;
                Sound(ClientAudio.Sfx.Tick, s.countdown == 1 ? 1.3f : 1f);
            }
            else if (pCountdown > 0 && s.gameStarted && !s.gameOver)
            {
                goUntil = now + 0.7f;
                Sound(ClientAudio.Sfx.Go);
                if (fx != null)
                {
                    fx.Ring(Vector2.zero, Color.white, 0.5f, 4.5f, 0.6f);
                    fx.AddShake(0.1f);
                }
            }
        }

        // ---- penalidades / eliminações / escudo
        for (int i = 0; i < 4; i++)
        {
            bool newlyOut = s.eliminated[i] && !pElim[i];
            bool scored = s.scores[i] > pScore[i];

            if (newlyOut)
            {
                OnEliminated(i);
            }
            else if (scored)
            {
                Vector2 at = ClampToSide(pBallPos, i);
                pipFlash[i] = 0.9f;
                if (fx != null)
                {
                    fx.Burst(at, colors[i], 24, 5.5f, 0.6f, 0.12f);
                    fx.Ring(at, Color.white, 0.3f, 2.4f, 0.5f);
                    fx.AddShake(0.45f);
                }
                Sound(ClientAudio.Sfx.Penalty);
            }
            else if (pShield[i] && !s.shield[i] && !s.eliminated[i])
            {
                // escudo absorveu a penalidade
                Vector2 at = ClampToSide(pBallPos, i);
                if (fx != null)
                {
                    fx.Burst(at, new Color(0.45f, 0.75f, 1f), 22, 5f, 0.5f, 0.1f);
                    fx.Ring(paddlePos[i], new Color(0.45f, 0.75f, 1f), 0.6f, 3.2f, 0.45f);
                    fx.AddShake(0.25f);
                }
                Sound(ClientAudio.Sfx.ShieldBreak);
                pipFlash[i] = 0.5f;
            }
        }

        // ---- power-up coletado
        if (s.pickupSeq != pPickupSeq)
        {
            int t = Mathf.Clamp(s.pickupType, 0, PowerupNames.Length - 1);
            bool hasOwner = s.pickupPlayer >= 0 && s.pickupPlayer < 4;
            Color c = PowerupColors[t];

            if (fx != null)
            {
                fx.Burst(lastPowerupPos, c, 26, 4.5f, 0.7f, 0.12f);
                fx.Ring(lastPowerupPos, c, 0.3f, 2.2f, 0.5f);
                fx.AddShake(0.13f);
            }
            Sound(ClientAudio.Sfx.Pickup, 0.9f + 0.07f * t);

            string who = hasOwner ? "P" + (s.pickupPlayer + 1) + " pegou " : "";
            ShowToast(who + PowerupNames[t] + "!", hasOwner ? colors[s.pickupPlayer] : c, 2.4f);
        }

        // ---- efeitos visuais de raquete / barreira (encolher, crescer, barreira nova)
        for (int i = 0; i < 4; i++)
        {
            float len = NetMessage.F(s.paddleLenC[i]);
            if (fx != null && len < paddleLength * 0.9f && len < pPaddleLen[i] - 0.15f)
                fx.Ring(paddlePos[i], PowerupColors[3], 0.5f, 2.6f, 0.4f);             // encolheu
            if (fx != null && len > paddleLength * 1.1f && len > pPaddleLen[i] + 0.15f)
                fx.Ring(paddlePos[i], PowerupColors[1], 0.5f, 2.6f, 0.4f);             // cresceu

            float br = NetMessage.F(s.barrierC[i]);
            if (br > 0.01f && pBarrierRemain[i] <= 0.01f)
            {
                barrierPopAt[i] = now;
                if (fx != null)
                {
                    Vector2 center = SideCenter(i) + SideInward(i) * barrierInset;
                    fx.Burst(center, colors[i], 12, 3f, 0.4f, 0.09f);
                }
            }
        }

        // ---- bola extra que sumiu: "puf"
        int extraNow = Mathf.Clamp(s.extraCount, 0, 3);
        if (extraNow < pExtraCount && fx != null)
        {
            if (extraNow == 0)
            {
                for (int k = 0; k < pExtraCount; k++) fx.Burst(pExtraPos[k], ExtraBallColor, 10, 3f, 0.4f, 0.08f);
            }
            else
            {
                int gone = 0;
                for (int k = 1; k < pExtraCount; k++) if (pExtraLife[k] < pExtraLife[gone]) gone = k;
                fx.Burst(pExtraPos[gone], ExtraBallColor, 10, 3f, 0.4f, 0.08f);
            }
        }

        // ---- fim de partida
        if (s.gameOver && !pGameOver)
        {
            if (s.winnerId >= 0 && s.winnerId < 4)
            {
                Color wc = colors[s.winnerId];
                if (fx != null) fx.StartConfetti(4f, new Color[] { wc, Color.white, new Color(wc.r * 0.7f + 0.3f, wc.g * 0.7f + 0.3f, wc.b * 0.7f + 0.3f) });
                Sound(ClientAudio.Sfx.Win);
                ShowToast("P" + (s.winnerId + 1) + " VENCEU!", wc, 6f);
            }
            else
            {
                ShowToast("FIM DE JOGO", Color.white, 5f);
            }
        }
    }

    private void OnEliminated(int i)
    {
        Vector2 a, b;
        SideEnds(i, out a, out b);
        Vector2 inward = SideInward(i);
        Vector2 at = ClampToSide(pBallPos, i);

        if (fx != null)
        {
            fx.Line(a, b, inward, colors[i], 90, 6f, 1.4f, 0.16f);
            fx.Burst(at, Color.white, 30, 7f, 1f, 0.14f);
            fx.Ring(SideCenter(i) + inward * 0.5f, colors[i], 0.5f, 8f, 1.2f);
            fx.AddShake(1f);
            fx.SlowMo(1.5f, 0.28f);      // câmera lenta nas partículas e na câmera
            fx.ZoomPunch(0.12f);
        }
        Sound(ClientAudio.Sfx.Eliminate);
        ShowToast("P" + (i + 1) + " ELIMINADO!", colors[i], 2.8f);
        pipFlash[i] = 1.2f;
    }

    private void OnStatusChanged(string message)
    {
        statusMessage = message;
        if (statusText != null) statusText.text = message;
        Debug.Log(message);
    }

    // ------------------------------------------------------------------ HUD (OnGUI: funciona sem Canvas)

    private void DrawOutlined(Rect r, string text, GUIStyle style, Color color, float shadow)
    {
        Color keep = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
        GUI.Label(new Rect(r.x - shadow, r.y, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x + shadow, r.y, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x, r.y - shadow, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x, r.y + shadow, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = keep;
    }

    private void FillRect(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.Max(14, Screen.height / 36);
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        float h = style.fontSize * 1.6f;
        float y = 6f;
        float now = Time.realtimeSinceStartup;
        bool connected = udpClient != null && udpClient.IsConnected && udpClient.PlayerId >= 0;

        string me = connected
            ? "Você: P" + (udpClient.PlayerId + 1) + " (" + SideNames[udpClient.PlayerId] + ")  —  " + KeyHints[udpClient.PlayerId]
            : statusMessage;

        if (connected && hudMyId >= 0 && hudMyId < 4) style.normal.textColor = colors[hudMyId];
        GUI.Label(new Rect(10f, y, Screen.width - 20f, h), me, style);
        style.normal.textColor = Color.white;

        if (connected && hudMyId >= 0 && hudMyId < 4 && hudEliminated[hudMyId])
        {
            y += h;
            GUI.Label(new Rect(10f, y, Screen.width - 20f, h), "Você foi ELIMINADO — assista até o fim!", style);
        }

        if (!built)
        {
            y += h;
            GUI.Label(new Rect(10f, y, Screen.width - 20f, h), "Aguardando a configuração do servidor...", style);
            return;
        }

        if (statusText == null && connected)
        {
            y += h;
            GUI.Label(new Rect(10f, y, Screen.width - 20f, h), hudStatus, style);
        }

        GUIStyle small = new GUIStyle(style);
        small.fontSize = Mathf.Max(11, style.fontSize * 2 / 3);
        small.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        GUI.Label(new Rect(10f, Screen.height - small.fontSize * 1.8f, 300f, small.fontSize * 1.8f),
                  "M: " + ((audioFx != null && audioFx.muted) ? "som desligado" : "som ligado"), small);

        // Nome do power-up em cima dele.
        if (powerupObject != null && powerupObject.activeSelf && targetCamera != null)
        {
            Vector3 sp = targetCamera.WorldToScreenPoint(powerupObject.transform.position);
            GUIStyle label = new GUIStyle(style);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = Mathf.Max(12, style.fontSize * 3 / 4);
            label.normal.textColor = PowerupColors[powerupType];
            float lh = label.fontSize * 1.6f;
            GUI.Label(new Rect(sp.x - 100f, Screen.height - sp.y - powerupRadius * 60f - lh, 200f, lh), PowerupNames[powerupType], label);
        }

        if (connected) DrawLives(style);
        DrawBumperWarning(style);
        DrawCountdown(style, now);
        DrawToast(style, now);
    }

    // Vidas de cada jogador (quadradinhos). Cheio = ainda aguenta; apagado = penalidade levada.
    private void DrawLives(GUIStyle baseStyle)
    {
        int fs = Mathf.Max(13, baseStyle.fontSize * 4 / 5);
        float pip = fs * 0.62f;
        float gap = pip * 0.3f;
        float labelW = fs * 2.3f;
        float pipsW = Mathf.Max(maxPenalty * (pip + gap), fs * 5f);
        float groupW = labelW + pipsW + fs * 0.9f;
        float total = groupW * 4f;
        float x0 = (Screen.width - total) * 0.5f;
        float rowH = fs * 1.9f;
        float y = Screen.height - rowH - fs * 1.1f;

        GUIStyle ls = new GUIStyle(baseStyle);
        ls.fontSize = fs;
        ls.alignment = TextAnchor.MiddleLeft;

        for (int i = 0; i < 4; i++)
        {
            float x = x0 + groupW * i;
            Rect box = new Rect(x + 2f, y, groupW - 4f, rowH);
            FillRect(box, new Color(0f, 0f, 0f, 0.35f));
            if (i == hudMyId) FillRect(new Rect(box.x, box.y, box.width, 3f), colors[i]);

            bool flash = pipFlash[i] > 0f && Mathf.Sin(now() * 30f) > 0f;
            Color nameColor = hudEliminated[i] ? new Color(0.5f, 0.5f, 0.55f) : (hudConnected[i] ? colors[i] : new Color(colors[i].r, colors[i].g, colors[i].b, 0.35f));
            if (flash) nameColor = Color.white;
            ls.normal.textColor = nameColor;
            GUI.Label(new Rect(x + fs * 0.4f, y, labelW, rowH), "P" + (i + 1), ls);

            if (hudEliminated[i])
            {
                ls.normal.textColor = new Color(0.65f, 0.65f, 0.7f);
                GUI.Label(new Rect(x + labelW, y, pipsW, rowH), "ELIMINADO", ls);
                continue;
            }

            int lives = Mathf.Clamp(maxPenalty - hudScores[i], 0, maxPenalty);
            for (int k = 0; k < maxPenalty; k++)
            {
                bool filled = k < lives;
                Color c = filled ? colors[i] : new Color(colors[i].r, colors[i].g, colors[i].b, 0.16f);
                if (flash) c = filled ? Color.white : new Color(1f, 1f, 1f, 0.3f);
                FillRect(new Rect(x + labelW + k * (pip + gap), y + (rowH - pip) * 0.5f, pip, pip), c);
            }
        }
    }

    private static float now() { return Time.realtimeSinceStartup; }

    private void DrawBumperWarning(GUIStyle baseStyle)
    {
        if (bumperRadius <= 0.01f || bumperMoveIn <= 0f || bumperMoveIn > 3f || !hudPlaying || hudCountdown > 0) return;

        GUIStyle s = new GUIStyle(baseStyle);
        s.alignment = TextAnchor.MiddleCenter;
        s.fontSize = Mathf.Max(12, baseStyle.fontSize * 3 / 4);
        s.normal.textColor = new Color(0.85f, 0.6f, 1f, 0.9f);
        float rowH = s.fontSize * 1.8f;
        GUI.Label(new Rect(0f, Screen.height - rowH * 3.6f, Screen.width, rowH),
                  "BUMPERS MUDAM DE LUGAR EM " + Mathf.CeilToInt(bumperMoveIn), s);
    }

    private void DrawCountdown(GUIStyle baseStyle, float now)
    {
        GUIStyle big = new GUIStyle(baseStyle);
        big.alignment = TextAnchor.MiddleCenter;

        if (hudPlaying && hudCountdown > 0)
        {
            float t = now - countdownShownAt;
            float scale = 1f + 0.9f * Mathf.Exp(-t * 6f);          // "estoura" a cada número
            big.fontSize = (int)(Screen.height * 0.26f * scale);
            Color c = hudCountdown == 1 ? new Color(1f, 0.85f, 0.2f) : Color.white;
            DrawOutlined(new Rect(0f, 0f, Screen.width, Screen.height), hudCountdown.ToString(), big, c, Mathf.Max(2f, big.fontSize * 0.03f));
        }
        else if (now < goUntil)
        {
            float k = Mathf.Clamp01((goUntil - now) / 0.7f);
            big.fontSize = (int)(Screen.height * 0.2f * (1.4f - 0.4f * k));
            DrawOutlined(new Rect(0f, 0f, Screen.width, Screen.height), "VAI!", big, new Color(0.3f, 1f, 0.5f, k), Mathf.Max(2f, big.fontSize * 0.03f));
        }
    }

    private void DrawToast(GUIStyle baseStyle, float now)
    {
        if (now >= toastUntil || string.IsNullOrEmpty(toast)) return;

        float since = now - toastStart;
        float left = toastUntil - now;
        float alpha = Mathf.Clamp01(left / 0.5f);
        float pop = 1f + 0.35f * Mathf.Exp(-since * 9f);

        GUIStyle big = new GUIStyle(baseStyle);
        big.alignment = TextAnchor.MiddleCenter;
        big.fontSize = (int)(Mathf.Max(18, Screen.height / 16) * pop);
        Color c = new Color(toastColor.r, toastColor.g, toastColor.b, alpha);
        DrawOutlined(new Rect(0f, Screen.height * 0.18f, Screen.width, big.fontSize * 1.6f), toast, big, c, Mathf.Max(2f, big.fontSize * 0.04f));
    }
}
