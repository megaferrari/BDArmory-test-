﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Targeting;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using BDArmory.Guidances;

namespace BDArmory.Control
{
    public class BDModuleOrbitalAI : BDGenericAIBase, IBDAIControl
    {
        // Code contained within this file is adapted from Hatbat, Spartwo and MiffedStarfish's Kerbal Combat Systems Mod https://github.com/Halbann/StockCombatAI/tree/dev/Source/KerbalCombatSystems.
        // Code is distributed under CC-BY-SA 4.0: https://creativecommons.org/licenses/by-sa/4.0/

        #region Declarations

        // Orbiter AI variables.
        public float commandedBurn = 10f;
        public float updateInterval;
        public float emergencyUpdateInterval = 0.5f;
        public float combatUpdateInterval = 2.5f;
        private bool allowWithdrawal = true;
        public float firingAngularVelocityLimit = 1; // degrees per second

        private BDOrbitalControl fc;
        private Coroutine shipController;
        private Coroutine maxAccelerationCR;

        public IBDWeapon currentWeapon;

        private float lastUpdate;
        private bool hasPropulsion;
        private bool hasWeapons;
        private float maxAcceleration;
        private Vector3 maxAngularAcceleration;
        private double minSafeAltitude;

        // Evading
        bool evadingGunfire = false;
        float evasiveTimer;
        float threatRating;
        Vector3 threatRelativePosition;
        Vector3 evasionNonLinearityDirection;
        string evasionString = " & Evading Gunfire";

        // User parameters changed via UI.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinEngagementRange"),//Min engagement range
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, scene = UI_Scene.All)]
        public float MinEngagementRange = 500;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ManeuverRCS"),//RCS active
            UI_Toggle(enabledText = "#LOC_BDArmory_ManeuverRCS_enabledText", disabledText = "#LOC_BDArmory_ManeuverRCS_disabledText", scene = UI_Scene.All),]//Maneuvers--Combat
        public bool ManeuverRCS = false;

        public float vesselStandoffDistance = 200f; // try to avoid getting closer than 200m

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_ManeuverSpeed",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 10f,
                maxValue = 10000f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float ManeuverSpeed = 100f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_StrafingSpeed",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 2f,
                maxValue = 1000f,
                scene = UI_Scene.All
            )]
        public float firingSpeed = 20f;

        #region Evade
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinEvasionTime", advancedTweakable = true, // Min Evasion Time
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float minEvasionTime = 0.2f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionThreshold", advancedTweakable = true, //Evasion Distance Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float evasionThreshold = 25f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionTimeThreshold", advancedTweakable = true, // Evasion Time Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float evasionTimeThreshold = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionMinRangeThreshold", advancedTweakable = true, // Evasion Min Range Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1)]
        public float evasionMinRangeThreshold = 10f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionIgnoreMyTargetTargetingMe", advancedTweakable = true,//Ignore my target targeting me
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionIgnoreMyTargetTargetingMe = false;
        #endregion


        // Debugging
        internal float nearInterceptBurnTime;
        internal float nearInterceptApproachTime;
        internal float lateralVelocity;
        internal Vector3 debugPosition;


        /// <summary>
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// </summary>

        Vector3 upDir;

        #endregion

        #region Status Mode
        public enum StatusMode { Idle, Evading, CorrectingOrbit, Withdrawing, Firing, Maneuvering, Stranded, Custom }
        public StatusMode currentStatusMode = StatusMode.Idle;
        StatusMode lastStatusMode = StatusMode.Idle;
        protected override void SetStatus(string status)
        {
            base.SetStatus(status);
            if (status.StartsWith("Idle")) currentStatusMode = StatusMode.Idle;
            else if (status.StartsWith("Correcting Orbit")) currentStatusMode = StatusMode.CorrectingOrbit;
            else if (status.StartsWith("Evading")) currentStatusMode = StatusMode.Evading;
            else if (status.StartsWith("Withdrawing")) currentStatusMode = StatusMode.Withdrawing;
            else if (status.StartsWith("Firing")) currentStatusMode = StatusMode.Firing;
            else if (status.StartsWith("Maneuvering")) currentStatusMode = StatusMode.Maneuvering;
            else if (status.StartsWith("Stranded")) currentStatusMode = StatusMode.Stranded;
            else currentStatusMode = StatusMode.Custom;
        }
        #endregion

        #region RMB info in editor

        // <color={XKCDColors.HexFormat.Lime}>Yes</color>
        public override string GetInfo()
        {
            // known bug - the game caches the RMB info, changing the variable after checking the info
            // does not update the info. :( No idea how to force an update.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>Available settings</b>:");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Engagement Range</color> - AI will try to move away from oponents if closer than this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- RCS Active</color> - Use RCS during any maneuvers, or only in combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Maneuver Speed</color> - Max speed relative to target during intercept maneuvers");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Strafing Speed</color> - Max speed relative to target during gun firing");
            if (GameSettings.ADVANCED_TWEAKABLES)
            {
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Evasion Time</color> - Minimum seconds AI will evade for");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Distance Threshold</color> - How close incoming gunfire needs to come to trigger evasion");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Time Threshold</color> - How many seconds the AI needs to be under fire to begin evading");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Min Range Threshold</color> - Attacker needs to be beyond this range to trigger evasion");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Don't Evade My Target</color> - Whether gunfire from the current target is ignored for evasion");
            }
            return sb.ToString();
        }

        #endregion RMB info in editor

        #region events

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
        }

        public override void ActivatePilot()
        {
            base.ActivatePilot();

            if (!fc)
            {
                fc = gameObject.AddComponent<BDOrbitalControl>();
                fc.vessel = vessel;

                fc.alignmentToleranceforBurn = 7.5f;
                fc.throttleLerpRate = 3;
            }
            fc.Activate();
            updateInterval = combatUpdateInterval;

            if (maxAccelerationCR == null) maxAccelerationCR = StartCoroutine(CalculateMaxAcceleration());
            if (shipController == null) shipController = StartCoroutine(ShipController());
        }

        public override void DeactivatePilot()
        {
            base.DeactivatePilot();

            if (fc)
            {
                fc.Deactivate();
                fc = null;
            }

            if (maxAccelerationCR != null)
            {
                StopCoroutine(maxAccelerationCR);
                maxAccelerationCR = null;
            }
            if (shipController != null)
            {
                StopCoroutine(shipController);
                shipController = null;
            }

            SetStatus("");
        }

        private IEnumerator ShipController()
        {
            while (true)
            {
                lastUpdate = Time.time;
                UpdateStatus();
                yield return PilotLogic();
                if (!vessel) yield break; // Abort if the vessel died.
            }
        }

        protected override void OnGUI()
        {
            base.OnGUI();

            if (!pilotEnabled || !vessel.isActiveVessel) return;

            if (!BDArmorySettings.DEBUG_LINES) return;
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, debugPosition, 5, Color.red); // Target intercept position
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, fc.attitude * 100, 5, Color.green); // Attitude command
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, fc.RCSVector * 100, 5, Color.cyan); // RCS command

            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vesselTransform.up * 1000, 3, Color.white);
        }

        #endregion events

        #region Actual AI Pilot
        protected override void AutoPilot(FlightCtrlState s)
        {

            upDir = VectorUtils.GetUpDirection(vesselTransform.position);

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine($"Current Status: {currentStatus}");
                debugString.AppendLine($"Has Propulsion: {hasPropulsion}");
                debugString.AppendLine($"Has Weapons: {hasWeapons}");
                if (targetVessel)
                {
                    Vector3 relVel = RelVel(vessel, targetVessel);
                    float minRange = Mathf.Max(MinEngagementRange, targetVessel.GetRadius() + vesselStandoffDistance);
                    debugString.AppendLine($"Target Vessel: {targetVessel.GetDisplayName()}");
                    debugString.AppendLine($"Can Intercept: {CanInterceptShip(targetVessel)}");
                    debugString.AppendLine($"Near Intercept: {NearIntercept(relVel, minRange)}");
                    debugString.AppendLine($"Near Intercept Burn Time: {nearInterceptBurnTime:G3}");
                    debugString.AppendLine($"Near Intercept Approach Time: {nearInterceptApproachTime:G3}");
                    debugString.AppendLine($"Lateral Velocity: {lateralVelocity:G3}");
                }
                debugString.AppendLine($"Evasive {evasiveTimer}s");
                debugString.AppendLine($"Threat Sqr Distance: {weaponManager.incomingThreatDistanceSqr}");
            }
            if (BDArmorySettings.DEBUG_AI)
            {
                if (lastStatusMode != currentStatusMode)
                {
                    Debug.Log("[BDArmory.BDModuleOrbitalAI]: Status of " + vessel.vesselName + " changed from " + lastStatusMode + " to " + currentStatus);
                }
                lastStatusMode = currentStatusMode;
            }
        }

        void UpdateStatus()
        {
            // Update propulsion and weapon status
            bool hasRCSFore = VesselModuleRegistry.GetModules<ModuleRCS>(vessel).Any(e => e.rcsEnabled && !e.flameout && e.useThrottle);
            hasPropulsion = hasRCSFore || VesselModuleRegistry.GetModuleEngines(vessel).Any(e => (e.EngineIgnited && e.isOperational));
            hasWeapons = weaponManager.HasWeaponsAndAmmo();

            // Update intervals
            if (weaponManager.incomingMissileVessel != null && updateInterval != emergencyUpdateInterval)
                updateInterval = emergencyUpdateInterval;
            else if (weaponManager.incomingMissileVessel == null && updateInterval == emergencyUpdateInterval)
                updateInterval = combatUpdateInterval;

            // Set evasion status
            EvasionStatus();

            // Set target as UI target
            if (vessel.isActiveVessel && targetVessel && !targetVessel.IsMissile() && (vessel.targetObject == null || vessel.targetObject.GetVessel() != targetVessel))
            {
                FlightGlobals.fetch.SetVesselTarget(targetVessel, true);
            }

            return;
        }

        void EvasionStatus()
        {
            // Check if we should be evading gunfire, missile evasion is handled separately
            threatRating = evasionThreshold + 1f; // Don't evade by default
            evadingGunfire = false;
            if (weaponManager != null && weaponManager.underFire)
            {
                if (weaponManager.incomingMissTime >= evasionTimeThreshold && weaponManager.incomingThreatDistanceSqr >= evasionMinRangeThreshold * evasionMinRangeThreshold) // If we haven't been under fire long enough or they're too close, ignore gunfire
                    threatRating = weaponManager.incomingMissDistance;
            }
            // If we're currently evading or a threat is significant
            if ((evasiveTimer < minEvasionTime && evasiveTimer != 0) || threatRating < evasionThreshold)
            {
                if (evasiveTimer < minEvasionTime)
                {
                    threatRelativePosition = vessel.Velocity().normalized + vesselTransform.right;
                    if (weaponManager)
                    {
                        if (weaponManager.underFire)
                            threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransform.position;
                    }
                }
                evadingGunfire = true;
                evasiveTimer += Time.fixedDeltaTime;

                if (evasiveTimer >= minEvasionTime)
                    evasiveTimer = 0;
            }
        }

        private IEnumerator PilotLogic()
        {
            maxAcceleration = GetMaxAcceleration(vessel);
            fc.RCSVector = Vector3.zero;
            debugPosition = Vector3.zero;
            evasionNonLinearityDirection = UnityEngine.Random.insideUnitSphere;
            fc.alignmentToleranceforBurn = 5;
            fc.throttle = 0;
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, ManeuverRCS);
            var wait = new WaitForFixedUpdate(); 

            // Movement.
            if (weaponManager.missileIsIncoming && weaponManager.incomingMissileVessel && weaponManager.incomingMissileTime <= weaponManager.evadeThreshold) // Needs to start evading an incoming missile.
            {

                SetStatus("Evading Missile");
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                float previousTolerance = fc.alignmentToleranceforBurn;
                fc.alignmentToleranceforBurn = 45;
                fc.throttle = 1;

                Vessel incoming = weaponManager.incomingMissileVessel;
                Vector3 incomingVector = FromTo(vessel, incoming);
                Vector3 dodgeVector;

                bool complete = false;

                while (UnderTimeLimit() && incoming != null && !complete)
                {
                    incomingVector = FromTo(vessel, incoming);
                    dodgeVector = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, incomingVector.normalized);
                    fc.attitude = dodgeVector;
                    fc.RCSVector = dodgeVector * 2;

                    yield return wait;
                    if (!vessel) yield break; // Abort if the vessel died.
                    complete = Vector3.Dot(RelVel(vessel, incoming), incomingVector) < 0;
                }

                fc.throttle = 0;
                fc.alignmentToleranceforBurn = previousTolerance;
            }
            else if (CheckOrbitUnsafe())
            {
                Orbit o = vessel.orbit;
                double UT;

                if (o.ApA < 0 && o.timeToPe < -60)
                {
                    // Vessel is on an escape orbit and has passed the periapsis by over 60s, burn retrograde

                    SetStatus("Correcting Orbit (On escape trajectory)" + (evadingGunfire ? evasionString : ""));

                    fc.throttle = 1;
                    while (UnderTimeLimit())
                    {
                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.

                        UT = Planetarium.GetUniversalTime();

                        fc.attitude = -o.Prograde(UT);
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                    }
                }
                else if (o.ApA < minSafeAltitude)
                {
                    // Entirety of orbit is inside atmosphere, perform gravity turn burn until apoapsis is outside atmosphere by a 10% margin.

                    SetStatus("Correcting Orbit (Apoapsis too low)" + (evadingGunfire ? evasionString : ""));
                    fc.throttle = 1;
                    float previousTolerance = fc.alignmentToleranceforBurn;
                    double gravTurnAlt = 0.1;
                    float turn;

                    while (UnderTimeLimit() && o.ApA < minSafeAltitude * 1.1)
                    {
                        UT = Planetarium.GetUniversalTime();
                        if (o.altitude < gravTurnAlt * minSafeAltitude) // At low alts, burn straight up
                            turn = 1f;
                        else // At higher alts, gravity turn towards horizontal orbit vector
                        {
                            turn = Mathf.Clamp((float)((1.1 * minSafeAltitude - o.ApA) / (minSafeAltitude * (1.1 - gravTurnAlt))), 0.1f, 1f);
                            turn = Mathf.Clamp(Mathf.Log10(turn) + 1f, 0.33f, 1f);
                        }
                        fc.attitude = Vector3.Lerp(o.Horizontal(UT), upDir, turn);
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        fc.alignmentToleranceforBurn = Mathf.Clamp(15f * turn, 5f, 15f);
                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.
                    }
                    fc.alignmentToleranceforBurn = previousTolerance;
                }
                else if (o.altitude < minSafeAltitude)
                {
                    // Our apoapsis is outside the atmosphere but we are inside the atmosphere and descending.
                    // Burn up until we are ascending and our apoapsis is outside the atmosphere by a 10% margin.

                    SetStatus("Correcting Orbit (Falling inside atmo)" + (evadingGunfire ? evasionString : ""));
                    fc.throttle = 1;

                    while (UnderTimeLimit() && (o.ApA < minSafeAltitude * 1.1 || o.timeToPe < o.timeToAp))
                    {
                        UT = Planetarium.GetUniversalTime();
                        fc.attitude = o.Radial(UT);
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.
                    }
                }
                else
                {
                    // We are outside the atmosphere but our periapsis is inside the atmosphere.
                    // Execute a burn to circularize our orbit at the current altitude.

                    SetStatus("Correcting Orbit (Circularizing)" + (evadingGunfire ? evasionString : ""));

                    Vector3d fvel, deltaV = Vector3d.up * 100;
                    fc.throttle = 1;

                    while (UnderTimeLimit() && deltaV.sqrMagnitude > 4)
                    {
                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaV = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaV.normalized;
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.sqrMagnitude / 100));
                    }
                }
            }
            else if (hasPropulsion && (currentCommand == PilotCommands.FlyTo || currentCommand == PilotCommands.Follow))
            {
                // We have been given a command from the WingCommander to fly/follow in a general direction
                // Burn for commandedBurn length, coast for 2x commandedBurn length
                SetStatus("Maneuvering " + (currentCommand == PilotCommands.FlyTo ? "(Commanded Position)" : "(Following)"));
                float timer = 0;

                while (UnderTimeLimit(commandedBurn * 3f) && (currentCommand == PilotCommands.FlyTo || currentCommand == PilotCommands.Follow))
                {
                    if (currentCommand == PilotCommands.FlyTo)
                        fc.attitude = (assignedPositionWorld - vessel.transform.position).normalized;
                    else // Following
                        fc.attitude = commandLeader.transform.up;
                    fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                    fc.throttle = Mathf.Lerp(1, 0, Mathf.Clamp01(timer / commandedBurn));
                    timer += Time.fixedDeltaTime;
                    yield return wait;
                    if (!vessel) yield break; // Abort if the vessel died.
                }
            }
            else if (allowWithdrawal && hasPropulsion && !hasWeapons && CheckWithdraw())
            {
                SetStatus("Withdrawing" + (evadingGunfire ? evasionString : ""));


                // Determine the direction.
                Vector3 averagePos = Vector3.zero;
                using (List<TargetInfo>.Enumerator target = BDATargetManager.TargetList(weaponManager.Team).GetEnumerator())
                    while (target.MoveNext())
                    {
                        if (target.Current == null) continue;
                        if (target.Current && target.Current.Vessel && weaponManager.CanSeeTarget(target.Current))
                        {
                            averagePos += FromTo(vessel, target.Current.Vessel).normalized;
                        }
                    }

                Vector3 direction = -averagePos.normalized;
                Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                bool facingNorth = Vector3.Dot(direction, orbitNormal) > 0;

                // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                Vector3 deltav = orbitNormal * (facingNorth ? 1 : -1) * 200;
                fc.throttle = 1;

                while (deltav.sqrMagnitude > 100)
                {
                    if (!hasPropulsion) break;

                    deltav -= Vector3.Project(vessel.acceleration, deltav) * TimeWarp.fixedDeltaTime;
                    fc.attitude = deltav.normalized;
                    fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                    yield return wait;
                    if (!vessel) yield break; // Abort if the vessel died.
                }

                fc.throttle = 0;
            }
            else if (targetVessel != null && weaponManager.currentGun && GunReady(weaponManager.currentGun))
            {
                // Aim at target using current non-missile weapon.

                SetStatus("Firing Guns" + (evadingGunfire ? evasionString : ""));
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                fc.throttle = 0;
                fc.lerpAttitude = false;
                Vector3 firingSolution = FromTo(vessel, targetVessel).normalized;

                while (UnderTimeLimit() && targetVessel != null && weaponManager.currentGun && GunReady(weaponManager.currentGun))
                {
                    if (weaponManager.currentGun.FiringSolutionVector != null)
                        firingSolution = (Vector3)weaponManager.currentGun.FiringSolutionVector;

                    fc.attitude = firingSolution;
                    fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : -Vector3.ProjectOnPlane(RelVel(vessel, targetVessel), FromTo(vessel, targetVessel));

                    yield return wait;
                    if (!vessel) yield break; // Abort if the vessel died.
                }

                fc.lerpAttitude = true;
            }
            else if (targetVessel != null && weaponManager.CurrentMissile && !weaponManager.GetLaunchAuthorization(targetVessel, weaponManager, weaponManager.CurrentMissile))
            {
                // Aim at appropriate point to launch missiles that aren't able to launch now
                
                SetStatus("Firing Missiles" + (evadingGunfire ? evasionString : ""));
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                fc.throttle = 0;
                fc.lerpAttitude = false;
                Vector3 firingSolution = FromTo(vessel, targetVessel).normalized;

                while (UnderTimeLimit() && targetVessel != null && weaponManager.CurrentMissile && !weaponManager.GetLaunchAuthorization(targetVessel, weaponManager, weaponManager.CurrentMissile))
                {
                    firingSolution = MissileGuidance.GetAirToAirFireSolution(weaponManager.CurrentMissile, targetVessel);

                    fc.attitude = firingSolution;
                    fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : -Vector3.ProjectOnPlane(RelVel(vessel, targetVessel), FromTo(vessel, targetVessel));

                    yield return wait;
                    if (!vessel) yield break; // Abort if the vessel died.
                }

                fc.lerpAttitude = true;
            }
            else if (targetVessel != null && hasWeapons)
            {

                // todo: implement for longer range movement.
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebModuleRendezvousAutopilot.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/OrbitalManeuverCalculator.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebLib/Maths/Gooding.cs

                float minRange = Mathf.Max(MinEngagementRange, targetVessel.GetRadius() + vesselStandoffDistance);
                float maxRange = Mathf.Max(weaponManager.gunRange, minRange * 1.2f);

                float minRangeProjectile = minRange;
                bool complete = false;
                bool usingProjectile = true;

                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                if (weaponManager != null && weaponManager.selectedWeapon != null)
                {
                    currentWeapon = weaponManager.selectedWeapon;
                    minRange = Mathf.Max((currentWeapon as EngageableWeapon).engageRangeMin, minRange);
                    maxRange = Mathf.Min((currentWeapon as EngageableWeapon).engageRangeMax, maxRange);
                    usingProjectile = weaponManager.selectedWeapon.GetWeaponClass() != WeaponClasses.Missile;
                }

                float currentRange = VesselDistance(vessel, targetVessel);
                bool nearInt = false;
                Vector3 relVel = RelVel(vessel, targetVessel);

                if (currentRange < (!usingProjectile ? minRange : minRangeProjectile) && AwayCheck(minRange))
                {
                    SetStatus("Maneuvering (Away)" + (evadingGunfire ? evasionString : ""));
                    fc.throttle = 1;
                    fc.alignmentToleranceforBurn = 135;

                    while (UnderTimeLimit() && targetVessel != null && !complete)
                    {
                        fc.attitude = FromTo(targetVessel, vessel).normalized;
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        fc.throttle = Vector3.Dot(RelVel(vessel, targetVessel), fc.attitude) < ManeuverSpeed ? 1 : 0;
                        complete = FromTo(vessel, targetVessel).sqrMagnitude > minRange * minRange || !AwayCheck(minRange);

                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.
                    }
                }
                // Reduce near intercept time by accounting for target acceleration
                // It should be such that "near intercept" is so close that you would go past them after you stop burning despite their acceleration
                // Also a chase timeout after which both parties should just use their weapons regardless of range.
                else if (hasPropulsion
                    && currentRange > maxRange
                    && !(nearInt = NearIntercept(relVel, minRange))
                    && CanInterceptShip(targetVessel))
                {
                    SetStatus("Maneuvering (Intercept Target)" + (evadingGunfire ? evasionString : ""));
                    complete = FromTo(vessel, targetVessel).sqrMagnitude < maxRange * maxRange || NearIntercept(relVel, minRange);
                    while (UnderTimeLimit() && targetVessel != null && !complete)
                    {
                        Vector3 toTarget = FromTo(vessel, targetVessel);
                        relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();

                        toTarget = ToClosestApproach(toTarget, -relVel, minRange);
                        debugPosition = toTarget;

                        // Burn the difference between the target and current velocities.
                        Vector3 desiredVel = toTarget.normalized * ManeuverSpeed;
                        Vector3 burn = desiredVel + relVel;

                        // Bias towards eliminating lateral velocity early on.
                        Vector3 lateral = Vector3.ProjectOnPlane(burn, toTarget.normalized);
                        burn = Vector3.Slerp(burn.normalized, lateral.normalized,
                            Mathf.Clamp01(lateral.magnitude / (maxAcceleration * 10))) * burn.magnitude;

                        lateralVelocity = lateral.magnitude;

                        float throttle = Vector3.Dot(RelVel(vessel, targetVessel), toTarget.normalized) < ManeuverSpeed ? 1 : 0;
                        if (burn.magnitude / maxAcceleration < 1 && fc.throttle == 0)
                            throttle = 0;

                        fc.throttle = throttle * Mathf.Clamp(burn.magnitude / maxAcceleration, 0.2f, 1);

                        if (fc.throttle > 0)
                            fc.attitude = burn.normalized;
                        else
                            fc.attitude = toTarget.normalized;
                        
                        fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        complete = FromTo(vessel, targetVessel).sqrMagnitude < maxRange * maxRange || NearIntercept(relVel, minRange);

                        yield return wait;
                        if (!vessel) yield break; // Abort if the vessel died.
                    }
                }
                else
                {
                    if (hasPropulsion && (relVel.sqrMagnitude > firingSpeed * firingSpeed || nearInt))
                    {
                        SetStatus("Maneuvering (Kill Velocity)" + (evadingGunfire ? evasionString : ""));
                        while (UnderTimeLimit() && targetVessel != null && !complete)
                        {
                            relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
                            fc.attitude = (relVel + targetVessel.acceleration).normalized;
                            fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                            complete = relVel.sqrMagnitude < firingSpeed * firingSpeed / 9;
                            fc.throttle = !complete ? 1 : 0;

                            yield return wait;
                            if (!vessel) yield break; // Abort if the vessel died.
                        }
                    }
                    else if (hasPropulsion && targetVessel != null && AngularVelocity(vessel, targetVessel) > firingAngularVelocityLimit)
                    {
                        SetStatus("Maneuvering (Kill Angular Velocity)" + (evadingGunfire ? evasionString : ""));

                        while (UnderTimeLimit() && targetVessel != null && !complete)
                        {
                            complete = AngularVelocity(vessel, targetVessel) < firingAngularVelocityLimit / 2;
                            fc.attitude = -Vector3.ProjectOnPlane(RelVel(vessel, targetVessel), FromTo(vessel, targetVessel)).normalized;
                            fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                            fc.throttle = !complete ? 1 : 0;

                            yield return wait;
                            if (!vessel) yield break; // Abort if the vessel died.
                        }
                    }
                    else
                    {
                        if (hasPropulsion)
                        {
                            if (currentRange < minRange)
                            {
                                SetStatus("Maneuvering (Drift Away)" + (evadingGunfire ? evasionString : ""));

                                Vector3 toTarget;
                                fc.throttle = 0;

                                while (UnderTimeLimit() && targetVessel != null && !complete)
                                {
                                    toTarget = FromTo(vessel, targetVessel);
                                    complete = toTarget.sqrMagnitude > minRange * minRange;
                                    fc.attitude = toTarget.normalized;
                                    fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                                    yield return wait;
                                    if (!vessel) yield break; // Abort if the vessel died.
                                }
                            }
                            else
                            {
                                SetStatus("Maneuvering (Drift)" + (evadingGunfire ? evasionString : ""));
                                fc.throttle = 0;
                                fc.attitude = FromTo(vessel, targetVessel).normalized;
                                fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                            }
                        }
                        else
                        {
                            SetStatus("Stranded" + (evadingGunfire ? evasionString : ""));
                            fc.throttle = 0;
                            fc.attitude = FromTo(vessel, targetVessel).normalized;
                            fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                        }

                        yield return new WaitForSecondsFixed(updateInterval);
                    }
                }
            }
            else
            {
                // Idle

                if (hasWeapons)
                    SetStatus("Idle" + (evadingGunfire ? evasionString : ""));
                else
                    SetStatus("Idle (Unarmed)" + (evadingGunfire ? evasionString : ""));

                fc.throttle = 0;
                fc.attitude = Vector3.zero;
                fc.RCSVector = evadingGunfire ? Vector3.ProjectOnPlane(evasionNonLinearityDirection, threatRelativePosition) : Vector3.zero;
                yield return new WaitForSecondsFixed(updateInterval);
            }
        }

        #endregion Actual AI Pilot

        #region Utility Functions

        private bool CheckWithdraw()
        {
            var nearest = BDATargetManager.GetClosestTarget(weaponManager);
            if (nearest == null) return false;

            return RelVel(vessel, nearest.Vessel).sqrMagnitude < 200 * 200;
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            double maxTerrainHeight = 200;
            if (body.pqsController)
            {
                PQS pqs = body.pqsController;
                maxTerrainHeight = pqs.radiusMax - pqs.radius;
            }
            minSafeAltitude = Math.Max(maxTerrainHeight, body.atmosphereDepth);

            return (o.PeA < minSafeAltitude && o.timeToPe < o.timeToAp) || (o.ApA < minSafeAltitude && (o.ApA >= 0 || o.timeToPe < -60)); // Match conditions in PilotLogic
        }

        private bool UnderTimeLimit(float timeLimit = 0)
        {
            if (timeLimit == 0)
                timeLimit = updateInterval;

            return Time.time - lastUpdate < timeLimit;
        }

        private bool NearIntercept(Vector3 relVel, float minRange)
        {
            float timeToKillVelocity = relVel.magnitude / Mathf.Max(maxAcceleration, 0.01f);

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, -relVel.normalized) * Mathf.Deg2Rad;
            float timeToRotate = BDAMath.SolveTime(rotDistance * 0.75f, maxAngularAcceleration.magnitude) / 0.75f;

            Vector3 toTarget = FromTo(vessel, targetVessel);
            Vector3 toClosestApproach = ToClosestApproach(toTarget, relVel, minRange);

            // Return false if we aren't headed towards the target.
            float velToClosestApproach = Vector3.Dot(relVel, toTarget.normalized);
            if (velToClosestApproach < 10)
                return false;

            float timeToClosestApproach = AIUtils.TimeToCPA(toClosestApproach, -relVel, Vector3.zero, 9999);
            if (timeToClosestApproach == 0)
                return false;

            nearInterceptBurnTime = timeToKillVelocity + timeToRotate;
            nearInterceptApproachTime = timeToClosestApproach;

            return timeToClosestApproach < (timeToKillVelocity + timeToRotate);
        }

        private bool CanInterceptShip(Vessel target)
        {
            bool canIntercept = false;

            // Is it worth us chasing a withdrawing ship?
            BDModuleOrbitalAI targetAI = VesselModuleRegistry.GetModule<BDModuleOrbitalAI>(target);

            if (targetAI)
            {
                Vector3 toTarget = target.CoM - vessel.CoM;
                bool escaping = targetAI.currentStatusMode == StatusMode.Withdrawing;

                canIntercept = !escaping || // It is not trying to escape.
                    toTarget.sqrMagnitude < weaponManager.gunRange * weaponManager.gunRange || // It is already in range.
                    maxAcceleration * maxAcceleration > targetAI.vessel.acceleration_immediate.sqrMagnitude || //  We are faster (currently).
                    Vector3.Dot(target.GetObtVelocity() - vessel.GetObtVelocity(), toTarget) < 0; // It is getting closer.
            }
            return canIntercept;
        }

        private bool GunReady(ModuleWeapon gun)
        {
            if (gun == null) return false;

            // Check gun/laser can fire soon, we are within guard and weapon engagement ranges, and we are under the firing speed
            float targetSqrDist = FromTo(vessel, targetVessel).sqrMagnitude;
            return RelVel(vessel, targetVessel).sqrMagnitude < firingSpeed * firingSpeed &&
                gun.CanFireSoon() &&
                (targetSqrDist <= gun.GetEngagementRangeMax() * gun.GetEngagementRangeMax()) &&
                (targetSqrDist <= weaponManager.gunRange * weaponManager.gunRange);
        }

        private bool AwayCheck(float minRange)
        {
            // Check if we need to manually burn away from an enemy that's too close or
            // if it would be better to drift away.

            Vector3 toTarget = FromTo(vessel, targetVessel);
            Vector3 toEscape = -toTarget.normalized;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();

            float rotDistance = Vector3.Angle(vessel.ReferenceTransform.up, toEscape) * Mathf.Deg2Rad;
            float timeToRotate = BDAMath.SolveTime(rotDistance / 2, maxAngularAcceleration.magnitude) * 2;
            float timeToDisplace = BDAMath.SolveTime(minRange - toTarget.magnitude, maxAcceleration, Vector3.Dot(-relVel, toEscape));
            float timeToEscape = timeToRotate * 2 + timeToDisplace;

            Vector3 drift = AIUtils.PredictPosition(toTarget, relVel, Vector3.zero, timeToEscape);
            bool manualEscape = drift.sqrMagnitude < minRange * minRange;

            return manualEscape;
        }

        private Vector3 ToClosestApproach(Vector3 toTarget, Vector3 relVel, float minRange)
        {
            Vector3 relVelInverse = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
            float timeToIntercept = AIUtils.TimeToCPA(toTarget, relVelInverse, Vector3.zero, 9999);

            // Minimising the target closest approach to the current closest approach prevents
            // ships that are targeting each other from fighting over the closest approach based on their min ranges.
            // todo: allow for trajectory fighting if fuel is high.
            Vector3 actualClosestApproach = toTarget + Displacement(relVelInverse, Vector3.zero, timeToIntercept);
            float actualClosestApproachDistance = actualClosestApproach.magnitude;

            // Get a position that is laterally offset from the target by our desired closest approach distance.
            Vector3 rotatedVector = Vector3.ProjectOnPlane(relVel, toTarget.normalized).normalized;

            // Lead if the target is accelerating away from us.
            if (Vector3.Dot(targetVessel.acceleration.normalized, toTarget.normalized) > 0)
                toTarget += Displacement(Vector3.zero, toTarget.normalized * Vector3.Dot(targetVessel.acceleration, toTarget.normalized), Mathf.Min(timeToIntercept, 999));

            Vector3 toClosestApproach = toTarget + (rotatedVector * Mathf.Clamp(actualClosestApproachDistance, minRange, weaponManager.gunRange * 0.5f));

            // Need a maximum angle so that we don't end up going further away at close range.
            toClosestApproach = Vector3.RotateTowards(toTarget, toClosestApproach, 22.5f * Mathf.Deg2Rad, float.MaxValue);

            return toClosestApproach;
        }

        #endregion

        #region Utils
        public static Vector3 FromTo(Vessel v1, Vessel v2)
        {
            return v2.transform.position - v1.transform.position;
        }

        public static Vector3 RelVel(Vessel v1, Vessel v2)
        {
            return v1.GetObtVelocity() - v2.GetObtVelocity();
        }

        public static Vector3 AngularAcceleration(Vector3 torque, Vector3 MoI)
        {
            return new Vector3(MoI.x.Equals(0) ? float.MaxValue : torque.x / MoI.x,
                MoI.y.Equals(0) ? float.MaxValue : torque.y / MoI.y,
                MoI.z.Equals(0) ? float.MaxValue : torque.z / MoI.z);
        }

        public static float AngularVelocity(Vessel v, Vessel t)
        {
            Vector3 tv1 = FromTo(v, t);
            Vector3 tv2 = tv1 + RelVel(v, t);
            return Vector3.Angle(tv1.normalized, tv2.normalized);
        }

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static Vector3 Displacement(Vector3 velocity, Vector3 acceleration, float time)
        {
            return velocity * time + 0.5f * acceleration * time * time;
        }

        private IEnumerator CalculateMaxAcceleration()
        {
            while (vessel.MOI == Vector3.zero)
            {
                yield return new WaitForSecondsFixed(1);
                if (!vessel) yield break; // Abort if the vessel died.
            }

            Vector3 availableTorque = Vector3.zero;
            var reactionWheels = VesselModuleRegistry.GetModules<ModuleReactionWheel>(vessel);
            foreach (var wheel in reactionWheels)
            {
                wheel.GetPotentialTorque(out Vector3 pos, out pos);
                availableTorque += pos;
            }

            maxAngularAcceleration = AngularAcceleration(availableTorque, vessel.MOI);
        }

        public static float GetMaxAcceleration(Vessel v)
        {
            return GetMaxThrust(v) / v.GetTotalMass();
        }

        public static float GetMaxThrust(Vessel v)
        {
            float thrust = VesselModuleRegistry.GetModuleEngines(v).Where(e => e != null && e.EngineIgnited && e.isOperational).Sum(e => e.MaxThrustOutputVac(true));
            thrust += VesselModuleRegistry.GetModules<ModuleRCS>(v).Where(rcs => rcs != null && rcs.useThrottle).Sum(rcs => rcs.thrusterPower);
            return thrust;
        }
        #endregion

        #region Autopilot helper functions

        public override bool CanEngage()
        {
            return !vessel.LandedOrSplashed && vessel.InOrbit();
        }

        public override bool IsValidFixedWeaponTarget(Vessel target)
        {
            if (!vessel) return false;

            return true;
        }

        #endregion Autopilot helper functions

        #region WingCommander

        Vector3 GetFormationPosition()
        {
            return commandLeader.vessel.CoM + Quaternion.LookRotation(commandLeader.vessel.up, upDir) * this.GetLocalFormationPosition(commandFollowIndex);
        }

        #endregion WingCommander
    }
}