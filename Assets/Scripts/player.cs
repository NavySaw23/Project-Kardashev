using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class player : MonoBehaviour
{
    /* ---------------- Inspector ---------------- */
    public GameObject centerObject;      // Body we are orbiting
    public float linearOrbitSpeed = 5f;  // Desired *linear* (tangential) speed while orbiting
    public float throttleSpeed     = 8f; // Units-per-second while the player is throttling
    public KeyCode throttleKey     = KeyCode.Space;

    /* ---------------- Internals ---------------- */
    private manager manager;                 // Whatever game-wide manager you use
    private float   currentAngle;            // Polar angle around centre (rad)
    private float   orbitRadius;             // Current orbit radius (world units)
    private float   orbitSpeed;              // ANGULAR speed (rad/s) – recalculated to keep linear speed constant
    private int     orbitDirection = 1;      // +1 = CCW, -1 = CW
    private Vector3 throttleDirection;       // Direction we push while throttling
    private bool    wasThrottling = false;   // State switch helper

    /* Colour handling */
    private GameObject previousCenterObject;
    private Color   originalColor;

    /* ====================================================================================== */
    /*                                          LIFE-CYCLE                                    */
    /* ====================================================================================== */
    void Start()
    {
        manager = FindObjectOfType<manager>();

        if (centerObject == null)
            centerObject = FindNearestCelestialObject();

        if (centerObject != null)
        {
            SetCenterObjectColor(centerObject, true);
            previousCenterObject = centerObject;

            // Initial polar data
            Vector3 fromCentre = transform.position - centerObject.transform.position;
            orbitRadius  = fromCentre.magnitude;
            currentAngle = Mathf.Atan2(fromCentre.y, fromCentre.x);

            UpdateOrbitSpeed();              // Ensures constant linear velocity
        }
    }

    void Update()
    {
        if (Input.GetKey(throttleKey))
            Throttle();
        else
            Orbit();
    }

    /* ====================================================================================== */
    /*                                          MOVEMENT                                      */
    /* ====================================================================================== */
    void Throttle()
    {
        /* ------------ first frame of throttle ------------ */
        if (!wasThrottling)
        {
            /* Choose a tangent direction based on current orbital position */
            if (centerObject != null)
            {
                Vector3 fromCentre = transform.position - centerObject.transform.position;
                float   angle      = Mathf.Atan2(fromCentre.y, fromCentre.x);
                throttleDirection  = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);

                /* Flip tangent if we were orbiting clockwise */
                if (orbitDirection < 0)
                    throttleDirection = -throttleDirection;
            }
            else
            {
                throttleDirection = transform.right;
            }
            wasThrottling = true;
        }

        /* ------------ move ------------ */
        transform.position += throttleDirection.normalized * throttleSpeed * Time.deltaTime;

        /* Rotate sprite to face velocity */
        float facingAngle = Mathf.Atan2(throttleDirection.y, throttleDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(facingAngle, Vector3.forward);
    }

    void Orbit()
    {
        /* ------------ just stopped throttling → pick new centre ------------ */
        if (wasThrottling)
        {
            GameObject nearest = FindNearestCelestialObject();
            if (nearest != null)
            {
                if (previousCenterObject != null && previousCenterObject != nearest)
                    SetCenterObjectColor(previousCenterObject, false);

                centerObject = nearest;
                SetCenterObjectColor(centerObject, true);
                previousCenterObject = centerObject;
            }

            /* Establish new polar data & direction of approach */
            if (centerObject != null)
            {
                orbitRadius = Vector3.Distance(centerObject.transform.position, transform.position);
                Vector3 fromCentre = transform.position - centerObject.transform.position;
                currentAngle = Mathf.Atan2(fromCentre.y, fromCentre.x);

                /* Determine clockwise vs counter-clockwise from last throttle direction */
                Vector3 velocityDir   = throttleDirection.normalized;
                Vector3 radialDir     = fromCentre.normalized;
                float   cross         = velocityDir.x * radialDir.y - velocityDir.y * radialDir.x;
                orbitDirection        = (cross >= 0f) ? 1 : -1;

                UpdateOrbitSpeed();
            }
            wasThrottling = false;
        }

        /* ------------ do the orbit ------------ */
        if (centerObject == null) return;

        currentAngle += orbitSpeed * Time.deltaTime;            // radians
        float x = centerObject.transform.position.x + Mathf.Cos(currentAngle) * orbitRadius;
        float y = centerObject.transform.position.y + Mathf.Sin(currentAngle) * orbitRadius;
        transform.position = new Vector3(x, y, transform.position.z);

        /* Rotate sprite so “nose” follows tangential path */
        Vector3 toCentre       = (centerObject.transform.position - transform.position).normalized;
        Vector3 tangent        = new Vector3(-toCentre.y, toCentre.x, 0f) * orbitDirection;
        float   spriteRotation = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(spriteRotation, Vector3.forward);
    }

    /* ====================================================================================== */
    /*                                          HELPERS                                       */
    /* ====================================================================================== */
    void UpdateOrbitSpeed()
    {
        if (orbitRadius < 0.001f) return;           // avoid divide-by-zero
        orbitSpeed = orbitDirection * (linearOrbitSpeed / orbitRadius); // ω = v / r
    }

    GameObject FindNearestCelestialObject()
    {
        GameObject[] celestial = GameObject.FindGameObjectsWithTag("celestial");
        if (celestial.Length == 0) return null;

        GameObject nearest = null;
        float       minSqr = Mathf.Infinity;
        foreach (GameObject obj in celestial)
        {
            float distSqr = (transform.position - obj.transform.position).sqrMagnitude;
            if (distSqr < minSqr)
            {
                minSqr = distSqr;
                nearest = obj;
            }
        }
        return nearest;
    }

    void SetCenterObjectColor(GameObject obj, bool isCenter)
    {
        if (obj == null) return;
        SpriteRenderer sr = obj.GetComponent<SpriteRenderer>();
        if (sr == null) return;

        if (isCenter)
        {
            originalColor = sr.color;
            sr.color      = Color.red;
        }
        else
        {
            sr.color = originalColor;
        }
    }
}
