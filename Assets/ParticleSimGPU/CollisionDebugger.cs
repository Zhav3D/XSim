using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visualizes collision events for the GPU particle simulation.
/// </summary>
[RequireComponent(typeof(GPUParticleSimulation))]
public class CollisionDebugger : MonoBehaviour
{
    [System.Serializable]
    public class CollisionEvent
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 velocity;
        public Vector3 newVelocity;
        public float time;
        public int particleType;

        public CollisionEvent(Vector3 pos, Vector3 norm, Vector3 vel, Vector3 newVel, int type)
        {
            position = pos;
            normal = norm;
            velocity = vel;
            newVelocity = newVel;
            time = Time.time;
            particleType = type;
        }
    }

    private GPUParticleSimulation simulation;
    private List<CollisionEvent> collisionEvents = new List<CollisionEvent>();
    private float eventLifetime = 2.0f; // How long to show collision events
    private int maxEvents = 100; // Maximum number of events to track
    private float checkInterval = 0.1f; // Check for collisions every N seconds
    private float lastCheckTime;

    // Set this to true to show more detailed collision visualization
    public bool showDetailed = false;

    // Previous particle data for detecting collisions
    private GPUParticleSimulation.GPUParticleData[] previousData;
    private GPUParticleSimulation.GPUParticleData[] currentData;

    // Sampling parameters
    private int sampleSize = 2000; // How many particles to sample
    private int sampleOffset = 0;  // Offset for sampling different particles each time

    void Start()
    {
        simulation = GetComponent<GPUParticleSimulation>();
        lastCheckTime = Time.time;
    }

    void Update()
    {
        // Clean up old events
        collisionEvents.RemoveAll(e => Time.time - e.time > eventLifetime);

        // Check for new collisions at interval
        if (Time.time - lastCheckTime > checkInterval)
        {
            DetectBoundaryCollisions();
            lastCheckTime = Time.time;

            // Update sample offset to check different particles next time
            sampleOffset = (sampleOffset + sampleSize) % simulation.particleCount;
        }
    }

    void DetectBoundaryCollisions()
    {
        // Get current particle data
        currentData = simulation.GetParticleData();
        if (currentData == null || currentData.Length == 0) return;

        // First time initialization
        if (previousData == null)
        {
            previousData = new GPUParticleSimulation.GPUParticleData[currentData.Length];
            System.Array.Copy(currentData, previousData, currentData.Length);
            return;
        }

        // Check for boundary collisions in a subset of particles
        int endIndex = Mathf.Min(sampleOffset + sampleSize, currentData.Length);
        for (int i = sampleOffset; i < endIndex; i++)
        {
            if (i >= currentData.Length || i >= previousData.Length) continue;

            Vector3 prevPos = previousData[i].position;
            Vector3 currPos = currentData[i].position;
            Vector3 prevVel = previousData[i].velocity;
            Vector3 currVel = currentData[i].velocity;

            // Detect sudden velocity changes (potential collision)
            if (Vector3.Dot(prevVel.normalized, currVel.normalized) < 0.8f &&
                currVel.sqrMagnitude > 0.01f && prevVel.sqrMagnitude > 0.01f)
            {
                // Calculate approximate collision normal
                Vector3 normal = Vector3.zero;

                switch (simulation.boundsShape)
                {
                    case GPUParticleSimulation.BoundsShape.Box:
                        // Determine which face was hit
                        Vector3 halfSize = simulation.simulationBounds * 0.5f;

                        if (Mathf.Abs(currPos.x) > halfSize.x * 0.95f)
                            normal = new Vector3(Mathf.Sign(currPos.x), 0, 0);
                        else if (Mathf.Abs(currPos.y) > halfSize.y * 0.95f)
                            normal = new Vector3(0, Mathf.Sign(currPos.y), 0);
                        else if (Mathf.Abs(currPos.z) > halfSize.z * 0.95f)
                            normal = new Vector3(0, 0, Mathf.Sign(currPos.z));
                        break;

                    case GPUParticleSimulation.BoundsShape.Sphere:
                        // For sphere, normal points outward from center
                        if (currPos.magnitude > simulation.sphereRadius * 0.95f)
                            normal = currPos.normalized;
                        break;

                    case GPUParticleSimulation.BoundsShape.Cylinder:
                        // For cylinder, check if horizontal or vertical collision
                        float halfHeight = simulation.cylinderHeight * 0.5f;

                        if (Mathf.Abs(currPos.y) > halfHeight * 0.95f)
                        {
                            normal = new Vector3(0, Mathf.Sign(currPos.y), 0);
                        }
                        else
                        {
                            // Radial collision
                            Vector2 horizontal = new Vector2(currPos.x, currPos.z);
                            if (horizontal.magnitude > simulation.cylinderRadius * 0.95f)
                            {
                                normal = new Vector3(horizontal.normalized.x, 0, horizontal.normalized.y);
                            }
                        }
                        break;
                }

                // If we could determine a normal, record the collision
                if (normal != Vector3.zero)
                {
                    collisionEvents.Add(new CollisionEvent(
                        transform.position + currPos,
                        normal,
                        prevVel,
                        currVel,
                        currentData[i].typeIndex
                    ));

                    // Limit list size
                    if (collisionEvents.Count > maxEvents)
                    {
                        collisionEvents.RemoveAt(0);
                    }
                }
            }
        }

        // Store current data for next comparison
        System.Array.Copy(currentData, previousData, currentData.Length);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || collisionEvents.Count == 0) return;

        foreach (var collision in collisionEvents)
        {
            // Fade based on age
            float age = Time.time - collision.time;
            float alpha = 1.0f - (age / eventLifetime);

            // Draw collision point
            Gizmos.color = new Color(1, 0, 0, alpha);
            Gizmos.DrawSphere(collision.position, 0.2f);

            // Draw collision normal
            Gizmos.color = new Color(0, 1, 0, alpha);
            Gizmos.DrawRay(collision.position, collision.normal * 1.0f);

            if (showDetailed)
            {
                // Draw incoming velocity
                Gizmos.color = new Color(1, 1, 0, alpha);
                Gizmos.DrawRay(collision.position, collision.velocity.normalized * 1.0f);

                // Draw outgoing velocity
                Gizmos.color = new Color(0, 0, 1, alpha);
                Gizmos.DrawRay(collision.position, collision.newVelocity.normalized * 1.0f);
            }
        }
    }
}