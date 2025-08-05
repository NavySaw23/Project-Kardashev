using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class TrajectoryPredictor : MonoBehaviour
{
    [Header("Prediction Settings")]
    public int maxPredictionPoints = 200;
    public float timeStep = 0.02f; // Smaller timestep for accuracy
    public float maxPredictionTime = 10f;
    public float minVelocityThreshold = 0.1f;

    [Header("Visual Settings")]
    public Gradient trajectoryGradient;
    public float baseLineWidth = 0.1f;

    private LineRenderer lineRenderer;
    private List<Vector3> predictedPoints = new List<Vector3>();

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("No LineRenderer component found!");
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = false;

        // Default gradient if none assigned
        if (trajectoryGradient.colorKeys.Length == 0)
        {
            GradientColorKey[] colorKeys = new GradientColorKey[2];
            colorKeys[0] = new GradientColorKey(Color.cyan, 0f);
            colorKeys[1] = new GradientColorKey(new Color(0f, 1f, 1f, 0.2f), 1f);

            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
            alphaKeys[0] = new GradientAlphaKey(0.8f, 0f);
            alphaKeys[1] = new GradientAlphaKey(0.2f, 1f);

            trajectoryGradient.SetKeys(colorKeys, alphaKeys);
        }
    }

    public void PredictTrajectory(Vector2 startPosition, Vector2 startVelocity,
                                List<SimpleGravitationalBody2D> gravityBodies,
                                float shipMass, float minimumGravityDistance,
                                float maxSpeed, bool isThrusting, Vector2 thrustDirection, float thrustForce)
    {
        if (lineRenderer == null) return;

        predictedPoints.Clear();

        if (startVelocity.magnitude < minVelocityThreshold)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        // Simulation state - EXACT copy of ship's physics
        Vector2 position = startPosition;
        Vector2 velocity = startVelocity;
        float currentTime = 0f;

        predictedPoints.Add(position);

        // Simulate using IDENTICAL physics to SpaceshipController2D
        while (currentTime < maxPredictionTime && predictedPoints.Count < maxPredictionPoints)
        {
            // 1. EXACT SAME gravity calculation as ApplyRealisticGravity()
            Vector2 totalGravityForce = Vector2.zero;

            foreach (SimpleGravitationalBody2D body in gravityBodies)
            {
                if (body == null) continue;

                Vector2 direction = (Vector2)body.transform.position - position;
                float distance = direction.magnitude;
                float gravityInfluenceRadius = body.GetInfluenceRadius();

                if (distance > gravityInfluenceRadius) continue;

                distance = Mathf.Max(distance, minimumGravityDistance);

                // Use the EXACT SAME force calculation as your ship
                Vector2 gravityForce = body.GetGravitationalForceOn(shipMass, position);
                totalGravityForce += gravityForce;
            }

            // 2. EXACT SAME thrust application as ApplyThrust()
            Vector2 totalForce = totalGravityForce;
            if (isThrusting)
            {
                totalForce += thrustDirection * thrustForce;
            }

            // 3. EXACT SAME physics integration as FixedUpdate
            if (totalForce.magnitude > 0.01f)
            {
                Vector2 acceleration = totalForce / shipMass;
                velocity += acceleration * timeStep;
            }

            // 4. EXACT SAME speed capping as CapSpeed()
            if (velocity.magnitude > maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            // 5. Update position
            position += velocity * timeStep;
            currentTime += timeStep;

            predictedPoints.Add(position);

            // Stop if hitting a body
            bool hitBody = false;
            foreach (var body in gravityBodies)
            {
                if (body == null) continue;
                float distanceToBody = Vector2.Distance(position, body.transform.position);
                if (distanceToBody < 0.8f) // Collision threshold
                {
                    hitBody = true;
                    break;
                }
            }

            if (hitBody) break;

            // Stop if going too far away
            if (Vector2.Distance(position, startPosition) > 100f) break;
        }

        UpdateLineRenderer();
    }

    void UpdateLineRenderer()
    {
        if (lineRenderer == null || predictedPoints.Count < 2)
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            return;
        }

        lineRenderer.positionCount = predictedPoints.Count;

        for (int i = 0; i < predictedPoints.Count; i++)
        {
            lineRenderer.SetPosition(i, predictedPoints[i]);
        }

        lineRenderer.colorGradient = trajectoryGradient;
        lineRenderer.startWidth = baseLineWidth;
        lineRenderer.endWidth = baseLineWidth * 0.3f;
    }

    public void ShowCircularOrbit(Vector3 center, float radius)
    {
        if (lineRenderer == null) return;

        predictedPoints.Clear();

        int segments = 100;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * 2f * Mathf.PI;
            Vector3 point = center + new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f
            );
            predictedPoints.Add(point);
        }

        UpdateLineRenderer();
    }

    public void ClearTrajectory()
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null) return;

        predictedPoints.Clear();
        lineRenderer.positionCount = 0;
    }

    public void SetVisible(bool visible)
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null) lineRenderer.enabled = visible;
    }
}
