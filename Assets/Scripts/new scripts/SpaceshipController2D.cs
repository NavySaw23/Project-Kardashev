using UnityEngine;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class SpaceshipController2D : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode thrustKey = KeyCode.Space;
    public KeyCode orbitDecreaseKey = KeyCode.LeftControl;
    public KeyCode orbitBreakKey = KeyCode.C;
    public KeyCode manualLockKey = KeyCode.X;

    [Header("Ship Properties")]
    public float shipMass = 10f;
    public float thrustForce = 15f;
    public float maxSpeed = 25f;

    [Header("Gravity Physics")]
    public float gravitationalConstant = 50f;
    public float minimumGravityDistance = 2f;

    [Header("Orbit System")]
    public float orbitStabilityTolerance = 3f;
    public float orbitRadiusChangeSpeed = 5f;
    public float minimumOrbitRadius = 1.5f;
    public float orbitTransitionSpeed = 10f;
    public KeyCode boostKey = KeyCode.LeftShift;
    public float orbitalSpeedBoost = 1.3f;
    public float orbitTransitionBoost = 3f;
    public float momentumCarryover = 1.2f;

    [Header("Fuel System")]
    public float maxFuel = 100f;
    public float thrustFuelConsumption = 15f;
    public float boostFuelConsumption = 25f;
    private float currentFuel;

    [Header("UI Elements")]
    public TMP_Text speedText;
    public TMP_Text modeText;
    public TMP_Text fuelText;
    public TMP_Text orbitRadiusText;
    public Transform speedometerNeedle;

    [Header("Speedometer Settings")]
    [Tooltip("Maximum speed shown on speedometer (in ly/s)")]
    public float maxSpeedometerSpeed = 30f;
    [Tooltip("How smoothly the needle moves (higher = faster response)")]
    public float speedometerDamping = 5f;

    [Header("Orbit Visualization")]
    public LineRenderer orbitLineRenderer;
    public Material orbitLineMaterial;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool extensiveLogging = false;
    public ParticleSystem thrusterEffect;

    private Rigidbody2D rb2d;
    private List<SimpleGravitationalBody2D> gravityBodies = new List<SimpleGravitationalBody2D>();

    private bool isThrusting = false;

    // Orbit tracking
    private SimpleGravitationalBody2D orbitTrackingBody = null;
    private SimpleGravitationalBody2D lockedOrbitBody = null;
    private float currentOrbitRadius = 0f;
    private float targetOrbitRadius = 0f;
    private float revolutionProgress = 0f;
    private Vector2 lastRelativePosition = Vector2.zero;
    private bool isOrbitLocked = false;

    // Enhanced momentum tracking
    private float currentMomentumMultiplier = 1f;
    private bool isBoosting = false;
    private Vector2 preOrbitVelocity = Vector2.zero;
    private float orbitDirection = 1f;

    // Speedometer variables
    private float currentSpeedometerRotation = 98f;
    private float targetSpeedometerRotation = 98f;
    private const float SPEEDOMETER_MIN_ROTATION = -31f;
    private const float SPEEDOMETER_MAX_ROTATION = 98f;

    // Debug tracking
    private float lastDebugTime = 0f;
    private Vector2 lastDebugPosition = Vector2.zero;
    private float debugInterval = 2f;

    void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>();
        rb2d.mass = shipMass;
        rb2d.gravityScale = 0f;
        rb2d.drag = 0f;

        currentFuel = maxFuel;
        SetupOrbitLineRenderer();
    }

    void Start()
    {
        UpdateNearbyBodies();
        lastDebugTime = Time.time;
        lastDebugPosition = transform.position;

        if (extensiveLogging)
            Debug.Log($"[SHIP START] Found {gravityBodies.Count} gravitational bodies at position {transform.position}");
    }

    void Update()
    {
        HandleInputWithFuel();
        UpdateUI();
        UpdateThrusterEffects();
        LogDebugInfo();
    }

    void LateUpdate()
    {
        UpdateOrbitVisualization();
    }

    void FixedUpdate()
    {
        if (isOrbitLocked)
        {
            HandleLockedOrbitWithLogging();
        }
        else
        {
            ApplyRealisticGravity();
            TrackOrbitProgress();

            if (isThrusting && HasFuel())
            {
                ApplyThrust();
            }
        }

        RotateShipTowardsVelocity();
        CapSpeed();
    }

    void SetupOrbitLineRenderer()
    {
        if (orbitLineRenderer == null) return;

        orbitLineRenderer.material = orbitLineMaterial;
        orbitLineRenderer.startColor = new Color(1f, 0.93f, 0.69f, 0.8f);
        orbitLineRenderer.startWidth = 0.1f;
        orbitLineRenderer.endWidth = 0.1f;
        orbitLineRenderer.useWorldSpace = true;
        orbitLineRenderer.positionCount = 0;
    }

    void UpdateUI()
    {
        float speed = rb2d.velocity.magnitude * 10;
        if (speedText != null)
        {
            speedText.text = $"{speed:F1}";
        }

        if (modeText != null)
        {
            string mode = isOrbitLocked ? "ORBIT LOCKED" : "FREE FLIGHT";
            if (isBoosting) mode += " (BOOSTING)";
            modeText.text = $"Mode: {mode}";
        }

        if (fuelText != null)
        {
            fuelText.text = $"Fuel: {currentFuel:F1}%";
        }

        if (orbitRadiusText != null && isOrbitLocked)
        {
            orbitRadiusText.text = $"Orbit: {currentOrbitRadius:F1}";
        }
        else if (orbitRadiusText != null)
        {
            orbitRadiusText.text = "Orbit: --";
        }

        UpdateSpeedometer();
    }

    void UpdateSpeedometer()
    {
        if (speedometerNeedle == null) return;

        float currentSpeed = rb2d.velocity.magnitude;
        float clampedSpeed = Mathf.Clamp(currentSpeed, 0f, maxSpeedometerSpeed);

        float speedPercentage = clampedSpeed / maxSpeedometerSpeed;
        targetSpeedometerRotation = Mathf.Lerp(SPEEDOMETER_MAX_ROTATION, SPEEDOMETER_MIN_ROTATION, speedPercentage);

        currentSpeedometerRotation = Mathf.Lerp(currentSpeedometerRotation, targetSpeedometerRotation, speedometerDamping * Time.deltaTime);

        currentSpeedometerRotation = Mathf.Clamp(currentSpeedometerRotation, SPEEDOMETER_MIN_ROTATION, SPEEDOMETER_MAX_ROTATION);

        speedometerNeedle.rotation = Quaternion.Euler(0f, 0f, currentSpeedometerRotation);
    }

    void UpdateOrbitVisualization()
    {
        if (orbitLineRenderer == null) return;

        if (isOrbitLocked && lockedOrbitBody != null)
        {
            int segments = 100;
            orbitLineRenderer.positionCount = segments + 1;

            Vector3 orbitCenter = lockedOrbitBody.transform.position;
            float radius = currentOrbitRadius;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * 2f * Mathf.PI;
                Vector3 position = orbitCenter + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f
                );
                orbitLineRenderer.SetPosition(i, position);
            }

            orbitLineRenderer.enabled = true;
        }
        else
        {
            orbitLineRenderer.enabled = false;
            orbitLineRenderer.positionCount = 0;
        }
    }

    bool HasFuel()
    {
        return currentFuel > 0f;
    }

    void ConsumeFuel(float consumptionRate)
    {
        if (currentFuel > 0f)
        {
            currentFuel -= consumptionRate * Time.deltaTime;
            currentFuel = Mathf.Max(currentFuel, 0f);
        }
    }

    void HandleInputWithFuel()
    {
        bool spaceHeld = Input.GetKey(thrustKey);
        bool ctrlHeld = Input.GetKey(orbitDecreaseKey);
        bool cPressed = Input.GetKeyDown(orbitBreakKey);
        bool xPressed = Input.GetKeyDown(manualLockKey);
        bool shiftHeld = Input.GetKey(boostKey);

        isThrusting = spaceHeld && HasFuel();
        isBoosting = shiftHeld && isOrbitLocked && HasFuel();

        if (isThrusting)
        {
            ConsumeFuel(thrustFuelConsumption);
        }

        if (isBoosting)
        {
            ConsumeFuel(boostFuelConsumption);
        }

        if (!isOrbitLocked && xPressed)
        {
            SimpleGravitationalBody2D targetBody = GetNearestInfluentialBody();
            if (targetBody != null)
            {
                float distance = Vector2.Distance(transform.position, targetBody.transform.position);
                float maxLockDistance = targetBody.GetInfluenceRadius() * 0.8f;

                if (distance <= maxLockDistance)
                {
                    if (extensiveLogging)
                        Debug.Log($"[MANUAL LOCK] Attempting to lock onto {targetBody.name} at distance {distance:F2}");
                    LockIntoOrbitManually(targetBody, distance);
                }
                else
                {
                    if (extensiveLogging)
                        Debug.Log($"[MANUAL LOCK] Too far from {targetBody.name} - Distance: {distance:F2}, Max: {maxLockDistance:F2}");
                }
            }
            else
            {
                if (extensiveLogging)
                    Debug.Log($"[MANUAL LOCK] No gravitational body in range");
            }
        }

        if (isOrbitLocked)
        {
            if (cPressed)
            {
                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Breaking orbit with momentum multiplier: {currentMomentumMultiplier:F2}");
                BreakOrbitWithMomentum();
            }
            else if (spaceHeld && HasFuel())
            {
                float oldRadius = targetOrbitRadius;
                targetOrbitRadius += orbitRadiusChangeSpeed * Time.deltaTime;

                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Increasing orbit radius from {oldRadius:F3} to {targetOrbitRadius:F3}");
            }
            else if (ctrlHeld && HasFuel())
            {
                float oldRadius = targetOrbitRadius;
                targetOrbitRadius -= orbitRadiusChangeSpeed * Time.deltaTime;
                targetOrbitRadius = Mathf.Max(targetOrbitRadius, minimumOrbitRadius);

                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Decreasing orbit radius from {oldRadius:F3} to {targetOrbitRadius:F3}");
            }

            if (isBoosting)
            {
                currentMomentumMultiplier = Mathf.Lerp(currentMomentumMultiplier, momentumCarryover, 3f * Time.deltaTime);

                if (extensiveLogging && Time.frameCount % 60 == 0)
                    Debug.Log($"[BOOST] Building momentum: {currentMomentumMultiplier:F2}");
            }
            else
            {
                currentMomentumMultiplier = Mathf.Lerp(currentMomentumMultiplier, 1f, 2f * Time.deltaTime);
            }
        }
        else
        {
            currentMomentumMultiplier = 1f;
            isBoosting = false;
        }
    }

    void HandleLockedOrbitWithLogging()
    {
        if (lockedOrbitBody == null)
        {
            BreakOrbitWithMomentum();
            return;
        }

        Vector2 center = lockedOrbitBody.transform.position;
        Vector2 position = transform.position;
        Vector2 direction = position - center;
        float actualRadius = direction.magnitude;

        float transitionSpeedMultiplier = isBoosting ? orbitTransitionBoost : 1f;
        float effectiveTransitionSpeed = orbitTransitionSpeed * transitionSpeedMultiplier;

        currentOrbitRadius = Mathf.Lerp(currentOrbitRadius, targetOrbitRadius, effectiveTransitionSpeed * Time.fixedDeltaTime);

        Vector2 tangent = new Vector2(-orbitDirection * direction.y, orbitDirection * direction.x).normalized;

        float baseOrbitalSpeed = Mathf.Sqrt(lockedOrbitBody.gravityStrength * lockedOrbitBody.mass / currentOrbitRadius);

        float targetSpeed;
        if (isBoosting)
        {
            targetSpeed = baseOrbitalSpeed * orbitalSpeedBoost;
        }
        else
        {
            targetSpeed = baseOrbitalSpeed;
        }

        float currentSpeed = rb2d.velocity.magnitude;
        float newSpeed = Mathf.Lerp(currentSpeed, targetSpeed, 5f * Time.fixedDeltaTime);

        rb2d.velocity = tangent * newSpeed;

        float radiusError = actualRadius - currentOrbitRadius;
        if (Mathf.Abs(radiusError) > 0.1f)
        {
            Vector2 radialCorrection = -direction.normalized * radiusError * 20f;
            rb2d.AddForce(radialCorrection, ForceMode2D.Force);
        }

        if (Time.time - lastDebugTime > 1f && extensiveLogging)
        {
            Debug.Log($"[ORBIT STATE] Target: {targetOrbitRadius:F3}, Current: {currentOrbitRadius:F3}, Speed: {newSpeed:F2} {(isBoosting ? "(BOOSTED)" : "(NORMAL)")}");
        }
    }

    void BreakOrbitWithMomentum()
    {
        if (lockedOrbitBody != null)
        {
            Vector2 center = lockedOrbitBody.transform.position;
            Vector2 direction = (Vector2)transform.position - center;

            Vector2 tangentDirection = new Vector2(-orbitDirection * direction.y, orbitDirection * direction.x).normalized;

            float currentOrbitalSpeed = rb2d.velocity.magnitude;
            float exitSpeed = currentOrbitalSpeed * currentMomentumMultiplier;

            rb2d.velocity = tangentDirection * exitSpeed;

            if (extensiveLogging)
                Debug.Log($"[ORBIT BREAK] Exit speed: {exitSpeed:F2} (base: {currentOrbitalSpeed:F2}, momentum: {currentMomentumMultiplier:F2}, direction: {orbitDirection})");
        }

        isOrbitLocked = false;
        lockedOrbitBody = null;
        ResetOrbitTracking();

        currentMomentumMultiplier = 1f;
        isBoosting = false;
        orbitDirection = 1f;
    }

    void TrackOrbitProgress()
    {
        SimpleGravitationalBody2D nearestBody = GetNearestInfluentialBody();

        if (nearestBody == null)
        {
            ResetOrbitTracking();
            return;
        }

        Vector2 relativePosition = (Vector2)transform.position - (Vector2)nearestBody.transform.position;
        float distance = relativePosition.magnitude;
        float orbitDetectionRadius = nearestBody.GetInfluenceRadius() * 0.75f;

        if (distance <= orbitDetectionRadius)
        {
            if (orbitTrackingBody != nearestBody)
            {
                orbitTrackingBody = nearestBody;
                currentOrbitRadius = distance;
                lastRelativePosition = relativePosition;
                revolutionProgress = 0f;

                if (extensiveLogging)
                    Debug.Log($"[ORBIT TRACK] Started tracking {nearestBody.name} at radius {distance:F3} (manual lock available)");
            }
            else
            {
                Vector2 crossProduct = new Vector2(
                    lastRelativePosition.x * relativePosition.y - lastRelativePosition.y * relativePosition.x, 0f
                );

                float angleChange = Vector2.Angle(lastRelativePosition, relativePosition);
                if (crossProduct.x < 0) angleChange = -angleChange;

                revolutionProgress += Mathf.Abs(angleChange);
                lastRelativePosition = relativePosition;
            }
        }
        else
        {
            ResetOrbitTracking();
        }
    }

    void LockIntoOrbitManually(SimpleGravitationalBody2D body, float radius)
    {
        preOrbitVelocity = rb2d.velocity;

        lockedOrbitBody = body;
        isOrbitLocked = true;
        currentOrbitRadius = radius;
        targetOrbitRadius = radius;

        Vector2 center = body.transform.position;
        Vector2 relativePosition = (Vector2)transform.position - center;
        Vector2 currentVelocity = rb2d.velocity;

        float crossProduct = relativePosition.x * currentVelocity.y - relativePosition.y * currentVelocity.x;

        if (Mathf.Abs(crossProduct) > 0.5f)
        {
            orbitDirection = Mathf.Sign(crossProduct);
        }
        else if (currentVelocity.magnitude > 2f)
        {
            Vector2 ccwTangent = new Vector2(-relativePosition.y, relativePosition.x).normalized;
            Vector2 cwTangent = new Vector2(relativePosition.y, -relativePosition.x).normalized;

            float ccwAlignment = Vector2.Dot(currentVelocity.normalized, ccwTangent);
            float cwAlignment = Vector2.Dot(currentVelocity.normalized, cwTangent);

            orbitDirection = ccwAlignment > cwAlignment ? 1f : -1f;
        }
        else
        {
            orbitDirection = 1f;
        }

        Vector2 tangent = new Vector2(-orbitDirection * relativePosition.y, orbitDirection * relativePosition.x).normalized;

        float orbitalSpeed;
        if (currentVelocity.magnitude > 1f)
        {
            float tangentialSpeed = Vector2.Dot(currentVelocity, tangent);
            orbitalSpeed = Mathf.Max(Mathf.Abs(tangentialSpeed), currentVelocity.magnitude * 0.7f);
        }
        else
        {
            orbitalSpeed = Mathf.Sqrt(body.gravityStrength * body.mass / radius);
        }

        rb2d.velocity = tangent * orbitalSpeed;

        ResetOrbitTracking();

        if (extensiveLogging)
            Debug.Log($"[MANUAL LOCK] Locked into {(orbitDirection > 0 ? "COUNTERCLOCKWISE" : "CLOCKWISE")} orbit around {body.name} at radius {radius:F3}, speed {orbitalSpeed:F2}");
    }

    void LockIntoOrbitWithLogging(SimpleGravitationalBody2D body, float radius)
    {
        LockIntoOrbitManually(body, radius);
    }

    void ResetOrbitTracking()
    {
        orbitTrackingBody = null;
        revolutionProgress = 0f;
        lastRelativePosition = Vector2.zero;
    }

    SimpleGravitationalBody2D GetNearestInfluentialBody()
    {
        SimpleGravitationalBody2D nearest = null;
        float minDistance = float.MaxValue;

        foreach (SimpleGravitationalBody2D body in gravityBodies)
        {
            if (body == null) continue;

            float distance = Vector2.Distance(transform.position, body.transform.position);
            float gravityInfluenceRadius = body.GetInfluenceRadius();

            if (distance <= gravityInfluenceRadius && distance < minDistance)
            {
                minDistance = distance;
                nearest = body;
            }
        }

        return nearest;
    }

    void ApplyRealisticGravity()
    {
        Vector2 totalGravityForce = Vector2.zero;

        foreach (SimpleGravitationalBody2D body in gravityBodies)
        {
            if (body == null) continue;

            Vector2 direction = (Vector2)body.transform.position - (Vector2)transform.position;
            float distance = direction.magnitude;

            float gravityInfluenceRadius = body.GetInfluenceRadius();

            if (distance > gravityInfluenceRadius) continue;

            distance = Mathf.Max(distance, minimumGravityDistance);

            Vector2 gravityForce = body.GetGravitationalForceOn(shipMass, transform.position);

            totalGravityForce += gravityForce;
        }

        if (totalGravityForce.magnitude > 0.01f)
        {
            rb2d.AddForce(totalGravityForce, ForceMode2D.Force);
        }
    }

    void RotateShipTowardsVelocity()
    {
        if (rb2d.velocity.sqrMagnitude > 0.1f)
        {
            float targetAngle = Mathf.Atan2(rb2d.velocity.y, rb2d.velocity.x) * Mathf.Rad2Deg - 90f;
            float newAngle = Mathf.LerpAngle(rb2d.rotation, targetAngle, 8f * Time.fixedDeltaTime);
            rb2d.rotation = newAngle;
        }
    }

    void ApplyThrust()
    {
        if (!HasFuel()) return;

        Vector2 thrustDirection = transform.up;
        rb2d.AddForce(thrustDirection * thrustForce, ForceMode2D.Force);
    }

    void CapSpeed()
    {
        if (rb2d.velocity.magnitude > maxSpeed)
        {
            rb2d.velocity = rb2d.velocity.normalized * maxSpeed;
        }
    }

    void UpdateThrusterEffects()
    {
        if (thrusterEffect != null)
        {
            bool shouldShowEffect = ((isThrusting && !isOrbitLocked) || isBoosting) && HasFuel();

            if (shouldShowEffect && !thrusterEffect.isPlaying)
            {
                thrusterEffect.Play();
            }
            else if (!shouldShowEffect && thrusterEffect.isPlaying)
            {
                thrusterEffect.Stop();
            }
        }
    }

    void UpdateNearbyBodies()
    {
        gravityBodies.Clear();
        SimpleGravitationalBody2D[] allBodies = FindObjectsOfType<SimpleGravitationalBody2D>();
        gravityBodies.AddRange(allBodies);
    }

    void LogDebugInfo()
    {
        if (!extensiveLogging) return;

        if (Time.time - lastDebugTime >= debugInterval)
        {
            Vector2 currentPos = transform.position;
            Vector2 positionChange = currentPos - lastDebugPosition;
            float distanceMoved = positionChange.magnitude;

            string status = isOrbitLocked ? "ORBIT_LOCKED" : "FREE_FLIGHT";

            Debug.Log($"[DEBUG {Time.time:F1}s] Status: {status} | Pos: {currentPos} | Moved: {distanceMoved:F3} | Vel: {rb2d.velocity.magnitude:F3} | Fuel: {currentFuel:F1}%");

            if (isOrbitLocked && lockedOrbitBody != null)
            {
                float actualRadius = Vector2.Distance(transform.position, lockedOrbitBody.transform.position);
                Debug.Log($"[ORBIT DEBUG] Body: {lockedOrbitBody.name} | Target Radius: {targetOrbitRadius:F3} | Actual Radius: {actualRadius:F3}");
            }

            lastDebugTime = Time.time;
            lastDebugPosition = currentPos;
        }
    }

    void OnValidate()
    {
        if (maxSpeedometerSpeed <= 0f)
            maxSpeedometerSpeed = 30f;

        if (speedometerDamping <= 0f)
            speedometerDamping = 5f;
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 1f);

        foreach (SimpleGravitationalBody2D body in gravityBodies)
        {
            if (body == null) continue;

            float distance = Vector2.Distance(transform.position, body.transform.position);
            float gravityInfluenceRadius = body.GetInfluenceRadius();

            if (distance <= gravityInfluenceRadius)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(body.transform.position, gravityInfluenceRadius);

                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, body.transform.position);

                if (!isOrbitLocked)
                {
                    float maxLockDistance = gravityInfluenceRadius * 0.8f;
                    if (distance <= maxLockDistance)
                    {
                        Gizmos.color = Color.white;
                        Gizmos.DrawWireSphere(body.transform.position, maxLockDistance);
                    }
                }
            }
        }

        if (rb2d != null && rb2d.velocity.magnitude > 0.1f)
        {
            Gizmos.color = isBoosting ? Color.red : Color.green;
            Gizmos.DrawRay(transform.position, rb2d.velocity.normalized * 3f);
        }

        if (isOrbitLocked && lockedOrbitBody != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lockedOrbitBody.transform.position, currentOrbitRadius);

            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireSphere(lockedOrbitBody.transform.position, targetOrbitRadius);

            Gizmos.color = isBoosting ? Color.red : Color.cyan;
            Gizmos.DrawRay(transform.position, rb2d.velocity.normalized * 4f);

            if (isBoosting)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, currentMomentumMultiplier);
            }
        }

        if (orbitTrackingBody != null && !isOrbitLocked)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Gizmos.DrawWireSphere(orbitTrackingBody.transform.position, currentOrbitRadius);
        }
    }

    public void RefreshGravitationalBodies()
    {
        UpdateNearbyBodies();
    }

    public float CurrentFuel => currentFuel;
    public float FuelPercentage => (currentFuel / maxFuel) * 100f;
    public bool IsInOrbit => isOrbitLocked;
    public float CurrentSpeed => rb2d.velocity.magnitude;
    public string CurrentMode => isOrbitLocked ? (isBoosting ? "ORBIT (BOOSTING)" : "ORBIT") : "FREE FLIGHT";
}
