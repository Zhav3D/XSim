using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// GPU-accelerated particle simulation capable of handling millions of particles.
/// This implementation preserves asymmetric particle interactions from the original system.
/// </summary>
[RequireComponent(typeof(GPUInteractionMatrixGenerator))]
public class GPUParticleSimulation : MonoBehaviour
{
    #region Structures

    // Struct must match layout in compute shader
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUParticleData
    {
        public Vector3 position;
        public Vector3 velocity;
        public int typeIndex;
        public float mass;
        public float radius;
        public float padding; // Ensure 16-byte alignment
    }

    // Particle type definition, visible in inspector
    [System.Serializable]
    public class ParticleType
    {
        public string name;
        public Color color = Color.white;
        public float mass = 1f;
        public float radius = 0.5f;
        public float spawnAmount = 50;
    }

    // Interaction rule definition, visible in inspector
    [System.Serializable]
    public class InteractionRule
    {
        public int typeIndexA;
        public int typeIndexB;
        public float attractionValue; // Positive for attraction, negative for repulsion
    }

    public enum BoundsShape
    {
        Box,
        Sphere,
        Cylinder
    }

    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct MergedParticleData
    {
        public Vector3 position;
        public Vector3 velocity;
        public int typeIndex;
        public float mass;
        public float radius;
        public int parentCount;    // Number of original particles combined
        public int lodLevel;       // Hierarchy level (0 = original)
    }

    #endregion

    #region Simulation Parameters

    [Header("Compute Resources")]
    public ComputeShader simulationShader;
    public Mesh particleMesh;
    public Material particleMaterial;
    public Shader particleShader;

    [Header("Simulation Settings")]
    public bool startParticlesAtCenter = false;
    [Range(0f, 5f)] public float simulationSpeed = 1.0f;
    [Range(0f, 1f)] public float collisionElasticity = 0.5f;
    public Vector3 simulationBounds = new Vector3(10f, 10f, 10f);
    public float dampening = 0.95f; // Air resistance / friction
    public float interactionStrength = 1f; // Global multiplier for interaction forces
    public float minDistance = 0.5f; // Minimum distance to prevent extreme forces
    public float bounceForce = 0.8f; // How much velocity is preserved on collision with boundaries
    public float maxForce = 100f; // Maximum force magnitude to prevent instability
    public float maxVelocity = 20f; // Maximum velocity magnitude to prevent instability
    public float interactionRadius = 10f; // Maximum distance for particle interactions

    [Header("Performance Optimization")]
    public float cellSize = 2.5f; // Size of each grid cell, should be >= interactionRadius/2
    [Range(1, 8)] public int threadGroupSize = 8; // For 3D spatial grid

    [Header("Hierarchical LOD Settings")]
    public bool enableHierarchicalLOD = true;
    public float lodDistanceThreshold = 50f;      // Distance at which merging begins
    public float lodDistanceMultiplier = 2.0f;    // How much to expand cell size per level
    public int maxLodLevels = 3;                  // Maximum hierarchical depth
    public float minParticlesForMerging = 5f;     // Min particles needed to create a merged particle

    [Header("Bounds Settings")]
    public BoundsShape boundsShape = BoundsShape.Box;
    public float sphereRadius = 10f;  // For spherical bounds
    public float cylinderRadius = 10f; // For cylindrical bounds
    public float cylinderHeight = 20f; // For cylindrical bounds

    // Statistics and debug
    [Header("Debug Info")]
    public bool debugDrawCells;
    public bool debugDrawParticles;
    [Space(8)]
    public int particleCount;
    public int activeParticleCount;
    public int gridCellCount;
    public float frameTimeMs;

    [Header("Particle Types")]
    public List<ParticleType> particleTypes = new List<ParticleType>();

    [Header("Interaction Rules")]
    public List<InteractionRule> interactionRules = new List<InteractionRule>();

    #endregion

    #region Private Fields

    // Compute shader kernels
    private int initKernel;
    private int gridAssignmentKernel;
    private int gridCountingKernel;
    private int gridSortingKernel;
    private int forceKernel;
    private int integrateKernel;
    private int resetGridKernel;
    private int validateKernel;

    private int identifyMergeCandidatesKernel;
    private int createMergedParticlesKernel;
    private int calculateHierarchicalForcesKernel;

    // Compute buffers
    private ComputeBuffer particleBuffer;
    private ComputeBuffer typesBuffer;
    private ComputeBuffer interactionBuffer;
    private ComputeBuffer gridBuffer;
    private ComputeBuffer gridCountBuffer;
    private ComputeBuffer gridOffsetBuffer;
    private ComputeBuffer argsBuffer;

    // LOD-related buffers
    private ComputeBuffer mergedParticleBuffer;    // Stores merged particles
    private ComputeBuffer lodCellBuffer;           // Stores LOD cell assignments
    private ComputeBuffer lodCellCountBuffer;      // Counts particles per LOD cell
    private ComputeBuffer lodCellStartBuffer;      // Start indices for particles in LOD cells

    // LOD tracking variables
    private int maxMergedParticles;
    public int activeMergedParticles { get; private set; }
    private int[] lodCellCounts;
    private int totalLodCells;

    // Rendering resources
    private Material instancedMaterial;
    private MaterialPropertyBlock propertyBlock;
    private ComputeBuffer colorBuffer;

    // Grid parameters
    private int gridSizeX, gridSizeY, gridSizeZ;
    private int totalGridCells;

    // Performance monitoring
    private float[] frameTimes = new float[60];
    private int frameTimeIndex = 0;
    private float accumulatedTime = 0f;
    private int frameCount = 0;

    // Runtime data
    private GPUParticleData[] particleData;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int maxParticleCount;
    private int typeCount;
    private bool initialized = false;
    private bool needsReset = false;

    // Integration with matrix generator
    private GPUInteractionMatrixGenerator matrixGenerator;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        matrixGenerator = GetComponent<GPUInteractionMatrixGenerator>();
        Initialize();
    }

    void OnValidate()
    {
        // Ensure valid parameters
        cellSize = Mathf.Max(minDistance * 2, cellSize);
    }

    void Update()
    {
        if (!initialized || needsReset)
        {
            if (initialized) CleanupResources();
            Initialize();
            needsReset = false;
            return;
        }

        // Track performance
        MonitorPerformance();

        // Update shader parameters
        UpdateShaderParameters();

        // Run simulation step
        RunSimulation(Time.deltaTime * simulationSpeed);

        // Render particles
        RenderParticles();
    }

    void OnDestroy()
    {
        CleanupResources();
    }

    void OnDrawGizmos()
    {
        // Draw simulation bounds
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, simulationBounds);

        // Draw grid cells for debugging
        if (Application.isPlaying && initialized)
        {
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Vector3 cellWorldSize = new Vector3(cellSize, cellSize, cellSize);
            Vector3 origin = transform.position - simulationBounds * 0.5f;

            if (debugDrawCells)
            {
                // Only draw a subset of grid cells when there are many
                int skipFactor = Mathf.Max(1, gridSizeX / 20);
                for (int x = 0; x < gridSizeX; x += skipFactor)
                {
                    for (int y = 0; y < gridSizeY; y += skipFactor)
                    {
                        for (int z = 0; z < gridSizeZ; z += skipFactor)
                        {
                            Vector3 cellPos = origin + new Vector3(
                                (x + 0.5f) * cellSize,
                                (y + 0.5f) * cellSize,
                                (z + 0.5f) * cellSize
                            );
                            Gizmos.DrawWireCube(cellPos, cellWorldSize);
                        }
                    }
                }
            }
            if (debugDrawParticles)
                DebugRenderParticles();
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw simulation bounds based on shape
        switch (boundsShape)
        {
            case BoundsShape.Box:
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(transform.position, simulationBounds);
                break;

            case BoundsShape.Sphere:
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, sphereRadius);
                break;

            case BoundsShape.Cylinder:
                Gizmos.color = Color.yellow;
                DrawWireCylinder(transform.position, cylinderRadius, cylinderHeight);
                break;
        }
    }

    void DrawWireCylinder(Vector3 position, float radius, float height)
    {
        // Draw top and bottom circles
        float halfHeight = height * 0.5f;
        DrawWireCircle(position + new Vector3(0, halfHeight, 0), radius);
        DrawWireCircle(position - new Vector3(0, halfHeight, 0), radius);

        // Draw connecting lines
        int segments = 12;
        float angleStep = 2 * Mathf.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            Vector3 bottomPoint = position + new Vector3(x, -halfHeight, z);
            Vector3 topPoint = position + new Vector3(x, halfHeight, z);
            Gizmos.DrawLine(bottomPoint, topPoint);
        }
    }

    void DrawWireCircle(Vector3 position, float radius)
    {
        int segments = 32;
        float angleStep = 2 * Mathf.PI / segments;
        Vector3 prevPoint = position + new Vector3(radius, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 nextPoint = position + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }

    #endregion

    #region Initialization

    public void Initialize()
    {
        if (particleTypes.Count == 0 || simulationShader == null)
        {
            Debug.LogWarning("Cannot initialize GPU Particle Simulation: Missing particle types or compute shader.");
            return;
        }

        // Get reference to matrix generator and ensure interactions are updated
        matrixGenerator = GetComponent<GPUInteractionMatrixGenerator>();
        if (matrixGenerator != null)
        {
            Debug.Log($"Matrix generator found with pattern: {matrixGenerator.patternType}");
        }
        else
        {
            Debug.LogWarning("No matrix generator found!");
        }

        // Calculate maximum particle count
        CalculateParticleCount();

        // Initialize grid
        InitializeGrid();

        // Create shader resources
        InitializeShaders();

        // Create buffers
        CreateBuffers();

        // Generate particles
        GenerateParticles();

        // Setup rendering
        SetupRendering();

        // Extra debug info
        Debug.Log($"Interaction rules count: {interactionRules.Count}");
        for (int i = 0; i < Math.Min(5, interactionRules.Count); i++)
        {
            var rule = interactionRules[i];
            Debug.Log($"Rule {i}: TypeA={rule.typeIndexA}, TypeB={rule.typeIndexB}, Value={rule.attractionValue}");
        }

        if (enableHierarchicalLOD)
        {
            InitializeHierarchicalLOD();
        }

        initialized = true;
    }

    private void CalculateParticleCount()
    {
        particleCount = 0;
        maxParticleCount = 0;
        typeCount = particleTypes.Count;

        foreach (var type in particleTypes)
        {
            particleCount += Mathf.CeilToInt(type.spawnAmount);
            maxParticleCount += Mathf.CeilToInt(type.spawnAmount * 1.5f); // Add some buffer
        }

        // Ensure power of 2 for better performance
        maxParticleCount = Mathf.NextPowerOfTwo(maxParticleCount);
        activeParticleCount = particleCount;
    }

    private void InitializeGrid()
    {
        cellSize = Mathf.Min(
            cellSize,
            Mathf.Min(simulationBounds.x, Mathf.Min(simulationBounds.y, simulationBounds.z)) / 4f
        );
        gridSizeX = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.x / cellSize));
        gridSizeY = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.y / cellSize));
        gridSizeZ = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.z / cellSize));
        totalGridCells = gridSizeX * gridSizeY * gridSizeZ;
        gridCellCount = totalGridCells;

        Debug.Log($"Spatial grid: {gridSizeX} x {gridSizeY} x {gridSizeZ} = {totalGridCells} cells");
    }

    private void InitializeShaders()
    {
        // Find all existing kernels
        initKernel = simulationShader.FindKernel("InitParticles");
        gridAssignmentKernel = simulationShader.FindKernel("AssignParticlesToGrid");
        gridCountingKernel = simulationShader.FindKernel("CountParticlesPerCell");
        gridSortingKernel = simulationShader.FindKernel("SortParticlesByCell");
        forceKernel = simulationShader.FindKernel("CalculateForces");
        integrateKernel = simulationShader.FindKernel("IntegrateParticles");
        resetGridKernel = simulationShader.FindKernel("ResetGrid");
        validateKernel = simulationShader.FindKernel("ValidateParticles");

        // Add these lines for the new hierarchical LOD kernels
        identifyMergeCandidatesKernel = simulationShader.FindKernel("IdentifyMergeCandidates");
        createMergedParticlesKernel = simulationShader.FindKernel("CreateMergedParticles");
        calculateHierarchicalForcesKernel = simulationShader.FindKernel("CalculateHierarchicalForces");
    }

    private void CreateBuffers()
    {
        // Create particle buffer
        int particleStride = Marshal.SizeOf(typeof(GPUParticleData));
        particleBuffer = new ComputeBuffer(maxParticleCount, particleStride);
        particleData = new GPUParticleData[maxParticleCount];

        // Initialize all particles (actual data will be generated in GPU)
        for (int i = 0; i < maxParticleCount; i++)
        {
            particleData[i] = new GPUParticleData
            {
                position = Vector3.zero,
                velocity = Vector3.zero,
                typeIndex = -1, // Invalid type to mark as inactive
                mass = 1f,
                radius = 0.5f
            };
        }
        particleBuffer.SetData(particleData);

        // Create types buffer
        TypeData[] typeData = new TypeData[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            typeData[i] = new TypeData
            {
                mass = particleTypes[i].mass,
                radius = particleTypes[i].radius,
                color = particleTypes[i].color
            };
        }
        typesBuffer = new ComputeBuffer(typeCount, 6 * sizeof(float)); // mass, radius, r, g, b
        typesBuffer.SetData(typeData);

        // Create interaction matrix buffer (non-symmetric)
        interactionBuffer = new ComputeBuffer(typeCount * typeCount, sizeof(float));
        float[] interactions = new float[typeCount * typeCount];

        // Initialize with zeros
        for (int i = 0; i < interactions.Length; i++)
        {
            interactions[i] = 0f;
        }

        // Set values from interaction rules
        foreach (var rule in interactionRules)
        {
            int index = rule.typeIndexA + rule.typeIndexB * typeCount;
            if (index >= 0 && index < interactions.Length)
            {
                interactions[index] = rule.attractionValue;
            }
            else
            {
                Debug.LogError($"Invalid rule index: {index} (TypeA={rule.typeIndexA}, TypeB={rule.typeIndexB})");
            }
        }
        interactionBuffer.SetData(interactions);

        // Create grid-related buffers
        gridBuffer = new ComputeBuffer(maxParticleCount, sizeof(int) * 2); // cellIndex, particleIndex
        gridCountBuffer = new ComputeBuffer(totalGridCells, sizeof(int));
        gridOffsetBuffer = new ComputeBuffer(totalGridCells, sizeof(int));

        // Create instanced rendering buffers
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        colorBuffer = new ComputeBuffer(typeCount, 4 * sizeof(float)); // r, g, b, a

        // Set color data
        Color[] colors = new Color[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            colors[i] = particleTypes[i].color;
        }
        colorBuffer.SetData(colors);
    }

    private void GenerateParticles()
    {
        // Setting data for initialization
        simulationShader.SetBuffer(initKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(initKernel, "ParticleTypes", typesBuffer);
        simulationShader.SetInt("ParticleCount", particleCount);
        simulationShader.SetInt("MaxParticleCount", maxParticleCount);
        simulationShader.SetInt("TypeCount", typeCount);
        simulationShader.SetVector("SimulationBounds", simulationBounds);

        // Type counts
        int[] typeCounts = new int[typeCount];
        int[] typeStartIndices = new int[typeCount];

        int currentIndex = 0;
        for (int i = 0; i < typeCount; i++)
        {
            typeStartIndices[i] = currentIndex;
            typeCounts[i] = Mathf.CeilToInt(particleTypes[i].spawnAmount);
            currentIndex += typeCounts[i];
        }

        ComputeBuffer typeCountsBuffer = new ComputeBuffer(typeCount, sizeof(int));
        ComputeBuffer typeStartIndicesBuffer = new ComputeBuffer(typeCount, sizeof(int));

        typeCountsBuffer.SetData(typeCounts);
        typeStartIndicesBuffer.SetData(typeStartIndices);

        simulationShader.SetBuffer(initKernel, "TypeCounts", typeCountsBuffer);
        simulationShader.SetBuffer(initKernel, "TypeStartIndices", typeStartIndicesBuffer);

        for (int i = 0; i < maxParticleCount; i++)
        {
            particleData[i].position = Vector3.zero;
            particleData[i].velocity = Vector3.zero;
            particleData[i].typeIndex = -1;
        }
        particleBuffer.SetData(particleData);

        simulationShader.SetBool("StartAtCenter", startParticlesAtCenter);
        simulationShader.SetInt("GlobalRandomSeed", UnityEngine.Random.Range(0, int.MaxValue));

        // Dispatch initialization
        int threadGroups = Mathf.CeilToInt(maxParticleCount / 64.0f);
        simulationShader.Dispatch(initKernel, threadGroups, 1, 1);

        // Clean up temporary buffers
        typeCountsBuffer.Release();
        typeStartIndicesBuffer.Release();

        Debug.Log($"Generated {particleCount} particles on GPU");
    }

    private void SetupRendering()
    {
        // Create material for instanced rendering
        if (instancedMaterial == null)
        {
            // Try with the explicit shader first
            if (particleShader != null)
            {
                instancedMaterial = new Material(particleShader);
                Debug.Log($"Created material with provided shader: {particleShader.name}");
            }
            else
            {
                // Try to find a simple URP shader that will work
                Shader[] fallbackOptions = new Shader[] {
                Shader.Find("Custom/SimpleGPUParticleShader"),       // Our simplified shader
                Shader.Find("Universal Render Pipeline/Particles/Unlit"),
                Shader.Find("Universal Render Pipeline/Unlit")
            };

                foreach (var shader in fallbackOptions)
                {
                    if (shader != null)
                    {
                        instancedMaterial = new Material(shader);
                        Debug.Log($"Created material with fallback shader: {shader.name}");
                        break;
                    }
                }

                if (instancedMaterial == null)
                {
                    Debug.LogError("Could not find any valid shader for particles! Create SimpleGPUParticleShader.shader");
                    return;
                }
            }
        }

        // Use the exact same buffer binding for both the material and property block
        if (particleBuffer != null && colorBuffer != null)
        {
            // Enable GPU instancing explicitly
            instancedMaterial.enableInstancing = true;

            // Set texture if needed
            if (particleMaterial != null && particleMaterial.mainTexture != null)
            {
                instancedMaterial.mainTexture = particleMaterial.mainTexture;
            }

            // Bind buffers to material
            instancedMaterial.SetBuffer("_ParticleBuffer", particleBuffer);
            instancedMaterial.SetBuffer("_ColorBuffer", colorBuffer);

            // Create property block and set the same buffers
            propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetBuffer("_ParticleBuffer", particleBuffer);
            propertyBlock.SetBuffer("_ColorBuffer", colorBuffer);

            Debug.Log("Buffers bound to material and property block");
        }
        else
        {
            Debug.LogError("Cannot set buffers - they're null!");
        }

        // Set up arguments for instanced rendering
        if (particleMesh != null)
        {
            args[0] = particleMesh.GetIndexCount(0);
            args[1] = (uint)activeParticleCount;
            args[2] = particleMesh.GetIndexStart(0);
            args[3] = particleMesh.GetBaseVertex(0);
            args[4] = 0; // Instance offset

            Debug.Log($"DrawMeshInstancedIndirect with {activeParticleCount} particles");
        }
        else
        {
            Debug.LogError("Particle mesh is null!");
        }

        // Update the args buffer
        argsBuffer.SetData(args);
    }

    // Add this method to explicitly sync matrix from GPU to CPU
    public void SyncMatrixFromGPU()
    {
        if (!initialized || interactionBuffer == null) return;

        float[] interactions = new float[typeCount * typeCount];
        interactionBuffer.GetData(interactions);

        // Debug first few values
        for (int i = 0; i < Math.Min(5, typeCount); i++)
        {
            for (int j = 0; j < Math.Min(5, typeCount); j++)
            {
                int idx = i + j * typeCount;
                Debug.Log($"Matrix[{i},{j}] = {interactions[idx]}");
            }
        }
    }

    // Add this method to explicitly sync matrix from generator to simulation
    public void SyncMatrixFromGenerator(GPUInteractionMatrixGenerator generator)
    {
        if (!initialized || interactionBuffer == null || generator == null) return;

        Debug.Log("Syncing matrix from generator to simulation");

        // We don't need to clear interaction rules since the generator already has
        // The most up-to-date rules after calling GenerateMatrix()

        // Create array for GPU buffer update
        float[] interactions = new float[typeCount * typeCount];

        // Initialize to zero
        for (int i = 0; i < interactions.Length; i++)
        {
            interactions[i] = 0f;
        }

        // Copy values from interactionRules to GPU buffer
        foreach (var rule in interactionRules)
        {
            int index = rule.typeIndexA + rule.typeIndexB * typeCount;
            if (index >= 0 && index < interactions.Length)
            {
                interactions[index] = rule.attractionValue;

                // Debug the value being sent to GPU
                Debug.Log($"Matrix[{rule.typeIndexA},{rule.typeIndexB}] = {rule.attractionValue}");
            }
            else
            {
                Debug.LogError($"Invalid rule index: {index} (TypeA={rule.typeIndexA}, TypeB={rule.typeIndexB})");
            }

            rule.attractionValue = Mathf.Clamp(rule.attractionValue, -2f, 2f);
        }

        // Update the GPU buffer
        interactionBuffer.SetData(interactions);

        Debug.Log($"Synced {interactionRules.Count} rules to GPU buffer");
    }

    private void DebugRenderParticles()
    {
        if (!initialized || particleBuffer == null) return;

        // Fetch a small sample of particle data for debugging
        int sampleSize = Mathf.Min(100, activeParticleCount);
        GPUParticleData[] debugData = new GPUParticleData[sampleSize];
        particleBuffer.GetData(debugData);

        for (int i = 0; i < sampleSize; i++)
        {
            var particle = debugData[i];
            if (particle.typeIndex >= 0 && particle.typeIndex < particleTypes.Count)
            {
                Color color = particleTypes[particle.typeIndex].color;
                float size = particle.radius * 2f;

                // Draw sphere at particle position
                Gizmos.color = color;
                Gizmos.DrawSphere(transform.position + particle.position, size);

                // Draw velocity vector
                if (particle.velocity.magnitude > 0.01f)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(
                        transform.position + particle.position,
                        particle.velocity.normalized * particle.radius * 2f
                    );
                }
            }
        }
    }

    #endregion

    #region Simulation

    private void UpdateShaderParameters()
    {
        // Update common parameters
        simulationShader.SetFloat("DeltaTime", Time.deltaTime * simulationSpeed);
        simulationShader.SetFloat("Dampening", dampening);
        simulationShader.SetFloat("InteractionStrength", interactionStrength);
        simulationShader.SetFloat("MinDistance", minDistance);
        simulationShader.SetFloat("BounceForce", bounceForce);
        simulationShader.SetFloat("MaxForce", maxForce);
        simulationShader.SetFloat("MaxVelocity", maxVelocity);
        simulationShader.SetFloat("InteractionRadius", interactionRadius);
        simulationShader.SetVector("HalfBounds", simulationBounds * 0.5f);
        simulationShader.SetFloat("CollisionElasticity", collisionElasticity);

        // Grid parameters
        simulationShader.SetFloat("CellSize", cellSize);
        simulationShader.SetInt("GridSizeX", gridSizeX);
        simulationShader.SetInt("GridSizeY", gridSizeY);
        simulationShader.SetInt("GridSizeZ", gridSizeZ);
        simulationShader.SetInt("TotalGridCells", totalGridCells);

        // Bounds parameters
        simulationShader.SetInt("BoundsShapeType", (int)boundsShape);
        simulationShader.SetFloat("SphereRadius", sphereRadius);
        simulationShader.SetFloat("CylinderRadius", cylinderRadius);
        simulationShader.SetFloat("CylinderHeight", cylinderHeight);

        // Pass camera info for LOD
        if (Camera.main != null)
        {
            simulationShader.SetMatrix("CameraToWorldMatrix", Camera.main.cameraToWorldMatrix);
            simulationShader.SetVector("CameraPosition", Camera.main.transform.position);
        }

        // Update LOD parameters if enabled
        if (enableHierarchicalLOD)
        {
            UpdateHierarchicalLODParameters();
        }

        // Update active particle count
        simulationShader.SetInt("ActiveParticleCount", activeParticleCount);
    }

    private void RunSimulation(float deltaTime)
    {
        int threadGroups = Mathf.CeilToInt(activeParticleCount / 64.0f);
        int gridThreadGroups = Mathf.CeilToInt(totalGridCells / 64.0f);

        // Reset grid
        simulationShader.SetBuffer(resetGridKernel, "GridCounts", gridCountBuffer);
        simulationShader.SetBuffer(resetGridKernel, "GridOffsets", gridOffsetBuffer);
        simulationShader.Dispatch(resetGridKernel, gridThreadGroups, 1, 1);

        // Assign particles to grid cells
        simulationShader.SetBuffer(gridAssignmentKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(gridAssignmentKernel, "ParticleGrid", gridBuffer);
        simulationShader.SetBuffer(gridAssignmentKernel, "GridCounts", gridCountBuffer);
        simulationShader.Dispatch(gridAssignmentKernel, threadGroups, 1, 1);

        // Calculate grid offsets (prefix sum)
        simulationShader.SetBuffer(gridSortingKernel, "GridCounts", gridCountBuffer);
        simulationShader.SetBuffer(gridSortingKernel, "GridOffsets", gridOffsetBuffer);
        simulationShader.Dispatch(gridSortingKernel, 1, 1, 1);

        // Run hierarchical LOD if enabled
        if (enableHierarchicalLOD)
        {
            RunHierarchicalLOD(deltaTime);
        }
        else
        {
            // Regular force calculation without LOD
            simulationShader.SetBuffer(forceKernel, "Particles", particleBuffer);
            simulationShader.SetBuffer(forceKernel, "ParticleGrid", gridBuffer);
            simulationShader.SetBuffer(forceKernel, "GridCounts", gridCountBuffer);
            simulationShader.SetBuffer(forceKernel, "GridOffsets", gridOffsetBuffer);
            simulationShader.SetBuffer(forceKernel, "InteractionMatrix", interactionBuffer);
            simulationShader.SetBuffer(forceKernel, "ParticleTypes", typesBuffer);
            simulationShader.Dispatch(forceKernel, threadGroups, 1, 1);
        }

        // Integrate particles
        simulationShader.SetBuffer(integrateKernel, "Particles", particleBuffer);
        simulationShader.Dispatch(integrateKernel, threadGroups, 1, 1);

        // Validate particles
        simulationShader.SetBuffer(validateKernel, "Particles", particleBuffer);
        simulationShader.SetInt("MaxParticleCount", maxParticleCount);
        simulationShader.SetInt("ParticleCount", particleCount);
        simulationShader.Dispatch(validateKernel, Mathf.CeilToInt(maxParticleCount / 64.0f), 1, 1);

        // Update args buffer for rendering
        args[1] = (uint)activeParticleCount;
        argsBuffer.SetData(args);
    }

    private void RenderParticles()
    {
        if (particleMesh != null && instancedMaterial != null)
        {
            // Update transform
            Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            // Draw all particles in one batch
            Graphics.DrawMeshInstancedIndirect(
                particleMesh,
                0,
                instancedMaterial,
                new Bounds(transform.position, simulationBounds * 3), // Make bounds larger to prevent culling
                argsBuffer,
                0,
                propertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                true
            );
        }
    }

    private void CleanupResources()
    {
        // Release all compute buffers with explicit disposal
        if (particleBuffer != null) { particleBuffer.Release(); particleBuffer = null; }
        if (typesBuffer != null) { typesBuffer.Release(); typesBuffer = null; }
        if (interactionBuffer != null) { interactionBuffer.Release(); interactionBuffer = null; }
        if (gridBuffer != null) { gridBuffer.Release(); gridBuffer = null; }
        if (gridCountBuffer != null) { gridCountBuffer.Release(); gridCountBuffer = null; }
        if (gridOffsetBuffer != null) { gridOffsetBuffer.Release(); gridOffsetBuffer = null; }
        if (argsBuffer != null) { argsBuffer.Release(); argsBuffer = null; }
        if (colorBuffer != null) { colorBuffer.Release(); colorBuffer = null; }
        if (mergedParticleBuffer != null) { mergedParticleBuffer.Release(); mergedParticleBuffer = null; }
        if (lodCellBuffer != null) { lodCellBuffer.Release(); lodCellBuffer = null; }
        if (lodCellCountBuffer != null) { lodCellCountBuffer.Release(); lodCellCountBuffer = null; }
        if (lodCellStartBuffer != null) { lodCellStartBuffer.Release(); lodCellStartBuffer = null; }

        // Force GC collection to clean up any remaining references
        System.GC.Collect();

        // Force a sync with the GPU to ensure all commands are processed
        GL.Flush();

        // Destroy material
        if (instancedMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(instancedMaterial);
            }
            else
            {
                DestroyImmediate(instancedMaterial);
            }
            instancedMaterial = null;
        }

        initialized = false;
    }

    private void InitializeHierarchicalLOD()
    {
        // Calculate LOD grid size (coarser than regular grid)
        int lodGridX = Mathf.Max(1, gridSizeX / 4);
        int lodGridY = Mathf.Max(1, gridSizeY / 4);
        int lodGridZ = Mathf.Max(1, gridSizeZ / 4);

        // Calculate total LOD cells across all levels
        totalLodCells = 0;
        for (int level = 1; level <= maxLodLevels; level++)
        {
            totalLodCells += lodGridX * lodGridY * lodGridZ;
        }

        // Create buffers
        maxMergedParticles = Mathf.Max(1, particleCount / 10); // Conservative estimate
        mergedParticleBuffer = new ComputeBuffer(maxMergedParticles, Marshal.SizeOf(typeof(MergedParticleData)));
        lodCellBuffer = new ComputeBuffer(maxParticleCount, Marshal.SizeOf(typeof(int)) * 4); // cellIndex, particleIndex, typeIndex, lodLevel
        lodCellCountBuffer = new ComputeBuffer(totalLodCells, sizeof(int));
        lodCellStartBuffer = new ComputeBuffer(totalLodCells, sizeof(int));

        // Initialize merged particles array with defaults
        MergedParticleData[] mergedData = new MergedParticleData[maxMergedParticles];
        for (int i = 0; i < maxMergedParticles; i++)
        {
            mergedData[i] = new MergedParticleData
            {
                position = Vector3.zero,
                velocity = Vector3.zero,
                typeIndex = -1,
                mass = 0,
                radius = 0,
                parentCount = 0,
                lodLevel = 0
            };
        }
        mergedParticleBuffer.SetData(mergedData);

        // Set shader parameters
        simulationShader.SetInt("TotalLODCells", totalLodCells);
        simulationShader.SetInt("MaxMergedParticles", maxMergedParticles);
        simulationShader.SetVector("LODGridSize", new Vector3(lodGridX, lodGridY, lodGridZ));
        simulationShader.SetFloat("LODCellSize", cellSize * 4); // Base LOD cell size
    }

    private void UpdateHierarchicalLODParameters()
    {
        simulationShader.SetBool("EnableHierarchicalLOD", enableHierarchicalLOD);
        simulationShader.SetFloat("LODDistanceThreshold", lodDistanceThreshold);
        simulationShader.SetFloat("LODDistanceMultiplier", lodDistanceMultiplier);
        simulationShader.SetInt("MaxLODLevels", maxLodLevels);

        // Pass camera info for LOD
        if (Camera.main != null)
        {
            simulationShader.SetMatrix("CameraToWorldMatrix", Camera.main.cameraToWorldMatrix);
            simulationShader.SetVector("CameraPosition", Camera.main.transform.position);
        }
    }

    private void RunHierarchicalLOD(float deltaTime)
    {
        // Check that all required buffers are initialized
        if (lodCellCountBuffer == null || lodCellBuffer == null ||
            mergedParticleBuffer == null || lodCellStartBuffer == null)
        {
            Debug.LogWarning("LOD buffers not initialized, initializing now...");
            InitializeHierarchicalLOD();
            UpdateHierarchicalLODParameters();
        }

        int threadGroups = Mathf.CeilToInt(activeParticleCount / 64.0f);
        int lodCellThreadGroups = Mathf.CeilToInt(totalLodCells / 64.0f);

        // Reset LOD cell counts
        simulationShader.SetBuffer(resetGridKernel, "LODCellCounts", lodCellCountBuffer);
        simulationShader.Dispatch(resetGridKernel, lodCellThreadGroups, 1, 1);

        // Reset merged particle counter (we use first element's parentCount field as counter)
        MergedParticleData[] resetData = new MergedParticleData[1];
        resetData[0].parentCount = 0;
        mergedParticleBuffer.SetData(resetData, 0, 0, 1);

        // Identify merge candidates
        simulationShader.SetBuffer(identifyMergeCandidatesKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(identifyMergeCandidatesKernel, "LODCellAssignments", lodCellBuffer);
        simulationShader.SetBuffer(identifyMergeCandidatesKernel, "LODCellCounts", lodCellCountBuffer);
        simulationShader.Dispatch(identifyMergeCandidatesKernel, threadGroups, 1, 1);

        // Calculate LOD cell start indices (prefix sum)
        simulationShader.SetBuffer(gridSortingKernel, "LODCellCounts", lodCellCountBuffer);
        simulationShader.SetBuffer(gridSortingKernel, "LODCellStartIndices", lodCellStartBuffer);
        simulationShader.Dispatch(gridSortingKernel, 1, 1, 1);

        // Create merged particles
        simulationShader.SetBuffer(createMergedParticlesKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(createMergedParticlesKernel, "LODCellAssignments", lodCellBuffer);
        simulationShader.SetBuffer(createMergedParticlesKernel, "LODCellCounts", lodCellCountBuffer);
        simulationShader.SetBuffer(createMergedParticlesKernel, "LODCellStartIndices", lodCellStartBuffer);
        simulationShader.SetBuffer(createMergedParticlesKernel, "MergedParticles", mergedParticleBuffer);
        simulationShader.Dispatch(createMergedParticlesKernel, lodCellThreadGroups, 1, 1);

        // Get number of active merged particles
        MergedParticleData[] countData = new MergedParticleData[1];
        mergedParticleBuffer.GetData(countData, 0, 0, 1);
        activeMergedParticles = countData[0].parentCount;

        // Calculate hierarchical forces
        simulationShader.SetBuffer(calculateHierarchicalForcesKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(calculateHierarchicalForcesKernel, "MergedParticles", mergedParticleBuffer);
        simulationShader.SetBuffer(calculateHierarchicalForcesKernel, "InteractionMatrix", interactionBuffer);
        simulationShader.Dispatch(calculateHierarchicalForcesKernel, threadGroups, 1, 1);
    }

    #endregion

    #region Performance Monitoring

    private void MonitorPerformance()
    {
        frameTimes[frameTimeIndex] = Time.unscaledDeltaTime * 1000f; // ms
        frameTimeIndex = (frameTimeIndex + 1) % frameTimes.Length;

        frameTimeMs = 0;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            frameTimeMs += frameTimes[i];
        }
        frameTimeMs /= frameTimes.Length;

        // Track FPS over longer period for LOD adjustment
        accumulatedTime += Time.unscaledDeltaTime;
        frameCount++;

        if (accumulatedTime >= 1.0f)
        {
            float avgFps = frameCount / accumulatedTime;
            accumulatedTime = 0;
            frameCount = 0;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Update interaction rule value and send to GPU
    /// </summary>
    public void UpdateInteractionRule(int typeA, int typeB, float value)
    {
        // Find existing rule or create new one
        bool foundRule = false;
        for (int i = 0; i < interactionRules.Count; i++)
        {
            var rule = interactionRules[i];
            if (rule.typeIndexA == typeA && rule.typeIndexB == typeB)
            {
                rule.attractionValue = value;
                interactionRules[i] = rule;
                foundRule = true;
                break;
            }
        }

        if (!foundRule)
        {
            interactionRules.Add(new InteractionRule
            {
                typeIndexA = typeA,
                typeIndexB = typeB,
                attractionValue = value
            });
        }

        // Update GPU buffer
        if (interactionBuffer != null && initialized)
        {
            float[] interactions = new float[typeCount * typeCount];
            interactionBuffer.GetData(interactions);

            int index = typeA + typeB * typeCount;
            if (index >= 0 && index < interactions.Length)
            {
                interactions[index] = value;
                interactionBuffer.SetData(interactions);
            }
        }
    }

    /// <summary>
    /// Signal that simulation needs reset (e.g. when particle types change)
    /// </summary>
    public void RequestReset()
    {
        needsReset = true;
    }

    /// <summary>
    /// Update particle count for a specific type
    /// </summary>
    public void UpdateParticleCount(int typeIndex, float newCount)
    {
        if (typeIndex >= 0 && typeIndex < particleTypes.Count)
        {
            particleTypes[typeIndex].spawnAmount = newCount;
            needsReset = true;
        }
    }

    /// <summary>
    /// Gets a copy of current particle data (can be expensive, use sparingly)
    /// </summary>
    public GPUParticleData[] GetParticleData()
    {
        if (!initialized || particleBuffer == null) return null;

        GPUParticleData[] data = new GPUParticleData[activeParticleCount];
        particleBuffer.GetData(particleData);

        System.Array.Copy(particleData, data, activeParticleCount);
        return data;
    }

    #endregion

    #region Helper Structs

    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct TypeData
    {
        public float mass;
        public float radius;
        public Color color;
    }

    #endregion
}