using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class SimpleGravitationalBody2D : MonoBehaviour
{
    [Header("Orbital Setup")]
    public Transform orbitTarget;
    public bool isStationary = true;

    [Header("Orbit Properties")]
    public float orbitRadius = 10f;
    public float orbitSpeed = 30f;
    public bool clockwise = false;

    [Header("Gravitational Properties")]
    public float strength = 100f; // SINGLE parameter controls everything

    [Header("Debug")]
    public bool showGravityField = true;

    private Rigidbody2D rb2d;
    private float currentAngle = 0f;
    private Vector2 initialOffset;

    // Calculated properties based on strength
    private float influenceRadius;

    void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>();
        CalculateGravityProperties();

        if (isStationary)
        {
            rb2d.bodyType = RigidbodyType2D.Kinematic;
        }
        else
        {
            rb2d.bodyType = RigidbodyType2D.Dynamic;
            rb2d.gravityScale = 0f;

            if (orbitTarget != null)
            {
                initialOffset = transform.position - orbitTarget.position;
                orbitRadius = initialOffset.magnitude;
                currentAngle = Mathf.Atan2(initialOffset.y, initialOffset.x) * Mathf.Rad2Deg;
            }
        }
    }

    void CalculateGravityProperties()
    {
        // Calculate influence radius based on strength
        // At the edge, gravity force becomes negligible (< 0.01)
        float minForceThreshold = 0.01f;
        influenceRadius = Mathf.Sqrt(strength / minForceThreshold);

        if (showGravityField)
        {
            Debug.Log($"[GRAVITY] {name} - Strength: {strength}, Influence Radius: {influenceRadius:F2}");
        }
    }

    void OnValidate()
    {
        // Recalculate when strength changes in inspector
        CalculateGravityProperties();
    }

    void FixedUpdate()
    {
        if (isStationary || orbitTarget == null) return;

        // Simple orbital motion
        float angleStep = orbitSpeed * Time.fixedDeltaTime;
        currentAngle += clockwise ? -angleStep : angleStep;

        // Calculate target position
        float radians = currentAngle * Mathf.Deg2Rad;
        Vector2 targetPosition = (Vector2)orbitTarget.position + new Vector2(
            Mathf.Cos(radians) * orbitRadius,
            Mathf.Sin(radians) * orbitRadius
        );

        // Smoothly move to target position
        Vector2 direction = targetPosition - (Vector2)transform.position;
        rb2d.velocity = direction * 5f;
    }

    public Vector2 GetGravitationalForceOn(float targetMass, Vector2 targetPosition)
    {
        Vector2 direction = (Vector2)transform.position - targetPosition;
        float distance = direction.magnitude;

        if (distance < 0.5f) return Vector2.zero; // Prevent extreme forces at zero distance

        // Define close threshold where gravity becomes intense
        float closeThreshold = influenceRadius * 0.3f; // Within 30% of influence radius

        if (distance < closeThreshold)
        {
            // INTENSE GRAVITY: Use inverse cubic law for dramatic pull
            float adjustedDistance = Mathf.Max(distance, 0.5f); // Prevent division by zero
            float forceMagnitude = strength * targetMass / (adjustedDistance * adjustedDistance * adjustedDistance);

            // Scale and cap the force to prevent ship from being stuck
            forceMagnitude *= 0.5f; // Scale down the intense force
            forceMagnitude = Mathf.Clamp(forceMagnitude, 0f, 150f); // Higher cap for close range

            Debug.Log($"[INTENSE GRAVITY] {name}: Distance={distance:F2}, Force={forceMagnitude:F1} (CLOSE RANGE)");

            return direction.normalized * forceMagnitude;
        }
        else if (distance <= influenceRadius)
        {
            // NORMAL GRAVITY: Standard inverse square law with falloff
            float normalizedDistance = distance / influenceRadius; // 0 to 1
            float falloffFactor = CalculateLogarithmicFalloff(normalizedDistance);

            float forceMagnitude = strength * targetMass * falloffFactor * 0.01f;
            forceMagnitude = Mathf.Clamp(forceMagnitude, 0f, 50f);

            return direction.normalized * forceMagnitude;
        }

        return Vector2.zero; // Outside influence radius
    }

    // Helper method for smooth falloff in normal range
    float CalculateLogarithmicFalloff(float normalizedDistance)
    {
        if (normalizedDistance <= 0.01f) return 1f;

        float logFactor = -Mathf.Log(normalizedDistance * 10f + 1f) / Mathf.Log(11f);
        return Mathf.Clamp01(logFactor);
    }

    // Getter methods for spaceship compatibility
    public float mass => strength; // Mass equivalent for spaceship calculations
    public float gravityStrength => strength * 0.1f;
    public float GetInfluenceRadius() => influenceRadius;

    void OnDrawGizmos()
    {
        if (showGravityField)
        {
            CalculateGravityProperties();

            // Show main influence sphere
            Gizmos.color = new Color(0, 1, 0, 0.1f);
            Gizmos.DrawSphere(transform.position, influenceRadius);

            Gizmos.color = isStationary ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, influenceRadius);

            // NEW: Show intense gravity zone in red
            float closeThreshold = influenceRadius * 0.3f;
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f); // Red for danger zone
            Gizmos.DrawSphere(transform.position, closeThreshold);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, closeThreshold);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * (influenceRadius + 2f),
                $"Strength: {strength}\nRadius: {influenceRadius:F1}\nIntense Zone: {closeThreshold:F1}");
#endif

            // Show falloff visualization
            for (int i = 1; i <= 5; i++)
            {
                float testRadius = influenceRadius * (i / 5f);
                float alpha = i <= 1 ? 0.8f : 0.3f; // Make close ring more visible
                Gizmos.color = new Color(1f, 1f - (i / 5f), 0f, alpha);
                Gizmos.DrawWireSphere(transform.position, testRadius);
            }
        }

        if (orbitTarget != null && !isStationary)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(orbitTarget.position, orbitRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, orbitTarget.position);
        }
    }

}
