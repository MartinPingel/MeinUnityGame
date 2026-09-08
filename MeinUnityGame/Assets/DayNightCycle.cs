using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lighting presentation driven only by GameClock, including fractional game minutes.
/// Sun times and Daylight01 provide extension points for seasons, moonlight and weather.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(GameClock))]
public sealed class DayNightCycle : MonoBehaviour
{
    [Header("Verbindungen")]
    [SerializeField] private GameClock clock;
    [SerializeField] private Light sun;

    [Header("Sonnenzeiten (Spielstunden)")]
    [SerializeField, Range(0f, 23.99f)] private float sunriseHour = 6f;
    [SerializeField, Range(0f, 23.99f)] private float sunsetHour = 18f;
    [Tooltip("Aufhellen nach Sonnenaufgang / Abdunkeln vor Sonnenuntergang.")]
    [SerializeField, Range(0.1f, 6f)] private float transitionHours = 2f;

    [Header("Sonnenlicht")]
    [SerializeField, Min(0f)] private float daySunIntensity = 2f;
    [SerializeField] private Color daySunColor = new Color(1f, 0.96f, 0.9f);
    [SerializeField] private Color horizonSunColor = new Color(1f, 0.5f, 0.25f);

    [Header("Nachthelligkeit (Anteil der Tageshelligkeit)")]
    [SerializeField, Range(0f, 1f)] private float nightAmbientFraction = 0.04f;
    [SerializeField, Range(0f, 1f)] private float nightSkyFraction = 0.01f;
    [SerializeField, Range(0f, 1f)] private float nightReflectionFraction = 0.02f;

    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

    private bool captured;
    private Quaternion originalSunRotation;
    private float originalSunIntensity;
    private Color originalSunColor;
    private bool originalUseColorTemperature;
    private float sunYaw;
    private Light originalRenderSun;
    private AmbientMode originalAmbientMode;
    private Color originalAmbientSky;
    private Color originalAmbientEquator;
    private Color originalAmbientGround;
    private float originalReflectionIntensity;
    private Material originalSkybox;
    private Material runtimeSkybox;
    private float originalSkyExposure;

    public float Daylight01 { get; private set; }
    public float SunriseHour => sunriseHour;
    public float SunsetHour => sunsetHour;

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (clock == null)
            clock = GetComponent<GameClock>();

        if (clock == null || sun == null || sun.type != LightType.Directional)
        {
            Debug.LogError("DayNightCycle needs a GameClock and the scene's Directional Light.", this);
            enabled = false;
            return;
        }

        OnValidate();
        originalSunRotation = sun.transform.rotation;
        originalSunIntensity = sun.intensity;
        originalSunColor = sun.color;
        originalUseColorTemperature = sun.useColorTemperature;
        sunYaw = sun.transform.eulerAngles.y;
        originalRenderSun = RenderSettings.sun;
        originalAmbientMode = RenderSettings.ambientMode;
        originalAmbientSky = RenderSettings.ambientSkyColor;
        originalAmbientEquator = RenderSettings.ambientEquatorColor;
        originalAmbientGround = RenderSettings.ambientGroundColor;
        originalReflectionIntensity = RenderSettings.reflectionIntensity;
        originalSkybox = RenderSettings.skybox;
        captured = true;

        // Work on a temporary copy, never on the shared skybox material.
        if (originalSkybox != null && originalSkybox.HasProperty(ExposureId))
        {
            runtimeSkybox = new Material(originalSkybox)
            {
                name = originalSkybox.name + " (DayNight runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            originalSkyExposure = runtimeSkybox.GetFloat(ExposureId);
            RenderSettings.skybox = runtimeSkybox;
        }

        RenderSettings.sun = sun;
        // Explicit ambient colors can follow time without repeatedly baking sky lighting.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        sun.useColorTemperature = false;
        ApplyLighting();
    }

    private void LateUpdate()
    {
        if (captured && clock != null && sun != null)
            ApplyLighting();
    }

    /// <summary>Future seasons may change the sun times without altering GameClock.
    /// Times wrap around midnight; at least 0.1 hours of both day and night are required.</summary>
    public void SetSunTimes(float sunrise, float sunset)
    {
        if (float.IsNaN(sunrise) || float.IsInfinity(sunrise) ||
            float.IsNaN(sunset) || float.IsInfinity(sunset))
            throw new ArgumentOutOfRangeException(nameof(sunrise), "Sun times must be finite.");

        float rise = Mathf.Repeat(sunrise, 24f);
        float set = Mathf.Repeat(sunset, 24f);
        float length = Mathf.Repeat(set - rise, 24f);
        if (length < 0.1f || length > 23.9f)
            throw new ArgumentOutOfRangeException(nameof(sunset), "Day and night must each last at least 0.1 hours.");

        sunriseHour = rise;
        sunsetHour = set;
    }

    /// <summary>Continuous daylight weight, also usable by future lighting features.</summary>
    public float EvaluateDaylight(float gameHour)
    {
        float dayHours = Mathf.Repeat(sunsetHour - sunriseHour, 24f);
        float sinceRise = Mathf.Repeat(gameHour - sunriseHour, 24f);
        if (sinceRise >= dayHours)
            return 0f;

        float fadeHours = Mathf.Min(transitionHours, dayHours * 0.5f);
        float dawn = Mathf.SmoothStep(0f, 1f, sinceRise / fadeHours);
        float dusk = Mathf.SmoothStep(0f, 1f, (dayHours - sinceRise) / fadeHours);
        return Mathf.Min(dawn, dusk);
    }

    private void ApplyLighting()
    {
        // Do not use integer CurrentHour/CurrentMinute: they would cause visible steps.
        float hour = (float)((clock.TotalGameMinutes % 1440d) / 60d);
        Daylight01 = EvaluateDaylight(hour);

        float dayHours = Mathf.Repeat(sunsetHour - sunriseHour, 24f);
        float sinceRise = Mathf.Repeat(hour - sunriseHour, 24f);
        float angle = sinceRise < dayHours
            ? 180f * sinceRise / dayHours
            : 180f + 180f * (sinceRise - dayHours) / (24f - dayHours);
        sun.transform.rotation = Quaternion.Euler(angle, sunYaw, 0f);
        sun.intensity = daySunIntensity * Daylight01;
        sun.color = Color.Lerp(horizonSunColor, daySunColor, Daylight01);

        float ambient = Mathf.Lerp(nightAmbientFraction, 1f, Daylight01);
        RenderSettings.ambientSkyColor = originalAmbientSky * ambient;
        RenderSettings.ambientEquatorColor = originalAmbientEquator * ambient;
        RenderSettings.ambientGroundColor = originalAmbientGround * ambient;
        RenderSettings.reflectionIntensity = originalReflectionIntensity *
            Mathf.Lerp(nightReflectionFraction, 1f, Daylight01);

        if (runtimeSkybox != null)
            runtimeSkybox.SetFloat(ExposureId, originalSkyExposure *
                Mathf.Lerp(nightSkyFraction, 1f, Daylight01));
    }

    private void OnDisable()
    {
        if (!captured)
            return;

        if (sun != null)
        {
            sun.transform.rotation = originalSunRotation;
            sun.intensity = originalSunIntensity;
            sun.color = originalSunColor;
            sun.useColorTemperature = originalUseColorTemperature;
        }

        RenderSettings.sun = originalRenderSun;
        RenderSettings.ambientMode = originalAmbientMode;
        RenderSettings.ambientSkyColor = originalAmbientSky;
        RenderSettings.ambientEquatorColor = originalAmbientEquator;
        RenderSettings.ambientGroundColor = originalAmbientGround;
        RenderSettings.reflectionIntensity = originalReflectionIntensity;
        if (runtimeSkybox != null)
        {
            if (RenderSettings.skybox == runtimeSkybox)
                RenderSettings.skybox = originalSkybox;
            Destroy(runtimeSkybox);
            runtimeSkybox = null;
        }
        captured = false;
    }

    private void OnValidate()
    {
        sunriseHour = ValidRange(sunriseHour, 6f, 0f, 23.99f);
        sunsetHour = ValidRange(sunsetHour, 18f, 0f, 23.99f);
        float length = Mathf.Repeat(sunsetHour - sunriseHour, 24f);
        if (length < 0.1f || length > 23.9f)
            sunsetHour = Mathf.Repeat(sunriseHour + 12f, 24f);

        transitionHours = ValidRange(transitionHours, 2f, 0.1f, 6f);
        daySunIntensity = ValidRange(daySunIntensity, 2f, 0f, float.MaxValue);
        nightAmbientFraction = ValidRange(nightAmbientFraction, 0.04f, 0f, 1f);
        nightSkyFraction = ValidRange(nightSkyFraction, 0.01f, 0f, 1f);
        nightReflectionFraction = ValidRange(nightReflectionFraction, 0.02f, 0f, 1f);
    }

    private static float ValidRange(float value, float fallback, float min, float max)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
