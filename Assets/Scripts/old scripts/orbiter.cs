using System.Collections.Generic;
using UnityEngine;

public class orbiter : MonoBehaviour
{
    private manager manager;
    public GameObject centerObject;
    public float orbitSpeed = 1f;

    private float orbitRadius = 5f;
    private TrailRenderer trail;
    private Vector3 lastCenterPosition;
    private Vector3[] trailPositions = new Vector3[0]; // Start with an empty array

    void Start()
    {
        manager = FindObjectOfType<manager>();
        
        if (centerObject == null)
        {
            centerObject = transform.gameObject;
        }

        orbitRadius = Mathf.Abs((centerObject.transform.position - transform.position).magnitude);

        trail = GetComponent<TrailRenderer>();
        if (trail == null)
        {
            Debug.LogWarning("TrailRenderer component not found on orbiter.");
        }

        lastCenterPosition = centerObject.transform.position;
    }

    void Update()
    {
        // Orbit logic
        float angle = Time.time * orbitSpeed;
        float x = centerObject.transform.position.x + Mathf.Cos(angle) * orbitRadius;
        float y = centerObject.transform.position.y + Mathf.Sin(angle) * orbitRadius;
        transform.position = new Vector3(x, y, transform.position.z);

        // Local-space-like trail fix
        if (trail != null)
        {
            Vector3 delta = centerObject.transform.position - lastCenterPosition;

            int count = trail.positionCount;

            // Resize array if needed
            if (trailPositions.Length < count)
            {
                trailPositions = new Vector3[count];
            }

            trail.GetPositions(trailPositions);

            for (int i = 0; i < count; i++)
            {
                trailPositions[i] += delta;
            }

            trail.SetPositions(trailPositions);

            lastCenterPosition = centerObject.transform.position;
        }
    }
}
