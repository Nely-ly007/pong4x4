using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Sons gerados POR CÓDIGO (síntese simples): não precisa importar nenhum arquivo de áudio.
// Tecla M liga/desliga o som. O ClientGameView cria este componente sozinho.
public class ClientAudio : MonoBehaviour
{
    public enum Sfx
    {
        PaddleHit, BumperHit, BarrierHit, CannonCharge, CannonFire, Pickup,
        Penalty, Eliminate, Tick, Go, Win, ShieldBreak
    }

    [Range(0f, 1f)] public float masterVolume = 0.7f;
    public bool muted;

    private const int SampleRate = 22050;
    private const int VoiceCount = 10;
    private const float TwoPi = 6.2831853f;

    private AudioClip[] clips;
    private AudioSource[] voices;
    private int nextVoice;
    private readonly System.Random rng = new System.Random(7);

    private delegate float Generator(float t);

    private void Awake()
    {
        BuildClips();

        voices = new AudioSource[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            voices[i] = src;
        }
    }

    private void Update()
    {
        if (MutePressed())
        {
            muted = !muted;
            if (muted && voices != null)
                for (int i = 0; i < voices.Length; i++) voices[i].Stop();
        }
    }

    private static bool MutePressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Keyboard kb = Keyboard.current;
        return kb != null && kb.mKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.M);
#endif
    }

    // pitch: 1 = normal. volume: 0..1 (multiplica o volume geral).
    public void Play(Sfx sfx, float pitch = 1f, float volume = 1f)
    {
        if (muted || clips == null || voices == null) return;

        AudioSource v = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;

        v.clip = clips[(int)sfx];
        v.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        v.volume = Mathf.Clamp01(masterVolume * volume);
        v.Play();
    }

    // ------------------------------------------------------------------ síntese

    private static float Sine(float phase) { return Mathf.Sin(phase); }
    private static float Square(float phase) { return Mathf.Sin(phase) >= 0f ? 1f : -1f; }

    // Fase de uma varredura linear de frequência f0 -> f1 durante "dur" segundos.
    private static float SweepPhase(float t, float f0, float f1, float dur)
    {
        return TwoPi * (f0 * t + (f1 - f0) * t * t / (2f * dur));
    }

    private float Noise() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

    private AudioClip Make(string name, float seconds, Generator gen)
    {
        int n = Mathf.Max(1, (int)(seconds * SampleRate));
        float[] data = new float[n];
        int fadeIn = Mathf.Max(1, (int)(0.003f * SampleRate));
        int fadeOut = Mathf.Max(1, (int)(0.012f * SampleRate));

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float s = gen(t);
            if (i < fadeIn) s *= i / (float)fadeIn;                 // sem "clique" no começo
            if (i > n - fadeOut) s *= (n - i) / (float)fadeOut;     // nem no fim
            data[i] = Mathf.Clamp(s, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void BuildClips()
    {
        clips = new AudioClip[Enum.GetValues(typeof(Sfx)).Length];

        // Rebate na raquete: "pong" curto e seco.
        clips[(int)Sfx.PaddleHit] = Make("PaddleHit", 0.11f, t =>
            0.8f * Sine(SweepPhase(t, 520f, 360f, 0.11f)) * Mathf.Exp(-t * 26f)
          + 0.2f * Square(SweepPhase(t, 1040f, 720f, 0.11f)) * Mathf.Exp(-t * 70f));

        // Bumper: "boing" que sobe.
        clips[(int)Sfx.BumperHit] = Make("BumperHit", 0.2f, t =>
            0.7f * Sine(SweepPhase(t, 300f, 1000f, 0.2f)) * Mathf.Exp(-t * 13f)
          + 0.3f * Sine(SweepPhase(t, 600f, 2000f, 0.2f)) * Mathf.Exp(-t * 22f));

        // Barreira: batida metálica.
        clips[(int)Sfx.BarrierHit] = Make("BarrierHit", 0.13f, t =>
            0.45f * Square(TwoPi * 700f * t) * Mathf.Exp(-t * 32f)
          + 0.4f * Sine(TwoPi * 1050f * t) * Mathf.Exp(-t * 24f)
          + 0.15f * Noise() * Mathf.Exp(-t * 60f));

        // Canhão carregando: zumbido que sobe e vibra.
        clips[(int)Sfx.CannonCharge] = Make("CannonCharge", 0.8f, t =>
        {
            float k = t / 0.8f;
            float trem = 1f + 0.4f * Sine(TwoPi * 18f * t);
            return (0.18f + 0.38f * k) * trem * Sine(SweepPhase(t, 140f, 520f, 0.8f)) + 0.04f * Noise();
        });

        // Tiro do canhão: estouro + queda de frequência.
        clips[(int)Sfx.CannonFire] = Make("CannonFire", 0.38f, t =>
            0.6f * Noise() * Mathf.Exp(-t * 14f)
          + 0.9f * Sine(SweepPhase(t, 240f, 55f, 0.38f)) * Mathf.Exp(-t * 7f));

        // Power-up: arpejo C-E-G.
        clips[(int)Sfx.Pickup] = Make("Pickup", 0.34f, t =>
        {
            float f = t < 0.1f ? 523f : (t < 0.2f ? 659f : 784f);
            float start = t < 0.1f ? 0f : (t < 0.2f ? 0.1f : 0.2f);
            return 0.6f * Sine(TwoPi * f * t) * Mathf.Exp(-(t - start) * 8f)
                 + 0.15f * Sine(TwoPi * f * 2f * t) * Mathf.Exp(-(t - start) * 12f);
        });

        // Penalidade: buzina grave.
        clips[(int)Sfx.Penalty] = Make("Penalty", 0.42f, t =>
            0.5f * Square(SweepPhase(t, 130f, 70f, 0.42f)) * Mathf.Exp(-t * 4.5f)
          + 0.25f * Sine(SweepPhase(t, 260f, 140f, 0.42f)) * Mathf.Exp(-t * 6f));

        // Eliminação: explosão longa.
        clips[(int)Sfx.Eliminate] = Make("Eliminate", 1.1f, t =>
            0.55f * Noise() * Mathf.Exp(-t * 3.2f)
          + 0.95f * Sine(SweepPhase(t, 110f, 30f, 1.1f)) * Mathf.Exp(-t * 2.2f));

        // Contagem 3-2-1: bip.
        clips[(int)Sfx.Tick] = Make("Tick", 0.13f, t =>
            0.8f * Sine(TwoPi * 660f * t) * Mathf.Exp(-t * 20f));

        // "VAI!": bip agudo que sobe.
        clips[(int)Sfx.Go] = Make("Go", 0.38f, t =>
            0.75f * Sine(SweepPhase(t, 880f, 1320f, 0.38f)) * Mathf.Exp(-t * 5.5f)
          + 0.25f * Sine(SweepPhase(t, 1760f, 2640f, 0.38f)) * Mathf.Exp(-t * 8f));

        // Vitória: fanfarra C-E-G-C.
        clips[(int)Sfx.Win] = Make("Win", 1.6f, t =>
        {
            float[] freqs = { 523f, 659f, 784f, 1047f };
            float sum = 0f;
            for (int i = 0; i < freqs.Length; i++)
            {
                float start = i * 0.18f;
                if (t < start) continue;
                float lt = t - start;
                sum += 0.4f * (Sine(TwoPi * freqs[i] * lt) + 0.3f * Sine(TwoPi * freqs[i] * 2f * lt)) * Mathf.Exp(-lt * 2.2f);
            }
            return sum;
        });

        // Escudo quebrando: vidro.
        clips[(int)Sfx.ShieldBreak] = Make("ShieldBreak", 0.32f, t =>
            0.5f * Noise() * Mathf.Exp(-t * 13f)
          + 0.6f * Sine(SweepPhase(t, 1400f, 300f, 0.32f)) * Mathf.Exp(-t * 11f));
    }
}
