/*****************************************************************************
* SpaceshipController2D.cs - Mouse Control with Fixed Orbital Mechanics
* -------------------------------------------------------------------------
* Mouse Controls:
* - Left Mouse: Orbit lock/maintain
* - Shift: Boost (free body or orbital boost)
* - Ctrl: Break/slow down
* - Scroll Wheel: Change orbit radius when in orbit
*****************************************************************************/

using UnityEngine;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class SpaceshipController2D : MonoBehaviour
{
    /*────────────────────────── INSPECTOR ──────────────────────────*/
    [Header("Mouse Controls")]
    public KeyCode boostKey = KeyCode.LeftShift;
    public KeyCode breakKey = KeyCode.LeftControl;

    [Header("Mobile UI Support")]
    public bool isMobileMode = false;
    public bool boostToggleEnabled = false;

    [Header("Ship")]
    public float shipMass = 10f;
    public float thrustForce = 15f;
    public float maxSpeed = 25f;

    [Header("Gravity")]
    public float minimumGravityDistance = 2f;

    [Header("Orbit System")]
    public float baseOrbitalSpeed = 5f;
    public float orbitRadiusChangeSpeed = 3f;
    public float minimumOrbitRadius = 3f;
    public float maximumOrbitRadius = 50f;
    public float orbitalBoostMultiplier = 2f;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public bool logOrbitPhysics = false;

    [Header("Fuel")]
    public float maxFuel = 100f;
    public float thrustFuelConsumption = 15f;
    public float boostFuelConsumption = 25f;
    public float breakFuelConsumption = 10f;

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

    // Input states
    bool isThrusting, isBoosting, isOrbitLocked, isBreaking;
    bool orbitHeld, boostHeld, breakHeld;
    float currentFuel;

    /* orbit-state */
    SimpleGravitationalBody2D lockedBody;
    float orbitRadius = 10f;
    float orbitAngle = 0f;
    float currentOrbitalSpeed;
    float orbitDirection = 1f; // +1 = CCW, −1 = CW

    /* speedometer */
    float needleRotation = 98f;
    const float NEEDLE_MIN = -31f, NEEDLE_MAX = 98f;

    /* debug tracking */
    float debugTimer = 0f;
    const float DEBUG_INTERVAL = 0.5f;

    /*────────────────────────── UNITY ───────────────────────────*/
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.mass = shipMass;
        rb.gravityScale = 0f;
        rb.drag = 0f;
        currentFuel = maxFuel;
        currentOrbitalSpeed = baseOrbitalSpeed;

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
        if (isOrbitLocked)
        {
            HandleOrbitalMovement();
            ApplyMinorGravitationalInfluences();
        }
        else
        {
            ApplyGravity();
            if (isThrusting && HasFuel()) ApplyThrust();
        }

        if (isBreaking && HasFuel()) ApplyBreaking();

        if (!isOrbitLocked)
        {
            RotateSpriteToVelocity();
            CapSpeed();
        }
        else
        {
            RotateSpriteToOrbitalDirection();
        }

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

    /*────────────────────────── MOUSE INPUT ───────────────────────────*/
    void HandleInput()
    {
        // Read input states
        orbitHeld = Input.GetMouseButton(0) || (isMobileMode && false); // Left mouse button
        boostHeld = Input.GetKey(boostKey) || (isMobileMode && boostToggleEnabled);
        breakHeld = Input.GetKey(breakKey);

        // Handle scroll wheel for orbit radius adjustment
        if (isOrbitLocked)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                orbitRadius += scroll * orbitRadiusChangeSpeed;
                orbitRadius = Mathf.Clamp(orbitRadius, minimumOrbitRadius, maximumOrbitRadius);

                if (enableDebugLogs)
                    Debug.Log($"[ORBIT] Radius adjusted to: {orbitRadius:F2}");
            }
        }

        // Reset states
        isThrusting = false;
        isBoosting = false;
        isBreaking = false;

        // Apply breaking if break button is held
        if (breakHeld)
        {
            isBreaking = true;
        }

        // Determine ship state based on button combinations
        if (boostHeld && orbitHeld)
        {
            // Both: Boost while orbiting
            HandleBoostingInOrbit();
        }
        else if (boostHeld && !orbitHeld)
        {
            // Boost only: Free body boosting
            HandleFreeBodyBoost();
        }
        else if (!boostHeld && orbitHeld)
        {
            // Orbit only: Pure orbital flight
            HandleOrbitOnly();
        }
        else
        {
            // Neither: Free flight or break orbit
            HandleFreeFlightOrBreakOrbit();
        }
    }

    void HandleBoostingInOrbit()
    {
        if (!HasFuel()) return;

        // Try to lock into orbit if not already locked
        if (!isOrbitLocked)
        {
            SimpleGravitationalBody2D nearestBody = GetNearestBody();
            if (nearestBody && Vector2.Distance(transform.position, nearestBody.transform.position) <= nearestBody.GetInfluenceRadius() * 0.8f)
            {
                LockIntoOrbit(nearestBody);
            }
        }

        // Set states for boosting while orbiting
        isBoosting = true;

        if (enableDebugLogs)
            Debug.Log("[INPUT] Boosting while orbiting");
    }

    void HandleFreeBodyBoost()
    {
        if (!HasFuel()) return;

        // Break orbit if currently locked
        if (isOrbitLocked)
        {
            BreakOrbit();
            if (enableDebugLogs)
                Debug.Log("[INPUT] Breaking orbit to boost as free body");
        }

        // Set states for free body boosting
        isThrusting = true;

        if (enableDebugLogs)
            Debug.Log("[INPUT] Free body boosting");
    }

    void HandleOrbitOnly()
    {
        // Try to lock into orbit if not already locked
        if (!isOrbitLocked)
        {
            SimpleGravitationalBody2D nearestBody = GetNearestBody();
            if (nearestBody && Vector2.Distance(transform.position, nearestBody.transform.position) <= nearestBody.GetInfluenceRadius() * 0.8f)
            {
                LockIntoOrbit(nearestBody);
                if (enableDebugLogs)
                    Debug.Log("[INPUT] Locking into orbit");
            }
            else if (enableDebugLogs)
            {
                Debug.Log("[INPUT] Attempting to orbit but no suitable body nearby");
            }
        }

        if (enableDebugLogs && isOrbitLocked)
            Debug.Log("[INPUT] Maintaining orbit");
    }

    void HandleFreeFlightOrBreakOrbit()
    {
        // Break orbit when orbit button is released
        if (isOrbitLocked)
        {
            BreakOrbit();
            if (enableDebugLogs)
                Debug.Log("[INPUT] Breaking orbit - returning to free flight");
        }

        if (enableDebugLogs)
            Debug.Log("[INPUT] Free flight mode");
    }

    /*────────────────────── ORBITAL MECHANICS ───────────────────────*/
    void HandleOrbitalMovement()
    {
        if (!lockedBody)
        {
            Debug.LogWarning("[ORBIT] Locked body is null, breaking orbit");
            BreakOrbit();
            return;
        }

        Vector2 center = lockedBody.transform.position;

        // Calculate orbital speed (with boost if active)
        float effectiveSpeed = currentOrbitalSpeed;
        if (isBoosting && HasFuel())
        {
            effectiveSpeed *= orbitalBoostMultiplier;
            ConsumeFuel(boostFuelConsumption);
        }

        // Update orbital angle
        float angularVelocity = effectiveSpeed / orbitRadius;
        orbitAngle += angularVelocity * orbitDirection * Time.fixedDeltaTime;

        // Calculate new position on orbit
        Vector2 newPosition = center + new Vector2(
            Mathf.Cos(orbitAngle) * orbitRadius,
            Mathf.Sin(orbitAngle) * orbitRadius
        );

        // Set position directly (no physics forces)
        transform.position = newPosition;

        // Calculate orbital velocity for display purposes
        Vector2 tangent = new Vector2(-Mathf.Sin(orbitAngle), Mathf.Cos(orbitAngle)) * orbitDirection;
        Vector2 orbitalVelocity = tangent * effectiveSpeed;
        rb.velocity = orbitalVelocity;

        if (logOrbitPhysics && enableDebugLogs)
        {
            Debug.Log($"[ORBIT] Radius: {orbitRadius:F2}, Speed: {effectiveSpeed:F2}, Angle: {orbitAngle * Mathf.Rad2Deg:F1}°");
        }
    }

    void ApplyMinorGravitationalInfluences()
    {
        if (!lockedBody) return;

        Vector2 totalInfluence = Vector2.zero;
        Vector2 center = lockedBody.transform.position;

        // Check influences from other bodies (not the locked one)
        foreach (var body in gravityBodies)
        {
            if (!body || body == lockedBody) continue;

            float dist = Vector2.Distance(transform.position, body.transform.position);
            if (dist > body.GetInfluenceRadius()) continue;

            Vector2 influence = body.GetGravitationalForceOn(shipMass, transform.position);

            // Only apply a fraction of the influence to maintain stable orbit
            totalInfluence += influence * 0.1f; // Reduce influence to 10%
        }

        if (totalInfluence.sqrMagnitude > 0.01f)
        {
            // Apply minor positional adjustment
            Vector2 adjustment = totalInfluence * Time.fixedDeltaTime * Time.fixedDeltaTime / shipMass;
            transform.position += (Vector3)adjustment;

            // Recalculate orbit parameters based on new position
            Vector2 newRelativePos = (Vector2)transform.position - center;
            orbitRadius = newRelativePos.magnitude;
            orbitAngle = Mathf.Atan2(newRelativePos.y, newRelativePos.x);

            if (enableDebugLogs)
                Debug.Log($"[ORBIT] Minor gravitational adjustment: {adjustment.magnitude:F4}");
        }
    }

    /*────────────────────── LOCK / BREAK ───────────────────────*/
    void LockIntoOrbit(SimpleGravitationalBody2D body)
    {
        lockedBody = body;
        isOrbitLocked = true;

        Vector2 center = body.transform.position;
        Vector2 relativePos = (Vector2)transform.position - center;

        // Set initial orbit parameters
        orbitRadius = Mathf.Max(relativePos.magnitude, minimumOrbitRadius);
        orbitAngle = Mathf.Atan2(relativePos.y, relativePos.x);

        // Determine orbit direction based on current velocity
        Vector2 velocityDirection = rb.velocity.normalized;
        Vector2 tangent = new Vector2(-relativePos.y, relativePos.x).normalized;
        orbitDirection = Mathf.Sign(Vector2.Dot(velocityDirection, tangent));
        if (orbitDirection == 0) orbitDirection = 1f; // Default to CCW

        // Set orbital speed based on current velocity or default
        float currentSpeed = rb.velocity.magnitude;
        currentOrbitalSpeed = currentSpeed > 0.5f ? currentSpeed : baseOrbitalSpeed;

        if (enableDebugLogs)
        {
            Debug.Log($"[ORBIT] LOCKED to {body.name}");
            Debug.Log($"[ORBIT] Radius: {orbitRadius:F2}, Speed: {currentOrbitalSpeed:F2}");
            Debug.Log($"[ORBIT] Direction: {(orbitDirection > 0 ? "CCW" : "CW")}");
        }

        if (lineDrawerObject) lineDrawerObject.SetActive(true);
    }

    void BreakOrbit()
    {
        if (!isOrbitLocked) return;

        if (enableDebugLogs)
            Debug.Log($"[ORBIT] BREAKING orbit from {(lockedBody ? lockedBody.name : "null")}");

        // Calculate tangential velocity for breaking orbit
        Vector2 tangent = new Vector2(-Mathf.Sin(orbitAngle), Mathf.Cos(orbitAngle)) * orbitDirection;
        rb.velocity = tangent * currentOrbitalSpeed;

        isOrbitLocked = false;
        lockedBody = null;

        if (lineDrawerObject) lineDrawerObject.SetActive(false);
    }

    /*──────────────────────── FORCES ───────────────────────────*/
    void ApplyThrust()
    {
        rb.AddForce(transform.up * thrustForce, ForceMode2D.Force);
        ConsumeFuel(thrustFuelConsumption);
    }

    void ApplyBreaking()
    {
        if (isOrbitLocked)
        {
            // In orbit: reduce orbital speed
            currentOrbitalSpeed = Mathf.Max(currentOrbitalSpeed * 0.98f, baseOrbitalSpeed * 0.5f);
        }
        else
        {
            // Free flight: apply breaking force
            if (rb.velocity.magnitude < 0.1f) return;
            Vector2 breakingForce = -rb.velocity.normalized * thrustForce * 0.8f;
            rb.AddForce(breakingForce, ForceMode2D.Force);
        }

        ConsumeFuel(breakFuelConsumption);

        if (enableDebugLogs)
            Debug.Log("[BREAKING] Slowing down");
    }

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

    void RotateSpriteToOrbitalDirection()
    {
        if (!isOrbitLocked) return;

        // Rotate sprite to face orbital direction
        Vector2 tangent = new Vector2(-Mathf.Sin(orbitAngle), Mathf.Cos(orbitAngle)) * orbitDirection;
        float a = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f;
        rb.rotation = Mathf.LerpAngle(rb.rotation, a, 8f * Time.fixedDeltaTime);
    }

    /*────────────────────── MOBILE UI METHODS ──────────────────────────*/
    public void OnBoostToggle(bool enabled)
    {
        if (isMobileMode)
            boostToggleEnabled = enabled;
    }

    public void OnOrbitButtonDown()
    {
        if (isMobileMode)
        {
            // Mobile orbit functionality
        }
    }

    public void OnOrbitButtonUp()
    {
        if (isMobileMode)
        {
            // Handle orbit release for mobile
        }
    }

    public void OnBreakButtonDown()
    {
        if (isMobileMode)
        {
            isBreaking = true;
        }
    }

    public void OnBreakButtonUp()
    {
        if (isMobileMode)
        {
            isBreaking = false;
        }
    }

    public void OnOrbitRadiusChange(float delta)
    {
        if (isMobileMode && isOrbitLocked)
        {
            orbitRadius += delta;
            orbitRadius = Mathf.Clamp(orbitRadius, minimumOrbitRadius, maximumOrbitRadius);
        }
    }

    /*────────────────────── DEBUG LOGGING ──────────────────────*/
    void LogPeriodicDebugInfo()
    {
        string mode = isOrbitLocked ? "ORBIT" : "FREE";
        Debug.Log($"[STATUS] Mode: {mode}, Velocity: {rb.velocity.magnitude:F2}, Fuel: {currentFuel:F1}%");

        if (isOrbitLocked && lockedBody)
        {
            Debug.Log($"[ORBIT STATUS] Radius: {orbitRadius:F2}, Speed: {currentOrbitalSpeed:F2}, Angle: {orbitAngle * Mathf.Rad2Deg:F1}°");
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
            pred.ShowCircularOrbit(lockedBody.transform.position, orbitRadius);
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
            pred.PredictTrajectory(transform.position, rb.velocity, gravityBodies, shipMass,
                minimumGravityDistance, maxSpeed, isThrusting && HasFuel(), transform.up, thrustForce);
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
        if (modeText)
        {
            string mode = isOrbitLocked ? "ORBIT LOCKED" : "FREE FLIGHT";
            if (isBoosting) mode += " + BOOST";
            if (isBreaking) mode += " + BREAK";
            modeText.text = $"Mode: {mode}";
        }
        if (orbitRadiusText) orbitRadiusText.text = isOrbitLocked ? $"Orbit: {orbitRadius:F1}" : "Orbit: --";

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

        bool play = (isThrusting || isBoosting || isBreaking) && HasFuel();

        if (play && !thrusterEffect.isPlaying) thrusterEffect.Play();
        else if (!play && thrusterEffect.isPlaying) thrusterEffect.Stop();
    }

    /*──────────────────── FUEL / BODIES ───────────────────────*/
    bool HasFuel() => currentFuel > 0f;

    void ConsumeFuel(float rate) => currentFuel = Mathf.Max(0f, currentFuel - rate * Time.deltaTime);

    public void RefreshGravitationalBodies()
    {
        gravityBodies.Clear();
        gravityBodies.AddRange(FindObjectsOfType<SimpleGravitationalBody2D>());
        if (enableDebugLogs)
            Debug.Log($"[INIT] Found {gravityBodies.Count} gravitational bodies");
    }
}
