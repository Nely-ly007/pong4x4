using UnityEngine;

// Efeitos visuais do cliente: partículas (pool de sprites), tremor de câmera, câmera lenta e zoom.
// Tudo é só visual: o estado do jogo continua vindo do servidor, então a "câmera lenta" é aplicada
// apenas nas partículas e na câmera (nunca em Time.timeScale, que atrasaria o envio dos comandos).
public class ClientFX : MonoBehaviour
{
    private class Particle
    {
        public Transform t;
        public SpriteRenderer sr;
        public bool active;
        public Vector2 pos, vel;
        public float life, maxLife, sizeStart, sizeEnd, gravity, drag;
        public Color color;
        public bool ring;
    }

    private const int PoolSize = 360;
    private static readonly Vector3 CameraBase = new Vector3(0f, 0f, -10f);

    private Particle[] pool;
    private int next;
    private Sprite dotSprite;
    private Sprite ringSprite;
    private Material material;
    private Camera cam;
    private Transform root;

    private float trauma;
    private const float ShakeMax = 0.3f;

    private float slowTimer;
    private float slowScale = 1f;
    private float curScale = 1f;

    private float zoom = 1f;
    private float zoomTarget = 1f;

    private float confettiTimer;
    private float confettiAcc;
    private Color[] confettiColors;
    private float arenaHalf = 5f;

    public bool enabledFx = true;

    // Delta de tempo já com a câmera lenta aplicada (1 = normal).
    public float FxDelta { get; private set; }
    // Multiplicador do tamanho ortográfico da câmera (< 1 = zoom in).
    public float ZoomFactor { get { return zoom; } }

    public void Init(Sprite dot, Material mat, Camera camera, Transform parent, float arena)
    {
        dotSprite = dot;
        material = mat;
        cam = camera;
        arenaHalf = arena;
        FxDelta = Time.deltaTime;

        ringSprite = MakeRingSprite();

        root = new GameObject("FX").transform;
        root.SetParent(parent, false);

        pool = new Particle[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            GameObject go = new GameObject("p");
            go.transform.SetParent(root, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 30;
            if (material != null) sr.sharedMaterial = material;
            go.SetActive(false);

            pool[i] = new Particle { t = go.transform, sr = sr };
        }
    }

    private static Sprite MakeRingSprite()
    {
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - r * 0.88f) / (r * 0.1f));
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // ------------------------------------------------------------------ API

    public void Burst(Vector2 pos, Color color, int count, float speed, float life, float size, float gravity = 0f)
    {
        if (!enabledFx || pool == null) return;
        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * 6.2831853f;
            float spd = speed * Random.Range(0.35f, 1f);
            Vector2 v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spd;
            Spawn(pos, v, life * Random.Range(0.6f, 1.1f), size * Random.Range(0.6f, 1.2f), 0f, color, gravity, 1.8f, false);
        }
    }

    // Anel que se expande e some.
    public void Ring(Vector2 pos, Color color, float startSize, float endSize, float life)
    {
        if (!enabledFx || pool == null) return;
        Spawn(pos, Vector2.zero, life, startSize, endSize, color, 0f, 0f, true);
    }

    // Partículas espalhadas ao longo de um segmento (ex.: lado inteiro do jogador eliminado).
    public void Line(Vector2 a, Vector2 b, Vector2 push, Color color, int count, float speed, float life, float size)
    {
        if (!enabledFx || pool == null) return;
        for (int i = 0; i < count; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, Random.value);
            Vector2 v = push * speed * Random.Range(0.3f, 1f) + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * speed * 0.35f;
            Spawn(p, v, life * Random.Range(0.6f, 1.1f), size * Random.Range(0.7f, 1.3f), 0f, color, 0f, 1.5f, false);
        }
    }

    public void AddShake(float amount)
    {
        if (!enabledFx) return;
        trauma = Mathf.Min(1f, trauma + amount);
    }

    // Câmera lenta nos efeitos: "scale" = 0.3 significa 30% da velocidade, por "seconds" segundos.
    public void SlowMo(float seconds, float scale)
    {
        if (!enabledFx) return;
        slowTimer = Mathf.Max(slowTimer, seconds);
        slowScale = Mathf.Clamp(scale, 0.05f, 1f);
    }

    // Aproxima a câmera por um instante (amount 0.1 = 10% mais perto).
    public void ZoomPunch(float amount)
    {
        if (!enabledFx) return;
        zoomTarget = Mathf.Min(zoomTarget, 1f - Mathf.Clamp(amount, 0f, 0.4f));
    }

    public void StartConfetti(float seconds, Color[] colors)
    {
        if (!enabledFx) return;
        confettiTimer = seconds;
        confettiColors = colors;
    }

    public void StopAll()
    {
        confettiTimer = 0f;
        slowTimer = 0f;
        trauma = 0f;
        zoomTarget = 1f;
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i].active = false;
            pool[i].t.gameObject.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ interno

    private void Spawn(Vector2 pos, Vector2 vel, float life, float sizeStart, float sizeEnd, Color color, float gravity, float drag, bool ring)
    {
        Particle p = null;
        for (int k = 0; k < pool.Length; k++)
        {
            int idx = (next + k) % pool.Length;
            if (!pool[idx].active) { p = pool[idx]; next = (idx + 1) % pool.Length; break; }
        }
        if (p == null) { p = pool[next]; next = (next + 1) % pool.Length; }   // pool cheio: recicla o mais antigo

        p.active = true;
        p.pos = pos;
        p.vel = vel;
        p.life = life;
        p.maxLife = Mathf.Max(0.01f, life);
        p.sizeStart = sizeStart;
        p.sizeEnd = sizeEnd;
        p.gravity = gravity;
        p.drag = drag;
        p.color = color;
        p.ring = ring;

        p.sr.sprite = ring ? ringSprite : dotSprite;
        p.sr.color = color;
        p.t.position = new Vector3(pos.x, pos.y, 0f);
        p.t.localScale = new Vector3(sizeStart, sizeStart, 1f);
        p.t.gameObject.SetActive(true);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // câmera lenta: entra e sai suavemente
        float target = 1f;
        if (slowTimer > 0f) { slowTimer -= dt; target = slowScale; }
        curScale = Mathf.MoveTowards(curScale, target, dt * 4f);
        FxDelta = dt * curScale;

        if (pool == null) return;

        // confete (vitória)
        if (confettiTimer > 0f && confettiColors != null && confettiColors.Length > 0)
        {
            confettiTimer -= FxDelta;
            confettiAcc += 110f * FxDelta;
            while (confettiAcc >= 1f)
            {
                confettiAcc -= 1f;
                Vector2 p = new Vector2(Random.Range(-arenaHalf, arenaHalf), arenaHalf + 1.2f);
                Vector2 v = new Vector2(Random.Range(-0.8f, 0.8f), Random.Range(-2.6f, -1f));
                Color c = confettiColors[Random.Range(0, confettiColors.Length)];
                Spawn(p, v, 3.4f, 0.14f, 0.1f, c, 0.6f, 0.2f, false);
            }
        }

        for (int i = 0; i < pool.Length; i++)
        {
            Particle p = pool[i];
            if (!p.active) continue;

            p.life -= FxDelta;
            if (p.life <= 0f)
            {
                p.active = false;
                p.t.gameObject.SetActive(false);
                continue;
            }

            p.vel = p.vel / (1f + p.drag * FxDelta);
            p.vel.y -= p.gravity * FxDelta;
            p.pos += p.vel * FxDelta;

            float k = p.life / p.maxLife;                         // 1 -> 0
            float size = Mathf.Lerp(p.sizeEnd, p.sizeStart, k);
            float alpha = p.ring ? k : Mathf.Clamp01(k * 1.6f);

            p.t.position = new Vector3(p.pos.x, p.pos.y, 0f);
            p.t.localScale = new Vector3(size, size, 1f);
            p.sr.color = new Color(p.color.r, p.color.g, p.color.b, p.color.a * alpha);
        }
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        float dt = Time.deltaTime;
        trauma = Mathf.Max(0f, trauma - dt * 1.6f);

        float shake = trauma * trauma * ShakeMax;
        float time = Time.time * 32f;
        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * shake,
            (Mathf.PerlinNoise(0.71f, time) - 0.5f) * 2f * shake, 0f);
        cam.transform.position = CameraBase + offset;

        zoom = Mathf.Lerp(zoom, zoomTarget, 1f - Mathf.Exp(-7f * FxDelta));
        zoomTarget = Mathf.Lerp(zoomTarget, 1f, 1f - Mathf.Exp(-1.6f * FxDelta));
    }
}
