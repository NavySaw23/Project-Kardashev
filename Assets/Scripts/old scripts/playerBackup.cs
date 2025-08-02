// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// public class player : MonoBehaviour
// {
//     private manager manager;
//     public GameObject centerObject;
//     public float orbitSpeed = 1f;
//     private float orbitRadius = 5f;
//     public float throttleSpeed = 1f;
//     private float currentAngle = 0f;
//     private Vector3 throttleDirection;
//     private bool wasThrottling = false;
//     private GameObject previousCenterObject; // Track previous center for color management
//     private Color originalColor; // Store the original color

//     void Start()
//     {
//         manager = FindObjectOfType<manager>();
//         if (centerObject == null)
//         {
//             centerObject = FindNearestCelestialObject();
//         }

//         if (centerObject != null)
//         {
//             SetCenterObjectColor(centerObject, true);
//             previousCenterObject = centerObject;
//             orbitRadius = Mathf.Abs((centerObject.transform.position - transform.position).magnitude);

//             Vector3 directionFromCenter = transform.position - centerObject.transform.position;
//             currentAngle = Mathf.Atan2(directionFromCenter.y, directionFromCenter.x);
//         }
//     }

//     void Update()
//     {
//         if (Input.GetKey(KeyCode.Space))
//         {
//             throttle();
//         }
//         else
//         {
//             orbit();
//         }
//     }

//     void throttle()
//     {
//         if (!wasThrottling)
//         {
//             if (centerObject != null)
//             {
//                 // Calculate tangent direction based on current orbital position
//                 Vector3 directionFromCenter = transform.position - centerObject.transform.position;
//                 float currentAngleInOrbit = Mathf.Atan2(directionFromCenter.y, directionFromCenter.x);

//                 // Create tangent vector (perpendicular to radial direction)
//                 // Using the current orbital direction for consistency
//                 throttleDirection = new Vector3(-Mathf.Sin(currentAngleInOrbit), Mathf.Cos(currentAngleInOrbit), 0f);

//                 // Determine orbital direction based on orbitSpeed sign for consistency
//                 if (orbitSpeed < 0)
//                 {
//                     throttleDirection = -throttleDirection; // Reverse for clockwise
//                 }
//             }
//             else
//             {
//                 throttleDirection = transform.right;
//             }

//             wasThrottling = true;
//         }

//         transform.position += throttleDirection * throttleSpeed * Time.deltaTime;
//         float angle = Mathf.Atan2(throttleDirection.y, throttleDirection.x) * Mathf.Rad2Deg;
//         transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
//     }


//     void orbit()
//     {
//         if (wasThrottling)
//         {
//             GameObject nearestCelestial = FindNearestCelestialObject();
//             if (nearestCelestial != null)
//             {
//                 // Restore previous center object color
//                 if (previousCenterObject != null && previousCenterObject != nearestCelestial)
//                 {
//                     SetCenterObjectColor(previousCenterObject, false);
//                 }

//                 // Set new center object and make it red
//                 centerObject = nearestCelestial;
//                 SetCenterObjectColor(centerObject, true);
//                 previousCenterObject = centerObject;

//                 if (centerObject != null)
//                 {
//                     orbitRadius = Vector3.Distance(centerObject.transform.position, transform.position);
//                     Vector3 directionFromCenter = transform.position - centerObject.transform.position;
//                     currentAngle = Mathf.Atan2(directionFromCenter.y, directionFromCenter.x);

//                     // Determine orbital direction based on approach velocity
//                     Vector3 velocityDirection = throttleDirection.normalized;
//                     Vector3 radialDirection = directionFromCenter.normalized;

//                     // Calculate cross product to determine rotation direction
//                     // Positive cross product = counter-clockwise, negative = clockwise
//                     float crossProduct = velocityDirection.x * radialDirection.y - velocityDirection.y * radialDirection.x;

//                     // Set orbit speed direction based on approach
//                     orbitSpeed = Mathf.Abs(orbitSpeed) * (crossProduct >= 0 ? 1f : -1f);
//                 }
//             }

//             wasThrottling = false;
//         }

//         if (centerObject != null)
//         {
//             currentAngle += orbitSpeed * Time.deltaTime;
//             float x = centerObject.transform.position.x + Mathf.Cos(currentAngle) * orbitRadius;
//             float y = centerObject.transform.position.y + Mathf.Sin(currentAngle) * orbitRadius;
//             transform.position = new Vector3(x, y, transform.position.z);

//             Vector3 directionToCenter = (centerObject.transform.position - transform.position).normalized;
//             Vector3 movementDirection = new Vector3(-directionToCenter.y, directionToCenter.x, 0f);

//             // Adjust movement direction based on orbital direction
//             if (orbitSpeed < 0)
//             {
//                 movementDirection = -movementDirection;
//             }

//             float rotationAngle = Mathf.Atan2(movementDirection.y, movementDirection.x) * Mathf.Rad2Deg;
//             transform.rotation = Quaternion.AngleAxis(rotationAngle, Vector3.forward);
//         }
//     }


//     GameObject FindNearestCelestialObject()
//     {
//         GameObject[] celestialObjects = GameObject.FindGameObjectsWithTag("celestial");

//         if (celestialObjects.Length == 0)
//         {
//             return null;
//         }

//         GameObject nearest = null;
//         float shortestDistance = Mathf.Infinity;

//         foreach (GameObject celestial in celestialObjects)
//         {
//             float distance = Vector3.Distance(transform.position, celestial.transform.position);
//             if (distance < shortestDistance)
//             {
//                 shortestDistance = distance;
//                 nearest = celestial;
//             }
//         }

//         return nearest;
//     }

//     void SetCenterObjectColor(GameObject obj, bool isCenter)
//     {
//         if (obj == null) return;

//         SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
//         if (spriteRenderer == null) return;

//         if (isCenter)
//         {
//             // Store original color and make it red
//             originalColor = spriteRenderer.color;
//             spriteRenderer.color = Color.red;
//         }
//         else
//         {
//             spriteRenderer.color = originalColor;
//         }
//     }
// }
