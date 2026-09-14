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
        private readonly Func<bool> consumeFood, consumeDrink;
        private readonly INpcDeliveryInventory deliveryInventory;
        private readonly double minutesPerUnit;
        private readonly int deliveryQuantity;
        private double productionRemaining;
        private readonly INpcWorkEquipment workEquipment;
        private readonly NpcSocialVenue socialVenue;
        private readonly NpcSocialSettings socialSettings;
        private bool seekingSocial;
        private int socialSlot = -1;
        public double Social { get; private set; } = 100d;
        public bool NeedsSocial => seekingSocial;
        public int SocialSlot => socialSlot;
        internal NpcSocialVenue SocialVenue => socialVenue;
        internal bool SocialParticipant => socialSlot >= 0 &&
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
            NpcSocialVenue socialVenue = null, NpcSocialSettings socialSettings = null)
        {
            if (navigation == null) throw new ArgumentNullException(nameof(navigation));
            schedule.Validate();
            settings.Validate();
            supplySettings.Validate();
            this.navigation = navigation;
            this.consumeFood = consumeFood;
            this.consumeDrink = consumeDrink;
            this.workEquipment = workEquipment;
            if (socialVenue != null)
            {
                if (!(navigation is INpcSocialNavigation) || socialSettings == null)
                    throw new ArgumentException("Social NPC needs social navigation and settings.");
                socialSettings.Validate();
                this.socialVenue = socialVenue;
                this.socialSettings = new NpcSocialSettings { initialValue = socialSettings.initialValue,
                    lossPerHour = socialSettings.lossPerHour, needThreshold = socialSettings.needThreshold,
                    recoveryPerHour = socialSettings.recoveryPerHour, satisfiedValue = socialSettings.satisfiedValue };
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
                commuteBeforeWork = schedule.commuteBeforeWork };
            supplies = new SupplySettings
            {
                initialSatiation = supplySettings.initialSatiation,
                initialHydration = supplySettings.initialHydration,
                satiationLossPerHour = supplySettings.satiationLossPerHour,
                productiveSatiationMultiplier = supplySettings.productiveSatiationMultiplier,
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
            public bool wearing;
            public bool producing;
            public double satiationRate;
            public double energyIn;
            public double foodIn;
            public double waterIn;
            public double arrivalIn;
            public bool socialising;
            public double socialRate;
            public double socialIn;
        }

        internal StepFrame PlanStep(double targetMinutes, double metresPerGameMinute)
        {
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
            double travelSpeed = metresPerGameMinute * TravelMultiplier(CargoQuantity);
            bool eating = State == NpcState.Eating, drinking = State == NpcState.Drinking;
            bool working = State == NpcState.Working && (workEquipment == null || workEquipment.CanWork);
            // No wear while an input-dependent workplace is idle.
            bool wearing = working && workEquipment != null &&
                !double.IsPositiveInfinity(workEquipment.MinutesUntilBreak) &&
                (!(deliveryInventory is INpcProductionGate equipmentGate) || equipmentGate.CanProduce);
            if (wearing) step = Math.Min(step, workEquipment.MinutesUntilBreak);
            if (workEquipment is INpcToolSupply) step = Math.Min(step, 1d);
            bool producing = deliveryInventory != null && working &&
                (!(deliveryInventory is INpcProductionGate gate) || gate.CanProduce);
            if (producing) step = Math.Min(step, productionRemaining);
            // Only real production receives the hunger surcharge. Needs, travel,
            // idle shifts and missing inputs/tools retain the ordinary rate.
            double satiationRate = supplies.satiationLossPerHour *
                (producing ? supplies.productiveSatiationMultiplier : 1d);
            if (State == NpcState.Delivering) step = Math.Min(step, 1d);
            double energyIn = sleeping ? (wakeEnergy - Energy) / (energyRecovery / 60d)
                : !seekingRest ? (Energy - sleepEnergy) / (energyLoss / 60d) : double.PositiveInfinity;
            double foodIn = !seekingFood && satiationRate > 0d
                ? (Satiation - supplies.hungerThreshold) / (satiationRate / 60d)
                : double.PositiveInfinity;
            double waterIn = !seekingWater && supplies.hydrationLossPerHour > 0d
                ? (Hydration - supplies.thirstThreshold) / (supplies.hydrationLossPerHour / 60d)
                : double.PositiveInfinity;
            double arrivalIn = travelling ? NpcPoint.Distance(Position, route[nextPoint]) / travelSpeed
                : double.PositiveInfinity;
            step = Math.Min(step, Math.Min(Math.Min(energyIn, foodIn), Math.Min(waterIn, arrivalIn)));
            if (eating || drinking) step = Math.Min(step, serviceRemaining);
            bool socialising = socialVenue != null && State == NpcState.Socialising;
            double socialRate = socialSettings == null ? 0d :
                socialising ? socialSettings.recoveryPerHour : -socialSettings.lossPerHour;
            double socialIn = socialising ? (socialSettings.satisfiedValue - Social) / (socialRate / 60d)
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
                working = working, wearing = wearing, producing = producing,
                satiationRate = satiationRate, energyIn = energyIn, foodIn = foodIn,
                waterIn = waterIn, arrivalIn = arrivalIn, socialising = socialising,
                socialRate = socialRate, socialIn = socialIn
            };
        }

        internal void ApplyStep(StepFrame frame, double step)
        {
            if (frame.working) WorkedMinutes += step;
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
            Hydration = Clamp(Hydration - step * supplies.hydrationLossPerHour / 60d);
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
            if (frame.eating || frame.drinking)
            {
                serviceRemaining -= step;
                if (serviceRemaining <= Epsilon)
                {
                    if (frame.eating && (consumeFood == null || consumeFood()))
                    { Satiation = 100d; seekingFood = false; MealsCompleted++; }
                    if (frame.drinking && (consumeDrink == null || consumeDrink()))
                    { Hydration = 100d; seekingWater = false; DrinksCompleted++; }
                    // Empty stock grants nothing; the need stays active and service can retry.
                    serviceRemaining = 0d;
                    State = NpcState.Home; // Neutral until Decide re-evaluates current priorities.
                }
            }
            if (socialSettings != null)
            {
                Social = Clamp(Social + step * frame.socialRate / 60d);
                if (step == frame.socialIn) Social = frame.socialising ? socialSettings.satisfiedValue : socialSettings.needThreshold;
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
            if (socialVenue != null)
            {
                if (Social <= socialSettings.needThreshold + Epsilon) seekingSocial = true;
                if (Social >= socialSettings.satisfiedValue - Epsilon) seekingSocial = false;
                // A meal/drink at an occupied bank place keeps that exclusive reservation.
                bool seatedSupply = AtSocialPlace && (seekingFood || seekingWater);
                if ((!seekingSocial && !seatedSupply) || seekingRest ||
                    ((seekingFood || seekingWater) && !seatedSupply)) ReleaseSocialPlace();
            }
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
            if (seekingWater) SetSupplyGoal(true);
            else if (seekingFood) SetSupplyGoal(false);
            else if (seekingRest) SetGoal(NpcPlace.Home, NpcState.GoingHome, NpcState.Sleeping);
            else if (seekingSocial)
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
            else if (work.IsWorkTime(TotalMinutes))
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
            !seekingFood && !seekingWater && !seekingRest && !seekingSocial &&
            CargoQuantity == 0 && ToolCargoQuantity == 0;
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
        private void SetSupplyGoal(bool drink)
        {
            bool seated = AtSocialPlace && (TavernService == null || !TavernService.IsWaiter(this));
            SetGoal(seated ? NpcPlace.Social : NpcPlace.Tavern,
                drink ? NpcState.GoingToDrink : NpcState.GoingToEat,
                seated && TavernService != null ? NpcState.WaitingForService :
                    drink ? NpcState.Drinking : NpcState.Eating);
        }

        private bool AtSocialPlace => socialVenue != null && socialSlot >= 0 &&
            NpcPoint.Distance(Position, socialVenue.GetPlace(socialSlot)) < Epsilon;

        private void SetGoal(NpcPlace destination, NpcState travellingState, NpcState arrivalState)
        {
            if (destination != NpcPlace.Social && socialSlot >= 0) ReleaseSocialPlace();
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


