using System;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Utils;
using TMPro;

namespace BDArmory.Targeting
{
    public struct TargetSignatureData : IEquatable<TargetSignatureData>
    {
        public Vector3 velocity;
        public Vector3 geoPos;
        public Vector3 acceleration;
        public bool exists;
        public float timeAcquired;
        public float signalStrength;
        public TargetInfo targetInfo;
        public BDTeam Team;
        public Vector2 pingPosition;
        public VesselECMJInfo vesselJammer;
        public ModuleRadar lockedByRadar;
        public Vessel vessel;
        public Part IRSource;
        bool orbital;
        Orbit orbit;

        public bool Equals(TargetSignatureData other)
        {
            return
                exists == other.exists &&
                geoPos == other.geoPos &&
                timeAcquired == other.timeAcquired;
        }

        public TargetSignatureData(Vessel v, float _signalStrength, Part heatpart = null)
        {
            orbital = v.InOrbit();
            orbit = v.orbit;

            timeAcquired = Time.time;
            vessel = v;
            velocity = v.Velocity();
            IRSource = heatpart;
            geoPos = VectorUtils.WorldPositionToGeoCoords(IRSource != null ? IRSource.transform.position : v.CoM, v.mainBody);
            acceleration = v.acceleration_immediate;
            exists = true;

            signalStrength = _signalStrength;

            targetInfo = v.gameObject.GetComponent<TargetInfo>();

            // vessel never been picked up on radar before: create new targetinfo record
            if (targetInfo == null)
            {
                targetInfo = v.gameObject.AddComponent<TargetInfo>();
            }

            Team = null;

            if (targetInfo)  // Always true, as we just set it?
            {
                Team = targetInfo.Team;
            }
            else
            {
                var mf = VesselModuleRegistry.GetMissileFire(v, true);
                if (mf != null) Team = mf.Team;
            }

            vesselJammer = v.gameObject.GetComponent<VesselECMJInfo>();

            pingPosition = Vector2.zero;
            lockedByRadar = null;
        }

        public TargetSignatureData(CMFlare flare, float _signalStrength)
        {
            velocity = flare.velocity;
            geoPos = VectorUtils.WorldPositionToGeoCoords(flare.transform.position, FlightGlobals.currentMainBody);
            exists = true;
            acceleration = Vector3.zero;
            timeAcquired = Time.time;
            signalStrength = _signalStrength;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            pingPosition = Vector2.zero;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
        }

        public TargetSignatureData(Vector3 _velocity, Vector3 _position, Vector3 _acceleration, bool _exists, float _signalStrength)
        {
            velocity = _velocity;
            geoPos = VectorUtils.WorldPositionToGeoCoords(_position, FlightGlobals.currentMainBody);
            acceleration = _acceleration;
            exists = _exists;
            timeAcquired = Time.time;
            signalStrength = _signalStrength;
            targetInfo = null;
            vesselJammer = null;
            Team = null;
            pingPosition = Vector2.zero;
            orbital = false;
            orbit = null;
            lockedByRadar = null;
            vessel = null;
            IRSource = null;
        }

        public Vector3 position
        {
            get
            {
                return VectorUtils.GetWorldSurfacePostion(geoPos, FlightGlobals.currentMainBody);
            }
            set
            {
                geoPos = VectorUtils.WorldPositionToGeoCoords(value, FlightGlobals.currentMainBody);
            }
        }

        public Vector3 predictedPosition
        {
            get
            {
                return position + (velocity * age);
            }
        }

        public Vector3 predictedPositionWithChaffFactor(float chaffEffectivity = 1f)
        {
            // get chaff factor of vessel and calculate decoy distortion caused by chaff echos
            float decoyFactor = 0f;
            Vector3 posDistortion = Vector3.zero;

            if (vessel != null)
            {
                // chaff check
                decoyFactor = (1f - RadarUtils.GetVesselChaffFactor(vessel));

                if (decoyFactor > 0f)
                {
                    // With ecm on better chaff effectiveness due to jammer strength
                    VesselECMJInfo vesseljammer = vessel.gameObject.GetComponent<VesselECMJInfo>();

                    // Jamming biases position distortion further to rear, depending on ratio of jamming strength and radarModifiedSignature
                    float jammingFactor = vesseljammer is null ? 0 : decoyFactor * Mathf.Clamp01(vesseljammer.jammerStrength / 100f / Mathf.Max(targetInfo.radarModifiedSignature, 0.1f));

                    // Random radius of distortion, 16-256m
                    float distortionFactor = decoyFactor * UnityEngine.Random.Range(16f, 256f);

                    // Convert Float jammingFactor position bias and signatureFactor scaling to Vector3 position
                    Vector3 signatureDistortion = distortionFactor * (vessel.GetSrfVelocity().normalized * -1f * jammingFactor + UnityEngine.Random.insideUnitSphere);

                    // Higher speed -> missile decoyed further "behind" where the chaff drops (also means that chaff is least effective for head-on engagements)
                    posDistortion = (vessel.GetSrfVelocity() * -1f * Mathf.Clamp(decoyFactor * decoyFactor, 0f, 0.5f)) + signatureDistortion;

                    // Apply effects from global settings and individual missile chaffEffectivity
                    posDistortion *= Mathf.Max(BDArmorySettings.CHAFF_FACTOR, 0f) * chaffEffectivity;
                }
            }

            return position + (velocity * age) + posDistortion;
        }

        public Vector3 predictedIOGChaff(float chaffEffectivity = 1f)
        {
            // get chaff factor of vessel and calculate decoy distortion caused by chaff echos
            float decoyFactor = 0f;
            Vector3 posDistortion = Vector3.zero;

            if (vessel != null)
            {
                // chaff check
                decoyFactor = (1f - RadarUtils.GetVesselChaffFactor(vessel));

                if (decoyFactor > 0f)
                {
                    // With ecm on better chaff effectiveness due to jammer strength
                    VesselECMJInfo vesseljammer = vessel.gameObject.GetComponent<VesselECMJInfo>();

                    // Jamming biases position distortion further to rear, depending on ratio of jamming strength and radarModifiedSignature
                    float jammingFactor = vesseljammer is null ? 0 : decoyFactor * Mathf.Clamp01(vesseljammer.jammerStrength / 100f / Mathf.Max(targetInfo.radarModifiedSignature, 0.1f));

                    // Random radius of distortion, 16-256m
                    float distortionFactor = decoyFactor * UnityEngine.Random.Range(16f, 256f);

                    // Convert Float jammingFactor position bias and signatureFactor scaling to Vector3 position
                    Vector3 signatureDistortion = distortionFactor * (vessel.GetSrfVelocity().normalized * -1f * jammingFactor + UnityEngine.Random.insideUnitSphere);

                    // Higher speed -> missile decoyed further "behind" where the chaff drops (also means that chaff is least effective for head-on engagements)
                    posDistortion = (vessel.GetSrfVelocity() * -1f * Mathf.Clamp(decoyFactor * decoyFactor, 0f, 0.5f)) + signatureDistortion;

                    // Apply effects from global settings and individual missile chaffEffectivity
                    posDistortion *= Mathf.Max(BDArmorySettings.CHAFF_FACTOR, 0f) * chaffEffectivity;
                }
            }

            return position + posDistortion;
        }

        public Vector3 predictedPositionIOG(Vessel missileVessel,float timesinceLastUpdate)
        {
            //Vector3 PredPosition = new();
            Vector3 state_estimate = Vector3.zero;
            Vector3 state_covariance = new Vector3(100f, 100f, 100f);
            // Time since last update
            float dt = timesinceLastUpdate;

            // State transition matrix
            Vector3 F = new Vector3(1f, 1f, 1f);
            Vector3 B = new Vector3(dt, dt, dt);
            Vector3 u = Vector3.zero;
            Vector3 x_hat = Vector3.Scale(F, state_estimate) + Vector3.Scale(B, u);

            // State covariance matrix
            float Q = 1f; // Process noise
            Vector3 P = Vector3.Scale(F, Vector3.Scale(state_covariance, F)) + new Vector3(Q, Q, Q);

            // Measurement matrix
            Vector3 H = Vector3.one;

            // Measurement covariance matrix
            float R = 10f; // Measurement noise
            float S = Vector3.Dot(Vector3.Scale(H, Vector3.Scale(P, H)), Vector3.one) + R;

            // Kalman gain
            Vector3 K = Vector3.Scale(P, H) / S;

            // Calculate new state estimate and covariance
            Vector3 z = position - state_estimate;
            Vector3 x_hat_new = x_hat + Vector3.Scale(K, Vector3.Scale(z - Vector3.Scale(H, x_hat), K));
            Vector3 P_new = Vector3.Scale(Vector3.one - Vector3.Scale(K, H), P);

            // Update state estimate and covariance
            state_estimate = x_hat_new;
            state_covariance = P_new;

            // Relative velocity
            Vector3 missileVel = (float)missileVessel.srfSpeed * missileVessel.Velocity().normalized;
            Vector3 relVelocity = velocity - missileVel;

            // Predicted position
            Vector3 predictedPos = state_estimate + Vector3.Scale(velocity, new Vector3(dt, dt, dt)) + 0.5f * Vector3.Scale(acceleration - relVelocity, new Vector3(dt * dt, dt * dt, dt * dt));

            return predictedPos;
        }
        
        public float altitude
        {
            get
            {
                return geoPos.z;
            }
        }

        public float age
        {
            get
            {
                return (Time.time - timeAcquired);
            }
        }

        public static TargetSignatureData noTarget
        {
            get
            {
                return new TargetSignatureData(Vector3.zero, Vector3.zero, Vector3.zero, false, (float)RadarWarningReceiver.RWRThreatTypes.None);
            }
        }

        public static void ResetTSDArray(ref TargetSignatureData[] tsdArray)
        {
            for (int i = 0; i < tsdArray.Length; i++)
            {
                tsdArray[i] = noTarget;
            }
        }
    }
}
