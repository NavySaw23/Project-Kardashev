using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class SpaceshipController2D : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode thrustKey = KeyCode.Space;
    public KeyCode orbitDecreaseKey = KeyCode.LeftControl;
    public KeyCode orbitBreakKey = KeyCode.C;

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
    public float orbitTransitionSpeed = 10f; // How fast ship moves to new orbit radius
    public KeyCode boostKey = KeyCode.LeftShift;
    public float orbitalSpeedBoost = 2f; // Orbital speed multiplier when boosting
    public float orbitTransitionBoost = 5f; // How much faster orbit transitions when boosting
    public float momentumCarryover = 1.5f; // Extra speed when ejecting from boosted orbit

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool extensiveLogging = true;
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

    // Momentum tracking
    private float currentMomentumMultiplier = 1f;
    private bool isBoosting = false;

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
        HandleInputSimplified();
        UpdateThrusterEffects();
        LogDebugInfo();
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

            if (isThrusting)
            {
                ApplyThrust();
            }
        }

        RotateShipTowardsVelocity();
        CapSpeed();
    }

    void HandleInputSimplified()
    {
        bool spaceHeld = Input.GetKey(thrustKey);
        bool ctrlHeld = Input.GetKey(orbitDecreaseKey);
        bool cPressed = Input.GetKeyDown(orbitBreakKey);
        bool shiftHeld = Input.GetKey(boostKey);

        isThrusting = spaceHeld;
        isBoosting = shiftHeld && isOrbitLocked; // Only boost when in orbit

        // Handle orbit controls when locked
        if (isOrbitLocked)
        {
            if (cPressed)
            {
                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Breaking orbit with momentum multiplier: {currentMomentumMultiplier:F2}");
                BreakOrbitWithMomentum();
            }
            else if (spaceHeld)
            {
                float oldRadius = targetOrbitRadius;
                targetOrbitRadius += orbitRadiusChangeSpeed * Time.deltaTime;

                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Increasing orbit radius from {oldRadius:F3} to {targetOrbitRadius:F3}");
            }
            else if (ctrlHeld)
            {
                float oldRadius = targetOrbitRadius;
                targetOrbitRadius -= orbitRadiusChangeSpeed * Time.deltaTime;
                targetOrbitRadius = Mathf.Max(targetOrbitRadius, minimumOrbitRadius);

                if (extensiveLogging)
                    Debug.Log($"[ORBIT] Decreasing orbit radius from {oldRadius:F3} to {targetOrbitRadius:F3}");
            }

            // Build up momentum when boosting
            if (isBoosting)
            {
                currentMomentumMultiplier = Mathf.Lerp(currentMomentumMultiplier, momentumCarryover, 2f * Time.deltaTime);

                if (extensiveLogging && Time.frameCount % 60 == 0)
                    Debug.Log($"[BOOST] Building momentum: {currentMomentumMultiplier:F2}");
            }
            else
            {
                // Gradually lose momentum when not boosting
                currentMomentumMultiplier = Mathf.Lerp(currentMomentumMultiplier, 1f, 1f * Time.deltaTime);
            }
        }
        else
        {
            // Reset momentum when not in orbit
            currentMomentumMultiplier = 1f;
            isBoosting = false;
        }
    }

    void HandleLockedOrbitWithLogging()
    {
        if (lockedOrbitBody == null)
        {
            if (extensiveLogging)
                Debug.Log($"[ORBIT] Lost orbit body, breaking orbit");
            BreakOrbitWithMomentum();
            return;
        }

        Vector2 center = lockedOrbitBody.transform.position;
        Vector2 position = transform.position;
        Vector2 direction = position - center;
        float actualRadius = direction.magnitude;

        // FASTER ORBIT TRANSITIONS: Use boost multiplier when shift is held
        float transitionSpeedMultiplier = isBoosting ? orbitTransitionBoost : 1f;
        float effectiveTransitionSpeed = orbitTransitionSpeed * transitionSpeedMultiplier;

        currentOrbitRadius = Mathf.Lerp(currentOrbitRadius, targetOrbitRadius, effectiveTransitionSpeed * Time.fixedDeltaTime);

        // FASTER ORBITAL SPEED: Calculate boosted orbital velocity
        float baseOrbitalSpeed = Mathf.Sqrt(lockedOrbitBody.gravityStrength * lockedOrbitBody.mass / currentOrbitRadius);
        float speedMultiplier = isBoosting ? orbitalSpeedBoost : 1f;
        float boostedOrbitalSpeed = baseOrbitalSpeed * speedMultiplier;

        Vector2 tangent = new Vector2(-direction.y, direction.x).normalized;
        rb2d.velocity = tangent * boostedOrbitalSpeed;

        // Enhanced radial correction with transition speed
        float radiusError = actualRadius - currentOrbitRadius;
        Vector2 radialCorrection = -direction.normalized * radiusError * effectiveTransitionSpeed * 2.5f;
        rb2d.AddForce(radialCorrection, ForceMode2D.Force);

        // Debug logging
        if (Time.time - lastDebugTime > 1f && extensiveLogging)
        {
            Debug.Log($"[ORBIT STATE] Target: {targetOrbitRadius:F3}, Current: {currentOrbitRadius:F3}, Speed: {boostedOrbitalSpeed:F2} {(isBoosting ? "(BOOSTED)" : "")}");
        }
    }

    void BreakOrbitWithMomentum()
    {
        if (lockedOrbitBody != null)
        {
            Vector2 center = lockedOrbitBody.transform.position;
            Vector2 direction = (Vector2)transform.position - center;
            Vector2 tangentDirection = new Vector2(-direction.y, direction.x).normalized;

            // Use current orbital speed and apply momentum multiplier
            float currentOrbitalSpeed = rb2d.velocity.magnitude;
            float exitSpeed = currentOrbitalSpeed * currentMomentumMultiplier;

            rb2d.velocity = tangentDirection * exitSpeed;

            if (extensiveLogging)
                Debug.Log($"[ORBIT BREAK] Exit speed: {exitSpeed:F2} (base: {currentOrbitalSpeed:F2}, momentum: {currentMomentumMultiplier:F2})");
        }

        isOrbitLocked = false;
        lockedOrbitBody = null;
        ResetOrbitTracking();

        // Reset momentum after use
        currentMomentumMultiplier = 1f;
        isBoosting = false;
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

            Debug.Log($"[DEBUG {Time.time:F1}s] Status: {status} | Pos: {currentPos} | Moved: {distanceMoved:F3} | Vel: {rb2d.velocity.magnitude:F3}");

            if (isOrbitLocked && lockedOrbitBody != null)
            {
                float actualRadius = Vector2.Distance(transform.position, lockedOrbitBody.transform.position);
                Debug.Log($"[ORBIT DEBUG] Body: {lockedOrbitBody.name} | Target Radius: {targetOrbitRadius:F3} | Actual Radius: {actualRadius:F3}");
            }

            lastDebugTime = Time.time;
            lastDebugPosition = currentPos;
        }
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

            if (extensiveLogging && Time.frameCount % 60 == 0)
                Debug.Log($"[GRAVITY] Body: {body.name}, Distance: {distance:F2}, Influence: {gravityInfluenceRadius:F2}, Force: {gravityForce.magnitude:F3}");
        }

        if (totalGravityForce.magnitude > 0.01f)
        {
            rb2d.AddForce(totalGravityForce, ForceMode2D.Force);

            if (extensiveLogging && Time.frameCount % 60 == 0)
                Debug.Log($"[GRAVITY] Total force applied: {totalGravityForce.magnitude:F3}");
        }
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
                    Debug.Log($"[ORBIT TRACK] Started tracking {nearestBody.name} at radius {distance:F3}");
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

                if (revolutionProgress >= 360f &&
                    Mathf.Abs(distance - currentOrbitRadius) <= orbitStabilityTolerance)
                {
                    LockIntoOrbitWithLogging(nearestBody, distance);
                }

                if (Mathf.Abs(distance - currentOrbitRadius) > orbitStabilityTolerance * 2f)
                {
                    if (extensiveLogging)
                        Debug.Log($"[ORBIT TRACK] Orbit too unstable, resetting tracking");
                    ResetOrbitTracking();
                }
            }
        }
        else
        {
            ResetOrbitTracking();
        }
    }

    void LockIntoOrbitWithLogging(SimpleGravitationalBody2D body, float radius)
    {
        lockedOrbitBody = body;
        isOrbitLocked = true;
        currentOrbitRadius = radius;
        targetOrbitRadius = radius;

        Vector2 center = body.transform.position;
        Vector2 toCenter = center - (Vector2)transform.position;
        Vector2 tangent = new Vector2(-toCenter.y, toCenter.x).normalized;

        float orbitalSpeed = Mathf.Sqrt(body.gravityStrength * body.mass / radius);
        rb2d.velocity = tangent * orbitalSpeed;

        ResetOrbitTracking();

        if (extensiveLogging)
            Debug.Log($"[ORBIT LOCK] Locked into orbit around {body.name} at radius {radius:F3} with speed {orbitalSpeed:F3}");
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

            if (extensiveLogging && Time.frameCount % 120 == 0)
                Debug.Log($"[NEAREST] Checking {body.name}: Distance={distance:F2}, Influence={gravityInfluenceRadius:F2}");

            if (distance <= gravityInfluenceRadius && distance < minDistance)
            {
                minDistance = distance;
                nearest = body;
            }
        }

        if (extensiveLogging && Time.frameCount % 120 == 0)
            Debug.Log($"[NEAREST] Selected: {(nearest ? nearest.name : "None")}");

        return nearest;
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
        Vector2 thrustDirection = transform.up;
        rb2d.AddForce(thrustDirection * thrustForce, ForceMode2D.Force);

        if (extensiveLogging && Time.frameCount % 30 == 0)
            Debug.Log($"[THRUST] Applying thrust in direction {thrustDirection}");
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
            // Show thruster effects when boosting in orbit or thrusting in free flight
            bool shouldShowEffect = (isThrusting && !isOrbitLocked) || isBoosting;

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
            }
        }

        if (rb2d != null && rb2d.velocity.magnitude > 0.1f)
        {
            // Show velocity with different colors based on boost state
            Gizmos.color = isBoosting ? Color.red : Color.green;
            Gizmos.DrawRay(transform.position, rb2d.velocity.normalized * 3f);
        }

        if (isOrbitLocked && lockedOrbitBody != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(lockedOrbitBody.transform.position, currentOrbitRadius);

            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireSphere(lockedOrbitBody.transform.position, targetOrbitRadius);

            // Show velocity direction with boost indication
            Gizmos.color = isBoosting ? Color.red : Color.cyan;
            Gizmos.DrawRay(transform.position, rb2d.velocity.normalized * 4f);

            // Show momentum buildup with a circle size
            if (isBoosting)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, currentMomentumMultiplier);
            }
        }

        if (orbitTrackingBody != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(orbitTrackingBody.transform.position, currentOrbitRadius);
        }
    }

    public void RefreshGravitationalBodies()
    {
        UpdateNearbyBodies();
    }
}
