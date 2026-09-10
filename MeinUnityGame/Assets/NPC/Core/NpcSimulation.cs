using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Village.Npc
{
    /// <summary>
    /// Event-boundary simulation shared by normal ticking and waiting.
    /// Priority: critical thirst, critical hunger, energy, work/home.
    /// Movement consumes game time along real routes, including interrupted journeys.
    /// </summary>
    public sealed class NpcSimulation
    {
        private const double Epsilon = 1e-9;
        private readonly WorkSchedule work;
        private readonly SupplySettings supplies;
        private readonly INpcNavigation navigation;
        private readonly double energyLoss, energyRecovery, sleepEnergy, wakeEnergy;
        private bool seekingRest, seekingFood, seekingWater;
        private NpcPoint[] route;
        private int nextPoint;
        private double serviceRemaining;
        private double commuteMinutes;

        public double TotalMinutes { get; private set; }
        public double Energy { get; private set; }
        public double Fatigue => 100d - Energy; // Compatibility for existing callers.
        public double Satiation { get; private set; }
        public double Hydration { get; private set; }
        public double RouteLength { get; }
        public double DistanceFromHome => NpcPoint.Distance(Position, navigation.GetPlace(NpcPlace.Home));
        public NpcPoint Position { get; private set; }
        public NpcPoint Facing { get; private set; }
        public NpcPlace Target { get; private set; }
        public NpcState State { get; private set; }
        public double WorkedMinutes { get; private set; }
        public double SleptMinutes { get; private set; }
        public double TravelledMetres { get; private set; }
        public double MealsCompleted { get; private set; }
        public double DrinksCompleted { get; private set; }
        public bool IsWorkTime => work.IsWorkTime(TotalMinutes);
        public bool NeedsRest => seekingRest;

        public NpcSimulation(double routeLength, WorkSchedule schedule, FatigueSettings settings)
            : this(new HomeWorkNavigation(routeLength), schedule, settings,
                new SupplySettings { satiationLossPerHour = 0d, hydrationLossPerHour = 0d }) { }

        public NpcSimulation(INpcNavigation navigation, WorkSchedule schedule,
            FatigueSettings settings, SupplySettings supplySettings)
        {
            if (navigation == null) throw new ArgumentNullException(nameof(navigation));
            schedule.Validate();
            settings.Validate();
            supplySettings.Validate();
            this.navigation = navigation;
            work = new WorkSchedule { startHour = schedule.startHour, endHour = schedule.endHour,
                commuteBeforeWork = schedule.commuteBeforeWork };
            supplies = new SupplySettings
            {
                initialSatiation = supplySettings.initialSatiation,
                initialHydration = supplySettings.initialHydration,
                satiationLossPerHour = supplySettings.satiationLossPerHour,
                hydrationLossPerHour = supplySettings.hydrationLossPerHour,
                hungerThreshold = supplySettings.hungerThreshold,
                thirstThreshold = supplySettings.thirstThreshold,
                eatingMinutes = supplySettings.eatingMinutes,
                drinkingMinutes = supplySettings.drinkingMinutes
            };
            Energy = 100d - settings.initialFatigue;
            energyLoss = settings.gainPerAwakeHour;
            energyRecovery = settings.recoveryPerSleepHour;
            sleepEnergy = 100d - settings.sleepThreshold;
            wakeEnergy = 100d - settings.wakeThreshold;
            Satiation = supplies.initialSatiation;
            Hydration = supplies.initialHydration;
            Position = navigation.GetPlace(NpcPlace.Home);
            Facing = new NpcPoint(0d, 0d, 1d);
            NpcPoint[] workRoute = navigation.FindRoute(Position, NpcPlace.Work);
            double workDistance = 0d;
            for (int i = 1; i < workRoute.Length; i++)
                workDistance += NpcPoint.Distance(workRoute[i - 1], workRoute[i]);
            RouteLength = workDistance;
            State = NpcState.Sleeping;
            seekingRest = Energy < wakeEnergy - Epsilon;
            Decide();
        }

        public void AdvanceTo(double targetMinutes, double metresPerGameMinute)
        {
            if (double.IsNaN(targetMinutes) || double.IsInfinity(targetMinutes) || targetMinutes < TotalMinutes)
                throw new ArgumentOutOfRangeException(nameof(targetMinutes));
            if (double.IsNaN(metresPerGameMinute) || double.IsInfinity(metresPerGameMinute) || metresPerGameMinute <= 0d)
                throw new ArgumentOutOfRangeException(nameof(metresPerGameMinute));
            // Opt-in: leave home early enough to cover the existing road route.
            // Default false preserves the departure behavior of existing NPCs.
            commuteMinutes = work.commuteBeforeWork
                ? Math.Min(RouteLength / metresPerGameMinute,
                    ((work.startHour - work.endHour + 24) % 24) * 60d) : 0d;
            var midnights = targetMinutes - TotalMinutes >= 2880d
                ? new Dictionary<string, Snapshot>() : null;
            while (targetMinutes - TotalMinutes > Epsilon)
            {
                Decide();
                // Only exactly repeated COMPLETE states may be skipped. Supplies,
                // destination, route and interrupted needs all affect future decisions.
                if (midnights != null && TotalMinutes % 1440d == 0d)
                {
                    string key = CycleKey();
                    if (midnights.TryGetValue(key, out Snapshot previous))
                    {
                        double period = TotalMinutes - previous.time;
                        double cycles = period > 0d ? Math.Floor((targetMinutes - TotalMinutes) / period) : 0d;
                        if (cycles > 0d)
                        {
                            WorkedMinutes += (WorkedMinutes - previous.worked) * cycles;
                            SleptMinutes += (SleptMinutes - previous.slept) * cycles;
                            TravelledMetres += (TravelledMetres - previous.travelled) * cycles;
                            MealsCompleted += (MealsCompleted - previous.meals) * cycles;
                            DrinksCompleted += (DrinksCompleted - previous.drinks) * cycles;
                            TotalMinutes += period * cycles;
                            midnights.Clear();
                            continue;
                        }
                    }
                    else if (midnights.Count < 4096)
                        midnights.Add(key, new Snapshot { time = TotalMinutes, worked = WorkedMinutes,
                            slept = SleptMinutes, travelled = TravelledMetres,
                            meals = MealsCompleted, drinks = DrinksCompleted });
                }

                double nextMidnight = (Math.Floor(TotalMinutes / 1440d) + 1d) * 1440d;
                double step = Math.Min(targetMinutes - TotalMinutes,
                    Math.Min(work.NextBoundary(TotalMinutes), nextMidnight) - TotalMinutes);
                if (commuteMinutes > 0d)
                {
                    double departure = Math.Floor(TotalMinutes / 1440d) * 1440d
                        + work.startHour * 60d - commuteMinutes;
                    if (departure <= TotalMinutes) departure += 1440d;
                    step = Math.Min(step, departure - TotalMinutes);
                }
                bool sleeping = State == NpcState.Sleeping;
                bool travelling = route != null && nextPoint < route.Length;
                bool eating = State == NpcState.Eating, drinking = State == NpcState.Drinking;
                double energyIn = sleeping ? (wakeEnergy - Energy) / (energyRecovery / 60d)
                    : !seekingRest ? (Energy - sleepEnergy) / (energyLoss / 60d) : double.PositiveInfinity;
                double foodIn = !seekingFood && supplies.satiationLossPerHour > 0d
                    ? (Satiation - supplies.hungerThreshold) / (supplies.satiationLossPerHour / 60d)
                    : double.PositiveInfinity;
                double waterIn = !seekingWater && supplies.hydrationLossPerHour > 0d
                    ? (Hydration - supplies.thirstThreshold) / (supplies.hydrationLossPerHour / 60d)
                    : double.PositiveInfinity;
                double arrivalIn = travelling ? NpcPoint.Distance(Position, route[nextPoint]) / metresPerGameMinute
                    : double.PositiveInfinity;
                step = Math.Min(step, Math.Min(Math.Min(energyIn, foodIn), Math.Min(waterIn, arrivalIn)));
                if (eating || drinking) step = Math.Min(step, serviceRemaining);
                if (step <= 0d || TotalMinutes + step == TotalMinutes)
                    throw new InvalidOperationException("NPC simulation could not advance.");

                if (State == NpcState.Working) WorkedMinutes += step;
                if (sleeping) SleptMinutes += step;
                Energy = Clamp(Energy + step * (sleeping ? energyRecovery : -energyLoss) / 60d);
                Satiation = Clamp(Satiation - step * supplies.satiationLossPerHour / 60d);
                Hydration = Clamp(Hydration - step * supplies.hydrationLossPerHour / 60d);
                if (step == energyIn) Energy = sleeping ? wakeEnergy : sleepEnergy;
                if (step == foodIn) Satiation = supplies.hungerThreshold;
                if (step == waterIn) Hydration = supplies.thirstThreshold;
                if (travelling)
                {
                    NpcPoint to = route[nextPoint];
                    double length = NpcPoint.Distance(Position, to);
                    double travelled = Math.Min(length, metresPerGameMinute * step);
                    Facing = new NpcPoint(to.X - Position.X, to.Y - Position.Y, to.Z - Position.Z);
                    Position = step == arrivalIn ? to : NpcPoint.Lerp(Position, to, travelled / length);
                    TravelledMetres += travelled;
                    if (step == arrivalIn) nextPoint++;
                }
                if (eating || drinking)
                {
                    serviceRemaining -= step;
                    if (serviceRemaining <= Epsilon)
                    {
                        if (eating) { Satiation = 100d; seekingFood = false; MealsCompleted++; }
                        if (drinking) { Hydration = 100d; seekingWater = false; DrinksCompleted++; }
                        serviceRemaining = 0d;
                        State = NpcState.Home; // Neutral until Decide re-evaluates current priorities.
                    }
                }
                TotalMinutes += step;
            }
            TotalMinutes = targetMinutes;
            Decide();
        }

        private void Decide()
        {
            if (Hydration <= supplies.thirstThreshold + Epsilon) seekingWater = true;
            if (Satiation <= supplies.hungerThreshold + Epsilon) seekingFood = true;
            if (Energy <= sleepEnergy + Epsilon) seekingRest = true;
            if (State == NpcState.Sleeping && Energy >= wakeEnergy - Epsilon) seekingRest = false;
            if (seekingWater) SetGoal(NpcPlace.Well, NpcState.GoingToDrink, NpcState.Drinking);
            else if (seekingFood) SetGoal(NpcPlace.Tavern, NpcState.GoingToEat, NpcState.Eating);
            else if (seekingRest) SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Sleeping);
            else if (work.IsWorkTime(TotalMinutes)) SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
            else if (IsCommuteTime()) SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.GoingToWork);
            else SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Home);
        }

        private bool IsCommuteTime()
        {
            if (commuteMinutes <= 0d) return false;
            double start = Math.Floor(TotalMinutes / 1440d) * 1440d + work.startHour * 60d;
            if (start <= TotalMinutes) start += 1440d;
            return TotalMinutes >= start - commuteMinutes;
        }

        private void SetGoal(NpcPlace destination, NpcState travellingState, NpcState arrivalState)
        {
            if (NpcPoint.Distance(Position, navigation.GetPlace(destination)) < Epsilon)
            {
                Position = navigation.GetPlace(destination);
                route = null;
                nextPoint = 0;
                if (State != arrivalState)
                    serviceRemaining = arrivalState == NpcState.Eating ? supplies.eatingMinutes
                        : arrivalState == NpcState.Drinking ? supplies.drinkingMinutes : 0d;
                Target = destination;
                State = arrivalState;
                return;
            }
            if (Target != destination || route == null || nextPoint >= route.Length)
            {
                route = navigation.FindRoute(Position, destination);
                if (route == null || route.Length < 2 ||
                    NpcPoint.Distance(route[0], Position) > 0.001d ||
                    NpcPoint.Distance(route[route.Length - 1], navigation.GetPlace(destination)) > 0.001d)
                    throw new InvalidOperationException("NPC route does not connect its current position to the destination.");
                // Keep the precise simulation position, avoiding a jump from float conversion.
                route[0] = Position;
                route[route.Length - 1] = navigation.GetPlace(destination);
                nextPoint = 1;
            }
            while (nextPoint < route.Length && NpcPoint.Distance(Position, route[nextPoint]) < Epsilon)
                nextPoint++;
            Target = destination;
            State = travellingState;
            serviceRemaining = 0d; // A higher-priority interruption cancels unfinished service.
            if (nextPoint < route.Length)
                Facing = new NpcPoint(route[nextPoint].X - Position.X,
                    route[nextPoint].Y - Position.Y, route[nextPoint].Z - Position.Z);
        }

        private string CycleKey()
        {
            var key = new StringBuilder();
            key.Append((int)State).Append('|').Append((int)Target).Append('|')
                .Append(seekingRest).Append('|').Append(seekingFood).Append('|').Append(seekingWater);
            foreach (double value in new[] { Energy, Satiation, Hydration, serviceRemaining,
                Position.X, Position.Y, Position.Z, Facing.X, Facing.Y, Facing.Z })
                key.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));
            if (route != null)
                for (int i = nextPoint; i < route.Length; i++)
                    key.Append('|').Append(route[i].X.ToString("R", CultureInfo.InvariantCulture))
                        .Append(',').Append(route[i].Y.ToString("R", CultureInfo.InvariantCulture))
                        .Append(',').Append(route[i].Z.ToString("R", CultureInfo.InvariantCulture));
            return key.ToString();
        }

        private static double Clamp(double value) => Math.Max(0d, Math.Min(100d, value));
        private struct Snapshot { public double time, worked, slept, travelled, meals, drinks; }
    }
}
