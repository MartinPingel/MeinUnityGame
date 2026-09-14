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
    private static readonly string[] WeekdayNames =
        { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };

    /// <summary>Day 1 is Monday. Derived from the game day, including any number of skipped days.</summary>
    public DayOfWeek CurrentWeekday => (DayOfWeek)(CurrentDay % 7);
    public string CurrentWeekdayName => GetWeekdayName(CurrentDay);

    public static string GetWeekdayName(int gameDay)
    {
        if (gameDay < 1) throw new ArgumentOutOfRangeException(nameof(gameDay));
        return WeekdayNames[(gameDay - 1) % 7];
    }

    private static readonly string[] MonthNames =
        { "Januar", "Februar", "März", "April", "Mai", "Juni",
          "Juli", "August", "September", "Oktober", "November", "Dezember" };
    private static readonly int[] MonthLengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    private const int DaysPerYear = 365; // Fixed calendar: February always has 28 days.

    public readonly struct CalendarDate
    {
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }
        public string MonthName => MonthNames[Month - 1];
        internal CalendarDate(int year, int month, int day)
        { Year = year; Month = month; Day = day; }
    }

    /// <summary>Game day 1 is January 1, year 1. CurrentDay remains the absolute simulation day.</summary>
    public CalendarDate CurrentDate => GetCalendarDate(CurrentDay);
    public int CurrentYear => CurrentDate.Year;
    public int CurrentMonth => CurrentDate.Month;
    public int CurrentDayOfMonth => CurrentDate.Day;
    public string CurrentMonthName => CurrentDate.MonthName;

    /// <summary>Derives the date directly, including multi-year skips. Does not reset the weekday.</summary>
    public static CalendarDate GetCalendarDate(int gameDay)
    {
        if (gameDay < 1) throw new ArgumentOutOfRangeException(nameof(gameDay));
        int elapsedDays = gameDay - 1;
        int year = elapsedDays / DaysPerYear + 1;
        int dayOfYear = elapsedDays % DaysPerYear;
        int month = 0;
        while (dayOfYear >= MonthLengths[month])
        {
            dayOfYear -= MonthLengths[month];
            month++;
        }
        return new CalendarDate(year, month + 1, dayOfYear + 1);
    }

    /// <summary>Shared by the live clock, wait preview and NPC debug time display.</summary>
    public static string FormatCalendarTime(double totalMinutes)
    {
        if (double.IsNaN(totalMinutes) || double.IsInfinity(totalMinutes) ||
            totalMinutes < 0d || totalMinutes >= MaxTotalMinutes)
            throw new ArgumentOutOfRangeException(nameof(totalMinutes));
        int gameDay = (int)Math.Floor(totalMinutes / MinutesPerDay) + 1;
        CalendarDate date = GetCalendarDate(gameDay);
        int hour = (int)(totalMinutes % MinutesPerDay / 60d);
        int minute = (int)(totalMinutes % 60d);
        return $"Tag {date.Day} – {date.MonthName} – Jahr {date.Year} – {GetWeekdayName(gameDay)} – {hour:00}:{minute:00}";
    }

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

