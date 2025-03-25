using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
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

    #endregion

    #region Simulation Parameters

    [Header("Compute Resources")]
    public ComputeShader simulationShader;
    public Mesh particleMesh;
    public Material particleMaterial;
    public Shader particleShader;

    [Header("Simulation Settings")]
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
    [Range(0, 3)] public int lodLevels = 2; // Level of detail for physics interactions
    public bool enableLOD = false;
    public bool dynamicLOD = true; // Adjust LOD based on frame rate

    [Header("Target Performance")]
    [Range(30, 144)] public float targetFPS = 60f;
    [Range(0.1f, 1.0f)] public float lodAdjustSpeed = 0.2f;
    public float currentLODFactor = 1.0f;
    private float[] lodDistanceMultipliers = new float[] { 1.0f, 2.0f, 4.0f, 8.0f };

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

    // Compute buffers
    private ComputeBuffer particleBuffer;
    private ComputeBuffer typesBuffer;
    private ComputeBuffer interactionBuffer;
    private ComputeBuffer gridBuffer;
    private ComputeBuffer gridCountBuffer;
    private ComputeBuffer gridOffsetBuffer;
    private ComputeBuffer argsBuffer;

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
        lodLevels = Mathf.Clamp(lodLevels, 0, 3);
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

        // Adjust LOD if needed
        if (enableLOD && dynamicLOD)
        {
            AdjustLODForPerformance();
        }

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
        if (EditorApplication.isPlaying && initialized)
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
        gridSizeX = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.x / cellSize));
        gridSizeY = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.y / cellSize));
        gridSizeZ = Mathf.Max(1, Mathf.CeilToInt(simulationBounds.z / cellSize));
        totalGridCells = gridSizeX * gridSizeY * gridSizeZ;
        gridCellCount = totalGridCells;

        Debug.Log($"Spatial grid: {gridSizeX} x {gridSizeY} x {gridSizeZ} = {totalGridCells} cells");
    }

    private void InitializeShaders()
    {
        // Find all kernels
        initKernel = simulationShader.FindKernel("InitParticles");
        gridAssignmentKernel = simulationShader.FindKernel("AssignParticlesToGrid");
        gridCountingKernel = simulationShader.FindKernel("CountParticlesPerCell");
        gridSortingKernel = simulationShader.FindKernel("SortParticlesByCell");
        forceKernel = simulationShader.FindKernel("CalculateForces");
        integrateKernel = simulationShader.FindKernel("IntegrateParticles");
        resetGridKernel = simulationShader.FindKernel("ResetGrid");
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

        // Clear existing interaction rules
        interactionRules.Clear();

        // Get visualized matrix from generator
        float[,] matrix = generator.VisualizeMatrix();

        // Create new interaction array
        float[] interactions = new float[typeCount * typeCount];

        // Copy rules from generator's matrix to our interaction rules
        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                float value = matrix[i, j];

                // Only add non-zero rules
                if (Math.Abs(value) > 0.001f)
                {
                    interactionRules.Add(new InteractionRule
                    {
                        typeIndexA = i,
                        typeIndexB = j,
                        attractionValue = value
                    });

                    // Set in the array too
                    int index = i + j * typeCount;
                    interactions[index] = value;
                }
            }
        }

        // Update the GPU buffer
        interactionBuffer.SetData(interactions);

        Debug.Log($"Synced {interactionRules.Count} rules from generator");
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

        // LOD parameters
        simulationShader.SetBool("EnableLOD", enableLOD);
        simulationShader.SetInt("LODLevels", lodLevels);
        simulationShader.SetFloat("LODFactor", currentLODFactor);

        // Pass camera info for LOD
        if (Camera.main != null)
        {
            simulationShader.SetMatrix("CameraToWorldMatrix", Camera.main.cameraToWorldMatrix);
            simulationShader.SetVector("CameraPosition", Camera.main.transform.position);
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

        // Count particles per cell
        simulationShader.SetBuffer(gridCountingKernel, "ParticleGrid", gridBuffer);
        simulationShader.SetBuffer(gridCountingKernel, "GridCounts", gridCountBuffer);
        simulationShader.Dispatch(gridCountingKernel, threadGroups, 1, 1);

        // Calculate grid offsets (prefix sum)
        simulationShader.SetBuffer(gridSortingKernel, "GridCounts", gridCountBuffer);
        simulationShader.SetBuffer(gridSortingKernel, "GridOffsets", gridOffsetBuffer);
        simulationShader.Dispatch(gridSortingKernel, 1, 1, 1);

        // Calculate forces
        simulationShader.SetBuffer(forceKernel, "Particles", particleBuffer);
        simulationShader.SetBuffer(forceKernel, "ParticleGrid", gridBuffer);
        simulationShader.SetBuffer(forceKernel, "GridCounts", gridCountBuffer);
        simulationShader.SetBuffer(forceKernel, "GridOffsets", gridOffsetBuffer);
        simulationShader.SetBuffer(forceKernel, "InteractionMatrix", interactionBuffer);
        simulationShader.SetBuffer(forceKernel, "ParticleTypes", typesBuffer);
        simulationShader.Dispatch(forceKernel, threadGroups, 1, 1);

        // Integrate particles
        simulationShader.SetBuffer(integrateKernel, "Particles", particleBuffer);
        simulationShader.Dispatch(integrateKernel, threadGroups, 1, 1);

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
        // Release all compute buffers
        if (particleBuffer != null) particleBuffer.Release();
        if (typesBuffer != null) typesBuffer.Release();
        if (interactionBuffer != null) interactionBuffer.Release();
        if (gridBuffer != null) gridBuffer.Release();
        if (gridCountBuffer != null) gridCountBuffer.Release();
        if (gridOffsetBuffer != null) gridOffsetBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
        if (colorBuffer != null) colorBuffer.Release();

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
        }

        initialized = false;
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

            // Use this for LOD adjustment
            OnFPSUpdated(avgFps);
        }
    }

    private void OnFPSUpdated(float fps)
    {
        if (!enableLOD || !dynamicLOD) return;

        // Adjust LOD to maintain target framerate
        float fpsRatio = fps / targetFPS;

        if (fpsRatio < 0.9f) // Under target
        {
            // Reduce quality
            currentLODFactor = Mathf.Lerp(currentLODFactor,
                Mathf.Min(currentLODFactor * 1.5f, 8.0f),
                lodAdjustSpeed);
        }
        else if (fpsRatio > 1.1f && currentLODFactor > 1.1f) // Above target with room to improve
        {
            // Increase quality
            currentLODFactor = Mathf.Lerp(currentLODFactor,
                Mathf.Max(currentLODFactor * 0.8f, 1.0f),
                lodAdjustSpeed);
        }
    }

    private void AdjustLODForPerformance()
    {
        // Validate LOD factor stays within bounds
        currentLODFactor = Mathf.Clamp(currentLODFactor, 1.0f, 8.0f);
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