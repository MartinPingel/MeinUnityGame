using System;
using UnityEngine;

/// <summary>
/// Central clock for the village. One day lasts 120 real minutes by default.
/// Time is independent of Time.timeScale; use IsPaused to pause this clock.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class GameClock : MonoBehaviour
{
    private const double MinutesPerDay = 24d * 60d;
    private const double MaxTotalMinutes = (double)int.MaxValue * MinutesPerDay;

    [Header("Tageslaenge")]
    [Tooltip("Echtzeit-Minuten fuer 24 Spielstunden. 120 = 2 Stunden.")]
    [SerializeField, Min(0.01f)] private float realMinutesPerDay = 120f;

    [Header("Startzeit")]
    [SerializeField, Min(1)] private int startDay = 1;
    [SerializeField, Range(0, 23)] private int startHour;
    [SerializeField, Range(0, 59)] private int startMinute;

    [Header("Pause")]
    [SerializeField] private bool isPaused;

    private double totalGameMinutes;
    private double minuteRoundingError;
    private double lastRealTime;
    private bool initialized;

    /// <summary>Raised after each advance, with the old and new total game minutes.
    /// NPC schedules can use this interval to handle skipped hours or days.</summary>
    public event Action<double, double> TimeAdvanced;

    /// <summary>Raised once with the destination day when an advance crosses midnight.
    /// Multi-day skips do not replay this event for every intermediate day.</summary>
    public event Action<int> DayChanged;

    public double TotalGameMinutes
    {
        get
        {
            Initialize();
            return totalGameMinutes;
        }
    }

    public int CurrentDay => (int)Math.Floor(TotalGameMinutes / MinutesPerDay) + 1;
    public int CurrentHour => (int)(TotalGameMinutes % MinutesPerDay / 60d);
    public int CurrentMinute => (int)(TotalGameMinutes % 60d);

    /// <summary>Can be changed in the Inspector or at runtime without resetting the date.</summary>
    public float RealMinutesPerDay
    {
        get => realMinutesPerDay;
        set
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.01f)
                throw new ArgumentOutOfRangeException(nameof(value), "Day length must be at least 0.01 real minutes.");

            realMinutesPerDay = value;
        }
    }

    /// <summary>Stops automatic ticking. Explicit waiting/sleeping advances still work.</summary>
    public bool IsPaused
    {
        get => isPaused;
        set => isPaused = value;
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        // Do not catch up time spent with this component disabled.
        lastRealTime = Time.unscaledTimeAsDouble;
    }

    private void Update()
    {
        double now = Time.unscaledTimeAsDouble;
        double realSeconds = Math.Max(0d, now - lastRealTime);
        lastRealTime = now;

        if (!isPaused)
        {
            // 120 real minutes/day: 0.2 game minutes/second, or 60 in 300 seconds.
            AdvanceMinutes(realSeconds * 24d / realMinutesPerDay);
        }
    }

    /// <summary>Hook for a future wait/sleep system; fractions and multi-day skips are supported.</summary>
    public void AdvanceHours(double hours)
    {
        AdvanceMinutes(hours * 60d);
    }

    /// <summary>Advances forward and notifies listeners after the new date is available.</summary>
    public void AdvanceMinutes(double minutes)
    {
        if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0d)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Time advances must be finite and non-negative.");

        Initialize();
        if (minutes == 0d)
            return;

        // Compensated summation prevents tiny per-frame rounding errors accumulating.
        double adjustedMinutes = minutes - minuteRoundingError;
        double next = totalGameMinutes + adjustedMinutes;
        if (next >= MaxTotalMinutes)
            throw new ArgumentOutOfRangeException(nameof(minutes), "The resulting game day exceeds the supported range.");

        minuteRoundingError = (next - totalGameMinutes) - adjustedMinutes;
        if (next == totalGameMinutes)
            return;

        double previous = totalGameMinutes;
        int previousDay = CurrentDay;
        totalGameMinutes = next;
        int nextDay = CurrentDay;

        TimeAdvanced?.Invoke(previous, next);
        if (nextDay != previousDay)
            DayChanged?.Invoke(nextDay);
    }

    private void Initialize()
    {
        if (initialized)
            return;

        ValidateSettings();
        totalGameMinutes = ((double)startDay - 1d) * MinutesPerDay + startHour * 60d + startMinute;
        initialized = true;
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    private void ValidateSettings()
    {
        if (float.IsNaN(realMinutesPerDay) || float.IsInfinity(realMinutesPerDay))
            realMinutesPerDay = 120f;

        realMinutesPerDay = Mathf.Max(0.01f, realMinutesPerDay);
        startDay = Mathf.Max(1, startDay);
        startHour = Mathf.Clamp(startHour, 0, 23);
        startMinute = Mathf.Clamp(startMinute, 0, 59);
    }
}
