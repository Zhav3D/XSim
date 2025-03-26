using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component to visualize the bounds of the GPU particle simulation in play mode.
/// </summary>
[RequireComponent(typeof(GPUParticleSimulation))]
public class BoundsVisualizer : MonoBehaviour
{
    private GPUParticleSimulation simulation;
    private LineRenderer lineRenderer;

    // Cached parameters to detect changes
    private GPUParticleSimulation.BoundsShape lastBoundsShape;
    private Vector3 lastBoxSize;
    private float lastSphereRadius;
    private float lastCylinderRadius;
    private float lastCylinderHeight;

    // Visualization settings
    private int boxVertexCount = 24; // 12 edges, 2 points each
    private int sphereVertexCount = 64; // Higher value for smoother sphere
    private int cylinderVertexCount = 128; // Includes top, bottom circles and connecting lines
    private Color boundsColor = new Color(1f, 0.92f, 0.016f, 0.8f); // Yellow, semi-transparent

    // Debug settings
    private bool debugOutOfBounds = false;
    private int debugParticleCount = 100; // How many particles to sample for debugging

    void Start()
    {
        simulation = GetComponent<GPUParticleSimulation>();

        lineRenderer = gameObject.GetComponent<LineRenderer>();
        // Create line renderer if not present
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }
        lineRenderer.startColor = boundsColor;
        lineRenderer.endColor = boundsColor;
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;

        // Initialize cached parameters
        lastBoundsShape = simulation.boundsShape;
        lastBoxSize = simulation.simulationBounds;
        lastSphereRadius = simulation.sphereRadius;
        lastCylinderRadius = simulation.cylinderRadius;
        lastCylinderHeight = simulation.cylinderHeight;

        UpdateVisualizer();
    }

    void Update()
    {
        // Only update if parameters have changed
        CheckForParameterChanges();
    }

    private bool CheckForParameterChanges()
    {
        bool parametersChanged = false;

        // Check if bounds parameters have changed
        if (lastBoundsShape != simulation.boundsShape)
        {
            lastBoundsShape = simulation.boundsShape;
            parametersChanged = true;
        }

        switch (simulation.boundsShape)
        {
            case GPUParticleSimulation.BoundsShape.Box:
                if (lastBoxSize != simulation.simulationBounds)
                {
                    lastBoxSize = simulation.simulationBounds;
                    parametersChanged = true;
                }
                break;

            case GPUParticleSimulation.BoundsShape.Sphere:
                if (Mathf.Abs(lastSphereRadius - simulation.sphereRadius) > 0.01f)
                {
                    lastSphereRadius = simulation.sphereRadius;
                    parametersChanged = true;
                }
                break;

            case GPUParticleSimulation.BoundsShape.Cylinder:
                if (Mathf.Abs(lastCylinderRadius - simulation.cylinderRadius) > 0.01f ||
                    Mathf.Abs(lastCylinderHeight - simulation.cylinderHeight) > 0.01f)
                {
                    lastCylinderRadius = simulation.cylinderRadius;
                    lastCylinderHeight = simulation.cylinderHeight;
                    parametersChanged = true;
                }
                break;
        }

        // Update visualizer if needed
        if (parametersChanged)
        {
            UpdateVisualizer();
        }

        return parametersChanged;
    }

    public void UpdateVisualizer()
    {
        if (lineRenderer == null) return;

        switch (simulation.boundsShape)
        {
            case GPUParticleSimulation.BoundsShape.Box:
                DrawBoxBounds();
                break;

            case GPUParticleSimulation.BoundsShape.Sphere:
                DrawSphereBounds();
                break;

            case GPUParticleSimulation.BoundsShape.Cylinder:
                DrawCylinderBounds();
                break;
        }
    }

    private void DrawBoxBounds()
    {
        Vector3 halfSize = simulation.simulationBounds * 0.5f;
        lineRenderer.positionCount = boxVertexCount;

        // Define vertices of the box
        Vector3[] vertices = new Vector3[8];
        vertices[0] = new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
        vertices[1] = new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
        vertices[2] = new Vector3(halfSize.x, -halfSize.y, halfSize.z);
        vertices[3] = new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
        vertices[4] = new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
        vertices[5] = new Vector3(halfSize.x, halfSize.y, -halfSize.z);
        vertices[6] = new Vector3(halfSize.x, halfSize.y, halfSize.z);
        vertices[7] = new Vector3(-halfSize.x, halfSize.y, halfSize.z);

        // Define edges (12 edges, each with 2 points)
        int[] indices = new int[]
        {
            // Bottom face
            0, 1, 1, 2, 2, 3, 3, 0,
            // Top face
            4, 5, 5, 6, 6, 7, 7, 4,
            // Connecting edges
            0, 4, 1, 5, 2, 6, 3, 7
        };

        // Set positions in line renderer
        for (int i = 0; i < indices.Length; i++)
        {
            lineRenderer.SetPosition(i, transform.position + vertices[indices[i]]);
        }
    }

    private void DrawSphereBounds()
    {
        int segments = sphereVertexCount;
        lineRenderer.positionCount = segments + 1;

        float radius = simulation.sphereRadius;

        // Draw three circles to represent the sphere (XY, XZ, YZ planes)
        DrawCircle(segments / 3, radius, Vector3.right, Vector3.up);
        DrawCircle(segments / 3, radius, Vector3.forward, Vector3.up, segments / 3);
        DrawCircle(segments / 3, radius, Vector3.right, Vector3.forward, 2 * segments / 3);
    }

    private void DrawCircle(int segments, float radius, Vector3 xAxis, Vector3 yAxis, int startIndex = 0)
    {
        float angleStep = 2f * Mathf.PI / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 pos = transform.position +
                          (xAxis * Mathf.Cos(angle) + yAxis * Mathf.Sin(angle)) * radius;

            int index = startIndex + i;
            if (index < lineRenderer.positionCount)
            {
                lineRenderer.SetPosition(index, pos);
            }
        }
    }

    private void DrawCylinderBounds()
    {
        float radius = simulation.cylinderRadius;
        float height = simulation.cylinderHeight;
        float halfHeight = height * 0.5f;

        // We need positions for: top circle, bottom circle, and connecting lines
        int circleSegments = cylinderVertexCount / 3;
        lineRenderer.positionCount = cylinderVertexCount;

        // Draw bottom circle
        Vector3 bottomCenter = transform.position - new Vector3(0, halfHeight, 0);
        for (int i = 0; i <= circleSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / circleSegments;
            Vector3 pos = bottomCenter + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );

            lineRenderer.SetPosition(i, pos);
        }

        // Draw top circle
        Vector3 topCenter = transform.position + new Vector3(0, halfHeight, 0);
        for (int i = 0; i <= circleSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / circleSegments;
            Vector3 pos = topCenter + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );

            lineRenderer.SetPosition(circleSegments + 1 + i, pos);
        }

        // Draw connecting lines
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI / 2; // Four lines at 90 degree intervals

            Vector3 bottomPos = bottomCenter + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );

            Vector3 topPos = topCenter + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            );

            int baseIndex = 2 * (circleSegments + 1) + i * 2;

            lineRenderer.SetPosition(baseIndex, bottomPos);
            lineRenderer.SetPosition(baseIndex + 1, topPos);
        }
    }

    /// <summary>
    /// Toggle debug visualization of particles that are out of bounds
    /// </summary>
    public void ToggleOutOfBoundsDebug()
    {
        debugOutOfBounds = !debugOutOfBounds;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || simulation == null || !debugOutOfBounds) return;

        DebugOutOfBoundsParticles();
    }

    private void DebugOutOfBoundsParticles()
    {
        if (!Application.isPlaying || simulation == null) return;

        // Access the GetParticleData method to get a sample of particles
        var particles = simulation.GetParticleData();
        if (particles == null) return;

        // Sample a subset of particles
        int sampleSize = Mathf.Min(debugParticleCount, particles.Length);
        int step = particles.Length / sampleSize;

        for (int i = 0; i < particles.Length; i += step)
        {
            if (i >= particles.Length) break;

            Vector3 pos = particles[i].position;
            float radius = particles[i].radius;

            bool isOutOfBounds = false;

            // Check if particle is outside bounds
            switch (simulation.boundsShape)
            {
                case GPUParticleSimulation.BoundsShape.Box:
                    Vector3 halfBounds = simulation.simulationBounds * 0.5f;
                    isOutOfBounds = Mathf.Abs(pos.x) > halfBounds.x - radius ||
                                   Mathf.Abs(pos.y) > halfBounds.y - radius ||
                                   Mathf.Abs(pos.z) > halfBounds.z - radius;
                    break;

                case GPUParticleSimulation.BoundsShape.Sphere:
                    isOutOfBounds = pos.magnitude > simulation.sphereRadius - radius;
                    break;

                case GPUParticleSimulation.BoundsShape.Cylinder:
                    float horizontalDist = new Vector2(pos.x, pos.z).magnitude;
                    float halfHeight = simulation.cylinderHeight * 0.5f;
                    isOutOfBounds = horizontalDist > simulation.cylinderRadius - radius ||
                                   Mathf.Abs(pos.y) > halfHeight - radius;
                    break;
            }

            if (isOutOfBounds)
            {
                // Draw red sphere at out-of-bounds particle
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(transform.position + pos, radius);

                // Draw line to show velocity
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(
                    transform.position + pos,
                    transform.position + pos + particles[i].velocity.normalized * radius * 2
                );
            }
        }
    }

    void OnDestroy()
    {
        // Clean up the line renderer
        if (lineRenderer != null)
        {
            Destroy(lineRenderer);
        }
    }
}