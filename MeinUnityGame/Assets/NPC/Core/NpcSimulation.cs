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
        private double lastWaiterClosingRest = -1d;
        // extendForBreaks bookkeeping: minutes owed for the current shift, and the worked-minutes
        // baseline captured at its start. Unused (stays 0/false) unless a WorkSchedule opts in.
        private readonly double dailyTargetMinutes;
        // Negative infinity until the first real shift-start transition: makes the "still
        // owes work" check false before any shift has ever begun.
        private double shiftWorkBaseline = double.NegativeInfinity;
        private bool wasWorkWindow;
        private NpcPoint[] route;
        private int nextPoint;
        private double serviceRemaining;
        private double commuteMinutes;
        private readonly Func<bool> consumeFood, consumeDrink;
        private readonly INpcDeliveryInventory deliveryInventory;
        private readonly double minutesPerUnit;
        private readonly int deliveryQuantity;
        private double productionRemaining;
        private readonly INpcWorkEquipment workEquipment;
        private readonly INpcVisitRoute visitRoute;
        private readonly NpcSocialVenue socialVenue;
        private readonly NpcSocialSettings socialSettings;
        private bool seekingSocial;
        private int socialSlot = -1;
        private readonly NpcSocialVenue churchVenue;
        private readonly WorkSchedule churchSchedule;
        private int churchSlot = -1;
        public int ChurchSlot => churchSlot;
        public double Social { get; private set; } = 100d;
        public bool NeedsSocial => seekingSocial;
        public int SocialSlot => socialSlot;
        internal NpcSocialVenue SocialVenue => socialVenue;
        private bool IsInnkeeper => TavernService != null && TavernService.IsWaiter(this);
        internal bool IsPresentTavernGuest => !IsInnkeeper &&
            ((AtSocialPlace && (State == NpcState.WaitingForCompany || State == NpcState.Socialising ||
                State == NpcState.WaitingForService)) ||
             (socialVenue != null && State == NpcState.WaitingForSocialPlace &&
                NpcPoint.Distance(Position, socialVenue.Entrance) < Epsilon));
        internal bool IsDoingInnkeeperWork => IsInnkeeper && work.IsWorkTime(TotalMinutes) &&
            (State == NpcState.Working || State == NpcState.GoingToServicePickup || State == NpcState.ServingGuest);
        internal bool SocialParticipant => !IsInnkeeper && !work.IsWorkTime(TotalMinutes) && socialSlot >= 0 &&
            (State == NpcState.WaitingForCompany || State == NpcState.Socialising) &&
            !seekingFood && !seekingWater && !seekingRest;

        public int ToolCargoQuantity { get; private set; }
        public bool WorkBlockedByTool => workEquipment != null && !workEquipment.CanWork;
        public int CargoQuantity { get; private set; }
        public double ProducedUnits { get; private set; }
        public double DeliveredUnits { get; private set; }

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
        public bool NeedsFood => seekingFood;
        public bool NeedsDrink => seekingWater;
        public bool NeedsRest => seekingRest;

        public NpcSimulation(double routeLength, WorkSchedule schedule, FatigueSettings settings)
            : this(new HomeWorkNavigation(routeLength), schedule, settings,
                new SupplySettings { satiationLossPerHour = 0d, hydrationLossPerHour = 0d }) { }

        public NpcSimulation(INpcNavigation navigation, WorkSchedule schedule,
            FatigueSettings settings, SupplySettings supplySettings,
            Func<bool> consumeFood = null, Func<bool> consumeDrink = null,
            INpcDeliveryInventory deliveryInventory = null, WorkDeliverySettings deliverySettings = null,
            INpcWorkEquipment workEquipment = null,
            NpcSocialVenue socialVenue = null, NpcSocialSettings socialSettings = null,
            INpcVisitRoute visitRoute = null,
            NpcSocialVenue churchVenue = null, WorkSchedule churchSchedule = null)
        {
            if (navigation == null) throw new ArgumentNullException(nameof(navigation));
            schedule.Validate();
            settings.Validate();
            supplySettings.Validate();
            this.navigation = navigation;
            this.consumeFood = consumeFood;
            this.consumeDrink = consumeDrink;
            this.workEquipment = workEquipment;
            this.visitRoute = visitRoute;
            if (churchVenue != null)
            {
                if (!(navigation is INpcChurchNavigation) || churchSchedule == null)
                    throw new ArgumentException("Church attendee needs church navigation and a schedule.");
                churchSchedule.Validate();
                this.churchVenue = churchVenue;
                this.churchSchedule = churchSchedule;
            }
            if (socialVenue != null)
            {
                if (!(navigation is INpcSocialNavigation) || socialSettings == null)
                    throw new ArgumentException("Social NPC needs social navigation and settings.");
                socialSettings.Validate();
                this.socialVenue = socialVenue;
                this.socialSettings = new NpcSocialSettings { initialValue = socialSettings.initialValue,
                    lossPerHour = socialSettings.lossPerHour, needThreshold = socialSettings.needThreshold,
                    recoveryPerHour = socialSettings.recoveryPerHour, satisfiedValue = socialSettings.satisfiedValue,
                    aloneRecoveryMultiplier = socialSettings.aloneRecoveryMultiplier };
                Social = socialSettings.initialValue;
            }
            if ((deliveryInventory == null) != (deliverySettings == null))
                throw new ArgumentException("Delivery inventory and settings must be supplied together.");
            this.deliveryInventory = deliveryInventory;
            if (deliverySettings != null)
            {
                deliverySettings.Validate();
                minutesPerUnit = 60d / deliverySettings.unitsPerWorkHour;
                productionRemaining = minutesPerUnit;
                deliveryQuantity = deliverySettings.deliveryQuantity;
            }
            work = new WorkSchedule { startHour = schedule.startHour, endHour = schedule.endHour,
                commuteBeforeWork = schedule.commuteBeforeWork, extendForBreaks = schedule.extendForBreaks,
                travelCountsAsWork = schedule.travelCountsAsWork, workDaysMask = schedule.workDaysMask,
                sundayOverride = schedule.sundayOverride, sundayStartHour = schedule.sundayStartHour,
                sundayEndHour = schedule.sundayEndHour };
            dailyTargetMinutes = ((work.endHour - work.startHour + 24) % 24) * 60d;
            supplies = new SupplySettings
            {
                initialSatiation = supplySettings.initialSatiation,
                initialHydration = supplySettings.initialHydration,
                satiationLossPerHour = supplySettings.satiationLossPerHour,
                productiveSatiationMultiplier = supplySettings.productiveSatiationMultiplier,
                workingSatiationLossPerHour = supplySettings.workingSatiationLossPerHour,
                hydrationLossPerHour = supplySettings.hydrationLossPerHour,
                workingHydrationLossPerHour = supplySettings.workingHydrationLossPerHour,
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
                ? Math.Min(RouteLength / (metresPerGameMinute * TravelMultiplier(0)),
                    ((work.startHour - work.endHour + 24) % 24) * 60d) : 0d;
            // External stocks change even if this NPC's state repeats: never skip their consumption.
            var midnights = consumeFood == null && consumeDrink == null && deliveryInventory == null && workEquipment == null && socialVenue == null && targetMinutes - TotalMinutes >= 2880d
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

                StepFrame frame = PlanStep(targetMinutes, metresPerGameMinute);
                ApplyStep(frame, frame.Step);
            }
            TotalMinutes = targetMinutes;
            Decide();
        }

        internal struct StepFrame
        {
            public double Step;
            public bool sleeping;
            public bool travelling;
            public double travelSpeed;
            public bool eating;
            public bool drinking;
            public bool working;
            public bool atWorkplace;
            public bool wearing;
            public bool producing;
            public double satiationRate;
            public double hydrationRate;
            public double energyIn;
            public double foodIn;
            public double waterIn;
            public double arrivalIn;
            public bool socialising;
            public double socialRate;
            public double socialIn;
            public double socialTarget;
        }

        internal StepFrame PlanStep(double targetMinutes, double metresPerGameMinute)
        {
            double nextMidnight = (Math.Floor(TotalMinutes / 1440d) + 1d) * 1440d;
            double step = Math.Min(targetMinutes - TotalMinutes,
                Math.Min(work.NextBoundary(TotalMinutes), nextMidnight) - TotalMinutes);
            // Opt-in (churchSchedule): also stop exactly at the service's start/end, so a
            // large jump never skips straight over the 10:00-11:00 window unevaluated.
            if (churchSchedule != null) step = Math.Min(step, churchSchedule.NextBoundary(TotalMinutes) - TotalMinutes);
            if (commuteMinutes > 0d)
            {
                double departure = Math.Floor(TotalMinutes / 1440d) * 1440d
                    + work.startHour * 60d - commuteMinutes;
                if (departure <= TotalMinutes) departure += 1440d;
                step = Math.Min(step, departure - TotalMinutes);
            }
            bool sleeping = State == NpcState.Sleeping;
            bool travelling = route != null && nextPoint < route.Length;
            double travelSpeed = metresPerGameMinute * TravelMultiplier(CargoQuantity);
            bool eating = State == NpcState.Eating, drinking = State == NpcState.Drinking;
            bool atWorkplace = State == NpcState.Working && (workEquipment == null || workEquipment.CanWork);
            // Opt-in (travelCountsAsWork): travel to the current work destination counts the
            // same as standing at it, for WorkedMinutes, the working hunger/thirst rate and
            // extendForBreaks. Tool wear/production below stay scoped to atWorkplace only.
            bool working = atWorkplace || (work.travelCountsAsWork && State == NpcState.GoingToWork);
            // No wear while an input-dependent workplace is idle.
            bool wearing = atWorkplace && workEquipment != null &&
                !double.IsPositiveInfinity(workEquipment.MinutesUntilBreak) &&
                (!(deliveryInventory is INpcProductionGate equipmentGate) || equipmentGate.CanProduce);
            if (wearing) step = Math.Min(step, workEquipment.MinutesUntilBreak);
            if (workEquipment is INpcToolSupply) step = Math.Min(step, 1d);
            bool producing = deliveryInventory != null && atWorkplace &&
                (!(deliveryInventory is INpcProductionGate gate) || gate.CanProduce);
            if (producing) step = Math.Min(step, productionRemaining);
            // Opt-in (visitRoute): cap the step to the remaining dwell time at the current
            // timed stop, so the next Decide() re-evaluates and can advance to the next one.
            if (atWorkplace && visitRoute != null) step = Math.Min(step, visitRoute.MinutesUntilNextStop);
            // Opt-in (WorkSchedule.sundayOverride): today's shorter Sunday shift never
            // extends to catch up missed hours and never triggers the elevated-rate meal
            // break - both stay fully active on every other day, unaffected.
            bool extendSuppressedToday = work.SuppressesExtendedWork(TotalMinutes);
            // Stop exactly when today's extended target is reached, rather than only at the
            // next boundary/need - which past the nominal end hour could be far in the future.
            if (working && work.extendForBreaks && !extendSuppressedToday)
                step = Math.Min(step, Math.Max(Epsilon, dailyTargetMinutes - (WorkedMinutes - shiftWorkBaseline)));
            // Only real production receives the hunger surcharge. Needs, travel,
            // idle shifts and missing inputs/tools retain the ordinary rate.
            // workingSatiationLossPerHour/workingHydrationLossPerHour are opt-in (default 0)
            // per-hour rates that, when set, fully replace the ordinary rate while State ==
            // Working - independent of production gating, unlike productiveSatiationMultiplier.
            // Off-work (including sleep) always keeps the ordinary base rate, unaffected.
            double satiationRate = working && supplies.workingSatiationLossPerHour > 0d && !extendSuppressedToday
                ? supplies.workingSatiationLossPerHour
                : supplies.satiationLossPerHour * (producing ? supplies.productiveSatiationMultiplier : 1d);
            double hydrationRate = working && supplies.workingHydrationLossPerHour > 0d && !extendSuppressedToday
                ? supplies.workingHydrationLossPerHour
                : supplies.hydrationLossPerHour;
            if (State == NpcState.Delivering) step = Math.Min(step, 1d);
            double energyIn = sleeping ? (wakeEnergy - Energy) / (energyRecovery / 60d)
                : !seekingRest ? (Energy - sleepEnergy) / (energyLoss / 60d) : double.PositiveInfinity;
            double foodIn = !seekingFood && satiationRate > 0d
                ? (Satiation - supplies.hungerThreshold) / (satiationRate / 60d)
                : double.PositiveInfinity;
            double waterIn = !seekingWater && hydrationRate > 0d
                ? (Hydration - supplies.thirstThreshold) / (hydrationRate / 60d)
                : double.PositiveInfinity;
            double arrivalIn = travelling ? NpcPoint.Distance(Position, route[nextPoint]) / travelSpeed
                : double.PositiveInfinity;
            step = Math.Min(step, Math.Min(Math.Min(energyIn, foodIn), Math.Min(waterIn, arrivalIn)));
            if (eating || drinking) step = Math.Min(step, serviceRemaining);
            bool hostContact = IsDoingInnkeeperWork && TavernService.HasPresentGuest;
            bool aloneAtSeat = !IsInnkeeper && seekingSocial && AtSocialPlace &&
                State == NpcState.WaitingForCompany && SocialParticipant;
            bool socialising = socialVenue != null && (State == NpcState.Socialising || hostContact || aloneAtSeat);
            double socialTarget = IsInnkeeper && !seekingSocial ? 100d
                : socialSettings != null ? socialSettings.satisfiedValue : 100d;
            double socialRate = socialSettings == null ? 0d :
                socialising ? (IsInnkeeper && Social >= socialTarget ? 0d : socialSettings.recoveryPerHour *
                    (aloneAtSeat ? socialSettings.aloneRecoveryMultiplier : 1d))
                    : -socialSettings.lossPerHour;
            double socialIn = socialising ? (socialRate > 0d ? (socialTarget - Social) / (socialRate / 60d) : double.PositiveInfinity)
                : socialSettings != null && !seekingSocial && socialSettings.lossPerHour > 0
                    ? (Social - socialSettings.needThreshold) / (socialSettings.lossPerHour / 60d)
                    : double.PositiveInfinity;
            step = Math.Min(step, socialIn);
            if (step <= 0d || TotalMinutes + step == TotalMinutes)
                throw new InvalidOperationException("NPC simulation could not advance.");

            return new StepFrame
            {
                Step = step, sleeping = sleeping, travelling = travelling,
                travelSpeed = travelSpeed, eating = eating, drinking = drinking,
                working = working, atWorkplace = atWorkplace, wearing = wearing, producing = producing,
                satiationRate = satiationRate, hydrationRate = hydrationRate, energyIn = energyIn, foodIn = foodIn,
                waterIn = waterIn, arrivalIn = arrivalIn, socialising = socialising,
                socialRate = socialRate, socialIn = socialIn, socialTarget = socialTarget
            };
        }

        internal void ApplyStep(StepFrame frame, double step)
        {
            if (frame.working) WorkedMinutes += step;
            if (frame.atWorkplace && visitRoute != null) visitRoute.Consume(step);
            if (frame.producing)
            {
                productionRemaining -= step;
                if (productionRemaining <= Epsilon)
                {
                    if (deliveryInventory.TryProduceOne()) ProducedUnits++;
                    productionRemaining = minutesPerUnit;
                }
            }
            // A cycle completed exactly at break time is valid; following work requires a spare.
            // No wear on walks, deliveries, need detours, sleep or time without a usable tool.
            if (frame.wearing) workEquipment.Wear(step);
            if (frame.sleeping) SleptMinutes += step;
            Energy = Clamp(Energy + step * (frame.sleeping ? energyRecovery : -energyLoss) / 60d);
            Satiation = Clamp(Satiation - step * frame.satiationRate / 60d);
            Hydration = Clamp(Hydration - step * frame.hydrationRate / 60d);
            if (step == frame.energyIn) Energy = frame.sleeping ? wakeEnergy : sleepEnergy;
            if (step == frame.foodIn) Satiation = supplies.hungerThreshold;
            if (step == frame.waterIn) Hydration = supplies.thirstThreshold;
            if (frame.travelling)
            {
                NpcPoint to = route[nextPoint];
                double length = NpcPoint.Distance(Position, to);
                double travelled = Math.Min(length, frame.travelSpeed * step);
                Facing = new NpcPoint(to.X - Position.X, to.Y - Position.Y, to.Z - Position.Z);
                Position = step == frame.arrivalIn ? to : NpcPoint.Lerp(Position, to, travelled / length);
                TravelledMetres += travelled;
                if (step == frame.arrivalIn) nextPoint++;
            }
            // Opt-in for the additional waiter only. Existing guests and Gunnar cannot use this path.
            if ((frame.eating || frame.drinking) && TavernService != null &&
                TavernService.SelfSuppliesAtTavern(this))
            {
                serviceRemaining -= step;
                if (serviceRemaining <= Epsilon)
                {
                    if (TavernService.TryConsumeSelf(this, frame.drinking)) ReceiveSeatService(frame.drinking);
                    else State = NpcState.Home; // Keep the need and retry after the normal service duration.
                    serviceRemaining = 0d;
                }
            }
            // Guests are still supplied only by physical handover. Waiting at a bank consumes nothing.
            if (socialSettings != null)
            {
                Social = Clamp(Social + step * frame.socialRate / 60d);
                if (step == frame.socialIn) Social = frame.socialising
                    ? frame.socialTarget : socialSettings.needThreshold;
            }
            TotalMinutes += step;
        }

        internal void PrepareGroupStep(double speed)
        {
            commuteMinutes = work.commuteBeforeWork
                ? Math.Min(RouteLength / (speed * TravelMultiplier(0)),
                    ((work.startHour - work.endHour + 24) % 24) * 60d) : 0d;
            Decide();
        }

        internal void SetSocialContact(bool contact)
        {
            if (SocialParticipant) State = contact ? NpcState.Socialising : NpcState.WaitingForCompany;
        }

        public void ReleaseSocialPlace()
        {
            if (socialVenue != null) socialVenue.Release(this);
            socialSlot = -1;
            if (State == NpcState.Socialising) State = NpcState.WaitingForCompany;
        }

        public void ReleaseChurchPlace()
        {
            if (churchVenue != null) churchVenue.Release(this);
            churchSlot = -1;
        }

        private double TravelMultiplier(int cargo)
        {
            double multiplier = deliveryInventory is INpcTravelSpeed speed
                ? speed.GetTravelSpeedMultiplier(cargo) : 1d;
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0d)
                throw new InvalidOperationException("Transport speed must be finite and positive.");
            return multiplier;
        }

        private void Decide()
        {
            if (Hydration <= supplies.thirstThreshold + Epsilon) seekingWater = true;
            if (Satiation <= supplies.hungerThreshold + Epsilon) seekingFood = true;
            if (Energy <= sleepEnergy + Epsilon) seekingRest = true;
            if (State == NpcState.Sleeping && Energy >= wakeEnergy - Epsilon) seekingRest = false;
            if (TavernService != null && TavernService.SelfSuppliesAtTavern(this) && !work.IsWorkTime(TotalMinutes))
            {
                double closing = Math.Floor(TotalMinutes / 1440d) * 1440d + work.endHour * 60d;
                if (closing > TotalMinutes) closing -= 1440d;
                if (closing > lastWaiterClosingRest)
                {
                    lastWaiterClosingRest = closing;
                    seekingRest = true; // This additional waiter returns to his own sleep point after closing.
                }
            }
            if (socialVenue != null)
            {
                if (Social <= socialSettings.needThreshold + Epsilon) seekingSocial = true;
                if (Social >= socialSettings.satisfiedValue - Epsilon) seekingSocial = false;
                // Hunger/thirst reserve a bank place for the entire journey and service wait.
                bool needsSupply = seekingFood || seekingWater;
                // A pending social need survives the shift, but cannot retain a leisure seat.
                if (((!seekingSocial || IsInnkeeper || work.IsWorkTime(TotalMinutes)) && !needsSupply) ||
                    (seekingRest && !needsSupply)) ReleaseSocialPlace();
            }
            // extendForBreaks: reset the owed-minutes baseline exactly once per shift, at the
            // transition into its nominal window, then track whether today's target is still short.
            bool isWorkTimeNow = work.IsWorkTime(TotalMinutes);
            // sundayOverride: suppressed today means extendForBreaks behaves as if off, so a
            // shortened Sunday shift never captures/uses a catch-up baseline for itself and
            // never carries a false "still owes work" debt into the following day.
            bool extendActiveNow = work.extendForBreaks && !work.SuppressesExtendedWork(TotalMinutes);
            if (extendActiveNow && isWorkTimeNow && !wasWorkWindow) shiftWorkBaseline = WorkedMinutes;
            if (visitRoute != null && isWorkTimeNow && !wasWorkWindow) visitRoute.ResetForNewShift();
            wasWorkWindow = isWorkTimeNow;
            bool stillOwesWork = extendActiveNow && !isWorkTimeNow &&
                WorkedMinutes - shiftWorkBaseline < dailyTargetMinutes - Epsilon;
            // Water carried to the tavern is unloaded even when the arrival was a need detour.
            // This is opt-in: farm/smith deliveries retain their existing behavior.
            var waterSupply = deliveryInventory as INpcWaterSupply;
            if (waterSupply != null && CargoQuantity > 0 &&
                NpcPoint.Distance(Position, navigation.GetPlace(NpcPlace.Delivery)) < Epsilon &&
                deliveryInventory.TryDeliver(CargoQuantity))
            {
                DeliveredUnits += CargoQuantity;
                CargoQuantity = 0;
            }
            if (waterSupply != null && seekingWater && waterSupply.IsDrinkStockEmpty)
            {
                // Drinking from an empty store cannot resolve thirst. Fetch its water first,
                // while allowing food/rest to interrupt and never discarding a carried load.
                if (seekingFood) SetSupplyGoal(false);
                else if (seekingRest) SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Sleeping);
                else if (CargoQuantity > 0)
                    SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                else if (work.IsWorkTime(TotalMinutes)) CollectWater();
                else SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Home);
                return;
            }
            var restock = deliveryInventory as INpcPriorityRestock;
            // Finish a retained load at the tavern, including arrival during a need detour.
            if (restock != null && CargoQuantity > 0 &&
                NpcPoint.Distance(Position, navigation.GetPlace(NpcPlace.Delivery)) < Epsilon &&
                restock.TryDeliver(CargoQuantity))
            {
                DeliveredUnits += CargoQuantity;
                CargoQuantity = 0;
            }
            // An empty store cannot resolve the carrier's own need: obtain real stock first.
            if (restock != null && work.IsWorkTime(TotalMinutes) && !seekingRest &&
                ((seekingWater && restock.IsTavernSupplyEmpty(true)) ||
                 (!seekingWater && seekingFood && restock.IsTavernSupplyEmpty(false))) &&
                TryRestock(seekingWater)) return;
            if (seekingWater) SetSupplyGoal(true);
            else if (seekingFood) SetSupplyGoal(false);
            else if (seekingRest) SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Sleeping);
            // Opt-in (churchVenue): a scheduled service outranks work, restocking, tavern
            // service, tools and cargo alike, for every attendee, for its whole duration.
            else if (churchVenue != null && churchSchedule.IsWorkTime(TotalMinutes))
            {
                int previousSlot = churchSlot;
                churchSlot = churchVenue.Reserve(this);
                if (churchSlot != previousSlot) route = null;
                ((INpcChurchNavigation)navigation).SetChurchDestination(churchVenue.GetPlace(churchSlot));
                SetGoal(NpcPlace.Church, NpcState.GoingToChurch,
                    churchSlot < 0 ? NpcState.WaitingForChurchPlace : NpcState.AtChurch);
            }
            else if (restock != null && work.IsWorkTime(TotalMinutes) && TryRestock(null)) { }
            else if (seekingSocial && !IsInnkeeper && !isWorkTimeNow && !stillOwesWork)
            {
                int previousSlot = socialSlot;
                socialSlot = socialVenue.Reserve(this);
                if (socialSlot != previousSlot) route = null;
                ((INpcSocialNavigation)navigation).SetSocialDestination(socialVenue.GetPlace(socialSlot));
                SetGoal(NpcPlace.Social, NpcState.GoingToSocial,
                    socialSlot < 0 ? NpcState.WaitingForSocialPlace : NpcState.WaitingForCompany);
            }
            else if (TavernService != null && TavernService.DirectWaiter(this)) { }
            else if (ToolCargoQuantity > 0)
            {
                SetGoal(NpcPlace.Work, NpcState.ReturningWithTool, NpcState.UnloadingTool);
                if (State == NpcState.UnloadingTool && ((INpcToolSupply)workEquipment).TryDeposit())
                {
                    ToolCargoQuantity = 0;
                    Decide();
                }
            }
            else if (CargoQuantity > 0)
            {
                SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                if (State == NpcState.Delivering && deliveryInventory.TryDeliver(CargoQuantity))
                {
                    DeliveredUnits += CargoQuantity;
                    CargoQuantity = 0; // Only clear cargo after the full deposit succeeds.
                    Decide(); // Re-evaluate the current shift, not the task from departure time.
                }
            }
            else if (isWorkTimeNow || stillOwesWork)
            {
                if (workEquipment is INpcToolSupply toolSupply && toolSupply.NeedsDelivery)
                {
                    SetGoal(NpcPlace.ToolPickup, NpcState.GoingToGetTool, NpcState.CollectingTool);
                    if (State == NpcState.CollectingTool)
                    {
                        if (toolSupply.TryCollect())
                        {
                            ToolCargoQuantity = 1;
                            SetGoal(NpcPlace.Work, NpcState.ReturningWithTool, NpcState.UnloadingTool);
                        }
                        else SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
                    }
                }
                else if (deliveryInventory != null && deliveryInventory.HasBatch(deliveryQuantity))
                {
                    if (waterSupply != null) { CollectWater(); return; }
                    SetGoal(NpcPlace.Pickup, NpcState.GoingToCollect, NpcState.Collecting);
                    if (State == NpcState.Collecting)
                    {
                        if (deliveryInventory.TryPickUp(deliveryQuantity))
                        {
                            CargoQuantity = deliveryQuantity; // Source withdrawal has already succeeded.
                            SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                        }
                        else SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
                    }
                }
                else SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
            }
            else if (IsCommuteTime()) SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.GoingToWork);
            else SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Home);
        }

        private bool TryRestock(bool? preferredDrink)
        {
            var restock = (INpcPriorityRestock)deliveryInventory;
            if (CargoQuantity > 0)
            {
                SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                if (State == NpcState.Delivering && restock.TryDeliver(CargoQuantity))
                {
                    DeliveredUnits += CargoQuantity;
                    CargoQuantity = 0;
                    Decide();
                }
                return true;
            }
            if (!restock.TrySelectSupply(preferredDrink, out NpcPoint pickup)) return false;
            var restockNavigation = navigation as INpcRestockNavigation;
            if (restockNavigation == null) throw new InvalidOperationException("Restocking needs selectable source navigation.");
            if (NpcPoint.Distance(navigation.GetPlace(NpcPlace.Pickup), pickup) > 0.001d) route = null;
            restockNavigation.SetRestockPickup(pickup);
            SetGoal(NpcPlace.Pickup, NpcState.GoingToCollect, NpcState.Collecting);
            if (State == NpcState.Collecting)
            {
                CargoQuantity = restock.TakeSelectedSupply();
                if (CargoQuantity > 0) SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                else SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
            }
            return true;
        }

        private void CollectWater()
        {
            // Start each new trip at the workplace; resume an uninterrupted outbound route.
            if (Target != NpcPlace.Pickup &&
                NpcPoint.Distance(Position, navigation.GetPlace(NpcPlace.Work)) >= Epsilon)
            {
                SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
                return;
            }
            SetGoal(NpcPlace.Pickup, NpcState.GoingToCollect, NpcState.Collecting);
            if (State == NpcState.Collecting)
            {
                if (deliveryInventory.TryPickUp(deliveryQuantity))
                {
                    CargoQuantity = deliveryQuantity;
                    SetGoal(NpcPlace.Delivery, NpcState.GoingToDeliver, NpcState.Delivering);
                }
                else SetGoal(NpcPlace.Work, NpcState.GoingToWork, NpcState.Working);
            }
        }

        private bool IsCommuteTime()
        {
            if (commuteMinutes <= 0d) return false;
            double start = Math.Floor(TotalMinutes / 1440d) * 1440d + work.startHour * 60d;
            if (start <= TotalMinutes) start += 1440d;
            return TotalMinutes >= start - commuteMinutes;
        }

        internal NpcTavernService TavernService { get; set; }
        internal NpcPoint TavernPoint => navigation.GetPlace(NpcPlace.Tavern);
        internal bool CanServeTavern => work.IsWorkTime(TotalMinutes) &&
            !(churchVenue != null && churchSchedule.IsWorkTime(TotalMinutes)) &&
            !seekingFood && !seekingWater && !seekingRest && (!seekingSocial || IsInnkeeper) &&
            CargoQuantity == 0 && ToolCargoQuantity == 0 &&
            !(deliveryInventory is INpcPriorityRestock restock && restock.HasAvailableSupply);
        internal bool WantsSeatService(bool drink) => AtSocialPlace &&
            State == NpcState.WaitingForService && (drink ? seekingWater : seekingFood);
        internal void ReceiveSeatService(bool drink)
        {
            if (drink) { Hydration = 100d; seekingWater = false; DrinksCompleted++; }
            else { Satiation = 100d; seekingFood = false; MealsCompleted++; }
            State = NpcState.Home; // Reevaluate needs/social/work at this same event boundary.
        }
        internal void GoToService(NpcPoint point, bool carrying)
        {
            var serviceNavigation = navigation as INpcServiceNavigation;
            if (serviceNavigation == null) throw new InvalidOperationException("Waiter needs service navigation.");
            if (NpcPoint.Distance(navigation.GetPlace(NpcPlace.Service), point) > 0.001d) route = null;
            serviceNavigation.SetServiceDestination(point);
            SetGoal(NpcPlace.Service, carrying ? NpcState.ServingGuest : NpcState.GoingToServicePickup,
                carrying ? NpcState.ServingGuest : NpcState.GoingToServicePickup);
        }
        internal NpcPoint ReservedSupplySeat => socialVenue.GetPlace(socialSlot);
        internal bool HasSupplyReservation(bool drink) => socialSlot >= 0 &&
            (drink ? seekingWater : seekingFood);

        private void SetSupplyGoal(bool drink)
        {
            if (TavernService != null && TavernService.SelfSuppliesAtTavern(this))
            {
                SetGoal(NpcPlace.Tavern, drink ? NpcState.GoingToDrink : NpcState.GoingToEat,
                    drink ? NpcState.Drinking : NpcState.Eating);
                return;
            }
            // Only the innkeeper may leave his reserved place to collect his own portion.
            if (TavernService != null && TavernService.IsOwnOrder(this) &&
                TavernService.DirectWaiter(this)) return;
            if (socialVenue == null)
            {
                // Fail closed for an unconfigured NPC: never fall back to tavern self-feeding.
                route = null;
                Target = NpcPlace.Social;
                State = NpcState.WaitingForSocialPlace;
                return;
            }
            int previousSlot = socialSlot;
            socialSlot = socialVenue.Reserve(this);
            if (socialSlot != previousSlot) route = null;
            ((INpcSocialNavigation)navigation).SetSocialDestination(socialVenue.GetPlace(socialSlot));
            SetGoal(NpcPlace.Social, drink ? NpcState.GoingToDrink : NpcState.GoingToEat,
                socialSlot < 0 ? NpcState.WaitingForSocialPlace : NpcState.WaitingForService);
        }

        private bool AtSocialPlace => socialVenue != null && socialSlot >= 0 &&
            NpcPoint.Distance(Position, socialVenue.GetPlace(socialSlot)) < Epsilon;

        private void SetGoal(NpcPlace destination, NpcState travellingState, NpcState arrivalState)
        {
            if (destination != NpcPlace.Social && socialSlot >= 0 &&
                !(destination == NpcPlace.Service && TavernService != null && TavernService.IsOwnOrder(this)))
                ReleaseSocialPlace();
            if (destination != NpcPlace.Church && churchSlot >= 0) ReleaseChurchPlace();
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





