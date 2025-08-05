/*****************************************************************************
 *  SpaceshipController2D.cs
 *  -------------------------------------------------------------------------
 *  Boost re-work: the ship can now accelerate rapidly while remaining in a
 *  locked orbit.  The boost system no longer fights the tangential-hold
 *  controller; instead, the reference speed is synchronised every frame
 *  while boosting.
 *
 *  Key changes (look for // *** MODIFIED *** comments):
 *  1.  Tangential-hold logic only runs when NOT boosting.
 *  2.  While boosting, lockedTangentialSpeed is updated instantly so the
 *      hold system never counter-acts the extra speed.
 *****************************************************************************/

using UnityEngine;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class SpaceshipController2D : MonoBehaviour
{
    /*────────────────────────── INSPECTOR ──────────────────────────*/
    [Header("Controls")]
    public KeyCode thrustKey = KeyCode.Space;
    public KeyCode orbitDecreaseKey = KeyCode.LeftControl;
    public KeyCode orbitBreakKey = KeyCode.C;
    public KeyCode manualLockKey = KeyCode.X;
    public KeyCode boostKey = KeyCode.LeftShift;

    [Header("Ship")]
    public float shipMass = 10f;
    public float thrustForce = 15f;
    public float maxSpeed = 25f;

    [Header("Gravity")]
    public float minimumGravityDistance = 2f;

    [Header("Orbit System")]
    public float orbitRadiusChangeSpeed = 5f;
    public float minimumOrbitRadius = 1.5f;
    public float orbitTransitionSpeed = 10f;
    public float orbitalSpeedBoost = 1.5f;

    [Header("Orbit-Assistance")]
    public bool enableOrbitAssistance = true;
    public float radialAssistForce = 20f;
    public float maxRadialApproachSpeed = 3f;
    public float tangentialHoldTolerance = 0.2f;
    public float tangentialHoldForce = 6f;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public bool logOrbitPhysics = true;
    public bool logBoostDetails = true;
    public bool logForces = true;

    [Header("Fuel")]
    public float maxFuel = 100f;
    public float thrustFuelConsumption = 15f;
    public float boostFuelConsumption = 25f;

    [Header("UI")]
    public TMP_Text speedText;
    public TMP_Text modeText;
    public TMP_Text fuelText;
    public TMP_Text orbitRadiusText;
    public Transform speedometerNeedle;

    [Header("Speedometer")]
    public float maxSpeedometerSpeed = 30f;
    public float speedometerDamping = 5f;

    [Header("Visualisation")]
    public GameObject lineDrawerObject;

    [Header("FX")]
    public ParticleSystem thrusterEffect;

    /*────────────────────────── PRIVATE ───────────────────────────*/
    Rigidbody2D rb;
    readonly List<SimpleGravitationalBody2D> gravityBodies = new();

    bool isThrusting, isBoosting, isOrbitLocked;
    float currentFuel;

    /* orbit-state */
    SimpleGravitationalBody2D lockedBody;
    float currentOrbitRadius, targetOrbitRadius;
    float orbitDirection = 1f;          // +1 = CCW, −1 = CW
    float lockedTangentialSpeed = 0f;   // reference tangential speed

    /* speedometer */
    float needleRotation = 98f;
    const float NEEDLE_MIN = -31f, NEEDLE_MAX = 98f;

    /* debug tracking */
    float debugTimer = 0f;
    const float DEBUG_INTERVAL = 0.5f;
    Vector2 lastVelocity;

    /*────────────────────────── UNITY ───────────────────────────*/
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.mass = shipMass;
        rb.gravityScale = 0f;
        rb.drag = 0f;

        currentFuel = maxFuel;
        if (lineDrawerObject) lineDrawerObject.SetActive(false);
    }

    void Start() => RefreshGravitationalBodies();

    void Update()
    {
        HandleInput();
        UpdateUI();
        UpdateThrusterEffects();
    }

    void LateUpdate() => UpdateOrbitVisualisation();

    void FixedUpdate()
    {
        if (isOrbitLocked) HandleOrbitPhysics();
        else ApplyGravity();

        if (isThrusting && HasFuel()) ApplyThrust();
        RotateSpriteToVelocity();
        CapSpeed();

        /* Periodic debug */
        if (enableDebugLogs)
        {
            debugTimer += Time.fixedDeltaTime;
            if (debugTimer >= DEBUG_INTERVAL)
            {
                LogPeriodicDebugInfo();
                debugTimer = 0f;
            }
        }
    }

    /*────────────────────────── INPUT ───────────────────────────*/
    void HandleInput()
    {
        bool spaceHeld = Input.GetKey(thrustKey);
        bool ctrlHeld = Input.GetKey(orbitDecreaseKey);
        bool breakKey = Input.GetKeyDown(orbitBreakKey);
        bool lockKey = Input.GetKeyDown(manualLockKey);
        bool boostHeld = Input.GetKey(boostKey);

        bool wasThrusting = isThrusting;
        bool wasBoosting = isBoosting;

        isThrusting = spaceHeld && HasFuel();
        isBoosting = boostHeld && isOrbitLocked && HasFuel();

        /* Debug input changes */
        if (enableDebugLogs)
        {
            if (isThrusting && !wasThrusting) Debug.Log("[INPUT] Started thrusting");
            if (!isThrusting && wasThrusting) Debug.Log("[INPUT] Stopped thrusting");
            if (isBoosting && !wasBoosting) Debug.Log("[INPUT] Started boosting");
            if (!isBoosting && wasBoosting) Debug.Log("[INPUT] Stopped boosting");
        }

        if (isThrusting) ConsumeFuel(thrustFuelConsumption);
        if (isBoosting) ConsumeFuel(boostFuelConsumption);

        /* Try manual orbit lock */
        if (!isOrbitLocked && lockKey)
        {
            SimpleGravitationalBody2D body = GetNearestBody();
            if (body && Vector2.Distance(transform.position, body.transform.position) <= body.GetInfluenceRadius() * 0.8f)
            {
                Debug.Log($"[ORBIT] Attempting to lock to {body.name}");
                LockIntoOrbit(body);
            }
            else
            {
                Debug.Log("[ORBIT] No suitable body for orbit lock");
            }
        }

        /* While locked */
        if (isOrbitLocked)
        {
            if (breakKey)
            {
                Debug.Log("[ORBIT] Breaking orbit manually");
                BreakOrbit();
            }
            else if (spaceHeld && HasFuel())
            {
                targetOrbitRadius += orbitRadiusChangeSpeed * Time.deltaTime;
            }
            else if (ctrlHeld && HasFuel())
            {
                targetOrbitRadius = Mathf.Max(minimumOrbitRadius,
                                              targetOrbitRadius - orbitRadiusChangeSpeed * Time.deltaTime);
            }
        }
    }

    /*────────────────────── ORBIT PHYSICS ───────────────────────*/
    void HandleOrbitPhysics()
    {
        if (!lockedBody)
        {
            Debug.LogWarning("[ORBIT] Locked body is null, breaking orbit");
            BreakOrbit();
            return;
        }

        /* Geometry */
        Vector2 center = lockedBody.transform.position;
        Vector2 toShip = (Vector2)transform.position - center;
        float radius = toShip.magnitude;
        Vector2 tangent = new(-orbitDirection * toShip.y, orbitDirection * toShip.x);
        tangent.Normalize();

        /* Track forces for optional logging */
        Vector2 radialForce = Vector2.zero;
        Vector2 tangentialForce = Vector2.zero;
        Vector2 boostForce = Vector2.zero;

        /* 1. Radial assistance */
        float rError = radius - targetOrbitRadius;
        if (enableOrbitAssistance && Mathf.Abs(rError) > 0.05f)
        {
            Vector2 radialDir = toShip.normalized;
            float radialVel = Vector2.Dot(rb.velocity, radialDir);

            float assistMult = isBoosting ? 2.5f : 1f;
            if (Mathf.Abs(radialVel) > maxRadialApproachSpeed)
            {
                Vector2 damping = -radialDir * radialVel * 2f * assistMult;
                rb.AddForce(damping, ForceMode2D.Force);
                radialForce += damping;
            }

            Vector2 correction = -radialDir * rError * radialAssistForce * assistMult;
            rb.AddForce(correction, ForceMode2D.Force);
            radialForce += correction;

            if (logOrbitPhysics && enableDebugLogs)
                Debug.Log($"[ORBIT] Radius: {radius:F2}, Target: {targetOrbitRadius:F2}, Error: {rError:F2}, RadialVel: {radialVel:F2}, Multiplier: {assistMult:F1}");
        }

        /* 2. Tangential hold  (runs ONLY when not boosting) */
        float tangentialNow = Vector2.Dot(rb.velocity, tangent);

        if (!isBoosting)                           // *** MODIFIED ***
        {
            float speedError = lockedTangentialSpeed - tangentialNow;
            if (Mathf.Abs(speedError) > tangentialHoldTolerance)
            {
                Vector2 holdForce = tangent * speedError * tangentialHoldForce;
                rb.AddForce(holdForce, ForceMode2D.Force);
                tangentialForce += holdForce;
            }

            if (logOrbitPhysics && enableDebugLogs)
                Debug.Log($"[ORBIT] TangentialNow: {tangentialNow:F2}, Locked: {lockedTangentialSpeed:F2}, Error: {(lockedTangentialSpeed - tangentialNow):F2}");
        }
        else                                        // *** MODIFIED ***
        {
            // While boosting, always keep the reference synced
            lockedTangentialSpeed = tangentialNow;
        }

        /* 3. Boost adds momentum */
        if (isBoosting && HasFuel())
        {
            Vector2 currentBoostForce = tangent * thrustForce * orbitalSpeedBoost;
            rb.AddForce(currentBoostForce, ForceMode2D.Force);
            boostForce = currentBoostForce;

            // *** MODIFIED ***  Instantly synchronise reference speed
            float newTangentialSpeed = Vector2.Dot(rb.velocity + (currentBoostForce / shipMass) * Time.fixedDeltaTime, tangent);
            lockedTangentialSpeed = newTangentialSpeed;

            if (logBoostDetails && enableDebugLogs)
            {
                Debug.Log($"[BOOST] Force Applied: {currentBoostForce.magnitude:F2}, Dir: {currentBoostForce.normalized}");
                Debug.Log($"[BOOST] LockedTangential updated to: {lockedTangentialSpeed:F2}");
                Debug.Log($"[BOOST] Total Velocity: {rb.velocity.magnitude:F2}");
            }
        }

        /* Smooth UI radius */
        currentOrbitRadius = Mathf.Lerp(currentOrbitRadius, targetOrbitRadius,
                                        orbitTransitionSpeed * Time.fixedDeltaTime);

        /* Optional force logging */
        if (logForces && enableDebugLogs &&
           (radialForce.sqrMagnitude > 0.01f || tangentialForce.sqrMagnitude > 0.01f || boostForce.sqrMagnitude > 0.01f))
        {
            Debug.Log($"[FORCES] Radial: {radialForce.magnitude:F2}, Tangential: {tangentialForce.magnitude:F2}, Boost: {boostForce.magnitude:F2}");
        }

        lastVelocity = rb.velocity;
    }

    /*────────────────────── LOCK / BREAK ───────────────────────*/
    void LockIntoOrbit(SimpleGravitationalBody2D body)
    {
        lockedBody = body;
        isOrbitLocked = true;

        Vector2 center = body.transform.position;
        Vector2 rel = (Vector2)transform.position - center;

        orbitDirection = Mathf.Sign(Vector2.Dot(new Vector2(-rel.y, rel.x), rb.velocity));
        currentOrbitRadius = rel.magnitude;
        targetOrbitRadius = currentOrbitRadius;

        Vector2 tangent = new(-rel.y, rel.x); tangent.Normalize();
        lockedTangentialSpeed = Vector2.Dot(rb.velocity, tangent);

        // Project velocity onto tangent
        rb.velocity = tangent * lockedTangentialSpeed;

        if (enableDebugLogs)
        {
            Debug.Log($"[ORBIT] LOCKED to {body.name}");
            Debug.Log($"[ORBIT] Radius: {currentOrbitRadius:F2}, Dir: {(orbitDirection > 0 ? "CCW" : "CW")}");
            Debug.Log($"[ORBIT] Tangential Speed: {lockedTangentialSpeed:F2}");
        }

        if (lineDrawerObject) lineDrawerObject.SetActive(true);
    }

    void BreakOrbit()
    {
        if (!isOrbitLocked) return;

        if (enableDebugLogs)
            Debug.Log($"[ORBIT] BREAKING orbit from {(lockedBody ? lockedBody.name : "null")}");

        isOrbitLocked = false;
        lockedBody = null;
        if (lineDrawerObject) lineDrawerObject.SetActive(false);
    }

    /*──────────────────────── FORCES ───────────────────────────*/
    void ApplyThrust() => rb.AddForce(transform.up * thrustForce, ForceMode2D.Force);

    void ApplyGravity()
    {
        Vector2 total = Vector2.zero;
        foreach (var b in gravityBodies)
        {
            if (!b) continue;
            float dist = Vector2.Distance(transform.position, b.transform.position);
            if (dist > b.GetInfluenceRadius()) continue;
            total += b.GetGravitationalForceOn(shipMass, transform.position);
        }
        rb.AddForce(total, ForceMode2D.Force);
    }

    /*──────────────────────── HELPERS ──────────────────────────*/
    SimpleGravitationalBody2D GetNearestBody()
    {
        SimpleGravitationalBody2D nearest = null;
        float min = float.MaxValue;
        foreach (var b in gravityBodies)
        {
            if (!b) continue;
            float d = Vector2.Distance(transform.position, b.transform.position);
            if (d <= b.GetInfluenceRadius() && d < min) { min = d; nearest = b; }
        }
        return nearest;
    }

    void CapSpeed()
    {
        if (rb.velocity.magnitude > maxSpeed)
        {
            rb.velocity = rb.velocity.normalized * maxSpeed;
            if (enableDebugLogs)
                Debug.Log($"[SPEED] Capped velocity to {maxSpeed:F2}");
        }
    }

    void RotateSpriteToVelocity()
    {
        if (rb.velocity.sqrMagnitude < 0.1f) return;
        float a = Mathf.Atan2(rb.velocity.y, rb.velocity.x) * Mathf.Rad2Deg - 90f;
        rb.rotation = Mathf.LerpAngle(rb.rotation, a, 8f * Time.fixedDeltaTime);
    }

    /*────────────────────── DEBUG LOGGING ──────────────────────*/
    void LogPeriodicDebugInfo()
    {
        Debug.Log($"[STATUS] Mode: {(isOrbitLocked ? "ORBIT" : "FREE")}, Velocity: {rb.velocity.magnitude:F2}, Fuel: {currentFuel:F1}%");

        if (isOrbitLocked && lockedBody)
        {
            Vector2 center = lockedBody.transform.position;
            Vector2 toShip = (Vector2)transform.position - center;

            float actualRadius = toShip.magnitude;

            Vector2 tangent = new(-orbitDirection * toShip.y, orbitDirection * toShip.x);
            tangent.Normalize();
            float actualTangential = Vector2.Dot(rb.velocity, tangent);

            Debug.Log($"[ORBIT STATUS] ActualRadius: {actualRadius:F2}, TargetRadius: {targetOrbitRadius:F2}");
            Debug.Log($"[ORBIT STATUS] ActualTangential: {actualTangential:F2}, LockedTangential: {lockedTangentialSpeed:F2}");
            Debug.Log($"[ORBIT STATUS] RadiusError: {(actualRadius - targetOrbitRadius):F2}, SpeedError: {(lockedTangentialSpeed - actualTangential):F2}");
        }
    }

    /*────────────────────── VISUALS ───────────────────────────*/
    void UpdateOrbitVisualisation()
    {
        if (!lineDrawerObject) return;
        var pred = lineDrawerObject.GetComponent<TrajectoryPredictor>();
        if (!pred) return;

        if (isOrbitLocked && lockedBody)
        {
            if (!lineDrawerObject.activeSelf) lineDrawerObject.SetActive(true);
            pred.ShowCircularOrbit(lockedBody.transform.position, currentOrbitRadius);
            return;
        }

        /* Free-flight prediction */
        if (rb.velocity.magnitude < 0.1f)
        {
            pred.ClearTrajectory();
            if (lineDrawerObject.activeSelf) lineDrawerObject.SetActive(false);
            return;
        }

        SimpleGravitationalBody2D near = GetNearestBody();
        if (near && Vector2.Distance(transform.position, near.transform.position) <= near.GetInfluenceRadius())
        {
            if (!lineDrawerObject.activeSelf) lineDrawerObject.SetActive(true);
            pred.PredictTrajectory(transform.position, rb.velocity,
                                   gravityBodies, shipMass,
                                   minimumGravityDistance, maxSpeed,
                                   isThrusting && HasFuel(), transform.up, thrustForce);
        }
        else
        {
            pred.ClearTrajectory();
            if (lineDrawerObject.activeSelf) lineDrawerObject.SetActive(false);
        }
    }

    /*────────────────────── UI / FX ───────────────────────────*/
    void UpdateUI()
    {
        if (speedText) speedText.text = (rb.velocity.magnitude * 10f).ToString("F1");
        if (fuelText) fuelText.text = $"Fuel: {currentFuel:F1}%";
        if (modeText) modeText.text = isOrbitLocked ? "Mode: ORBIT LOCKED" : "Mode: FREE FLIGHT";
        if (orbitRadiusText) orbitRadiusText.text = isOrbitLocked ? $"Orbit: {currentOrbitRadius:F1}" : "Orbit: --";

        if (speedometerNeedle)
        {
            float pct = Mathf.Clamp(rb.velocity.magnitude, 0, maxSpeedometerSpeed) / maxSpeedometerSpeed;
            float target = Mathf.Lerp(NEEDLE_MAX, NEEDLE_MIN, pct);
            needleRotation = Mathf.Lerp(needleRotation, target, speedometerDamping * Time.deltaTime);
            speedometerNeedle.rotation = Quaternion.Euler(0, 0, needleRotation);
        }
    }

    void UpdateThrusterEffects()
    {
        if (!thrusterEffect) return;

        bool play = ((isThrusting && !isOrbitLocked) || isBoosting) && HasFuel();
        if (play && !thrusterEffect.isPlaying) thrusterEffect.Play();
        else if (!play && thrusterEffect.isPlaying) thrusterEffect.Stop();
    }

    /*──────────────────── FUEL / BODIES ───────────────────────*/
    bool HasFuel() => currentFuel > 0f;
    void ConsumeFuel(float r) => currentFuel = Mathf.Max(0f, currentFuel - r * Time.deltaTime);

    public void RefreshGravitationalBodies()
    {
        gravityBodies.Clear();
        gravityBodies.AddRange(FindObjectsOfType<SimpleGravitationalBody2D>());
        if (enableDebugLogs)
            Debug.Log($"[INIT] Found {gravityBodies.Count} gravitational bodies");
    }
}

