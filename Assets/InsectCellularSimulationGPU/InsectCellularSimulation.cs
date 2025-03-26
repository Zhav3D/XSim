using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// GPU-accelerated cellular simulation for generating insect morphology.
/// Extends the GPU Particle System to model developmental biology principles.
/// </summary>
[RequireComponent(typeof(GPUParticleSimulation))]
public class InsectCellularSimulation : MonoBehaviour
{
    #region Cell Types and Structures

    // Cell types found in insect development
    public enum CellType
    {
        Stem,           // Undifferentiated cells
        Epithelial,     // Surface/skin cells
        Neural,         // Nervous system cells
        Muscle,         // Movement cells
        Tracheal,       // Respiratory system cells
        Fat,            // Energy storage cells
        Cuticle,        // Exoskeleton cells
        Hemolymph,      // Circulatory fluid cells
        Segment,        // Body segment cells
        Appendage       // Limb/wing/antenna cells
    }

    // Developmental stages
    public enum DevelopmentalStage
    {
        Egg,            // Initial development
        Embryo,         // Pattern formation
        Larva,          // Growth phase
        Pupa,           // Metamorphosis (for holometabolous insects)
        Adult           // Final form
    }

    // Morphogen - diffusible signals that establish gradients
    [System.Serializable]
    public class Morphogen
    {
        public string name;
        public float concentration;
        public float diffusionRate;
        public float decayRate;
        public Color visualizationColor;
        public CellType[] targetCellTypes;
        public bool isVisible = true;
    }

    // Gene - controls expression of developmental traits
    [System.Serializable]
    public class Gene
    {
        public string name;
        public float expressionThreshold;
        public Morphogen[] activators;
        public Morphogen[] repressors;
        public CellType[] expressionResults;
        public bool isExpressed = false;
    }

    // Body segment definition
    [System.Serializable]
    public class BodySegment
    {
        public string name;
        public float relativePosition; // 0.0 to 1.0 from anterior to posterior
        public float size;
        public CellType[] allowedCellTypes;
        public Morphogen[] localMorphogens;
        public Gene[] localGenes;
        public bool hasAppendages;
        public int appendagePairs;
    }

    #endregion

    #region Simulation Parameters

    [Header("Development Settings")]
    public float spawnMultiplier = 1.0f;
    public float boundsMultiplier = 10.0f;
    public DevelopmentalStage currentStage = DevelopmentalStage.Egg;
    [Range(0f, 1f)] public float developmentProgress = 0f;
    public float developmentSpeed = 1.0f;

    [Header("Body Plan")]
    public Vector3 bodyAxisPrimary = Vector3.forward;  // Anterior-Posterior axis
    public Vector3 bodyAxisSecondary = Vector3.up;     // Dorsal-Ventral axis
    public Vector3 bodyAxisTertiary = Vector3.right;   // Left-Right axis
    public float bodyLength = 10f;
    public float bodyWidth = 3f;
    public float bodyHeight = 3f;

    [Header("Segmentation")]
    public int segmentCount = 13;  // Standard for many insects: 3 head + 3 thorax + 7 abdomen
    public BodySegment[] segments;

    [Header("Morphogens")]
    public Morphogen[] morphogens;

    [Header("Gene Regulatory Network")]
    public Gene[] genes;
    public float geneExpressionRate = 1.0f;

    [Header("Cell Behaviors")]
    public float divisionRate = 0.05f;
    public float differentiationRate = 0.1f;
    public float migrationRate = 0.2f;
    public float adhesionFactor = 0.8f;

    [Header("Visualization")]
    public bool showMorphogenGradients = true;
    public bool showGeneExpression = true;
    public bool showBodySegments = true;
    public bool labelCellTypes = true;

    #endregion

    #region Runtime Variables

    private GPUParticleSimulation particleSystem;
    private GPUInteractionMatrixGenerator matrixGenerator;

    // Maps from CellType to particle type index
    private Dictionary<CellType, int> cellTypeToParticleType = new Dictionary<CellType, int>();

    // Maps from segment index to cell indices
    private List<int>[] segmentCells;

    // Track morphogen gradients
    private float[,,,] morphogenField;
    private int gradientResolutionX = 32;
    private int gradientResolutionY = 16;
    private int gradientResolutionZ = 32;

    // Track developmental state
    private float developmentalAge = 0f;
    private bool isInitialized = false;
    private bool needsReset = false;

    // Visualization objects
    private Dictionary<CellType, GameObject> cellTypeVisualizers = new Dictionary<CellType, GameObject>();
    private Dictionary<int, GameObject> segmentVisualizers = new Dictionary<int, GameObject>();

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        particleSystem = GetComponent<GPUParticleSimulation>();
        matrixGenerator = GetComponent<GPUInteractionMatrixGenerator>();

        if (particleSystem == null || matrixGenerator == null)
        {
            Debug.LogError("InsectCellularSimulation requires GPUParticleSimulation and GPUInteractionMatrixGenerator components");
            enabled = false;
            return;
        }

        Initialize();
    }

    void Update()
    {
        if (!isInitialized || needsReset)
        {
            Initialize();
            needsReset = false;
            return;
        }

        // Progress development
        developmentalAge += Time.deltaTime * developmentSpeed;
        UpdateDevelopmentalStage();

        // Update simulation parameters based on current stage
        UpdateMorphogenGradients();
        UpdateGeneExpression();

        // Calculate inter-cell interactions based on developmental rules
        UpdateCellInteractions();

        // Handle cell division and differentiation
        if (UnityEngine.Random.value < divisionRate * Time.deltaTime)
        {
            DivideCells();
        }

        // Update visualization
        if (showMorphogenGradients)
        {
            UpdateMorphogenVisualization();
        }

        // Update development progress value (0-1)
        developmentProgress = CalculateDevelopmentProgress();
    }

    void OnDestroy()
    {
        CleanupVisualization();
    }

    void OnDrawGizmos()
    {
        // Draw body axes
        if (!Application.isPlaying)
        {
            Gizmos.color = Color.blue; // Anterior-Posterior
            Gizmos.DrawRay(transform.position, bodyAxisPrimary.normalized * bodyLength * 0.5f);
            Gizmos.DrawRay(transform.position, -bodyAxisPrimary.normalized * bodyLength * 0.5f);

            Gizmos.color = Color.green; // Dorsal-Ventral
            Gizmos.DrawRay(transform.position, bodyAxisSecondary.normalized * bodyHeight * 0.5f);
            Gizmos.DrawRay(transform.position, -bodyAxisSecondary.normalized * bodyHeight * 0.5f);

            Gizmos.color = Color.red; // Left-Right
            Gizmos.DrawRay(transform.position, bodyAxisTertiary.normalized * bodyWidth * 0.5f);
            Gizmos.DrawRay(transform.position, -bodyAxisTertiary.normalized * bodyWidth * 0.5f);

            // Draw segment boundaries if defined
            if (segments != null && showBodySegments)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i] != null)
                    {
                        float position = (segments[i].relativePosition * 2 - 1) * bodyLength * 0.5f;
                        Vector3 segmentPos = transform.position + bodyAxisPrimary.normalized * position;

                        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.7f);
                        Gizmos.DrawSphere(segmentPos, segments[i].size * 0.5f);

                        // Draw appendage indicators
                        if (segments[i].hasAppendages)
                        {
                            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.7f);
                            float appendageSpacing = bodyWidth / (segments[i].appendagePairs + 1);
                            for (int j = 1; j <= segments[i].appendagePairs; j++)
                            {
                                Vector3 leftPos = segmentPos + bodyAxisTertiary.normalized * j * appendageSpacing;
                                Vector3 rightPos = segmentPos - bodyAxisTertiary.normalized * j * appendageSpacing;

                                Gizmos.DrawSphere(leftPos, 0.3f);
                                Gizmos.DrawSphere(rightPos, 0.3f);
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region Initialization

    public void Initialize()
    {
        Debug.Log("Initializing Insect Cellular Simulation");

        // Initialize morphogen field
        InitializeMorphogenField();

        // Create cell types
        SetupCellTypes();

        // Generate initial cells
        GenerateInitialCells();

        // Setup segment tracking
        InitializeSegments();

        // Setup visualization
        SetupVisualization();

        isInitialized = true;
        Debug.Log("Insect Cellular Simulation Initialized");
    }

    private void InitializeMorphogenField()
    {
        // Create a 3D grid for morphogen diffusion
        morphogenField = new float[morphogens.Length, gradientResolutionX, gradientResolutionY, gradientResolutionZ];

        // Set initial morphogen gradients based on body axes
        for (int m = 0; m < morphogens.Length; m++)
        {
            // Example: Setup anterior-posterior gradient
            if (morphogens[m].name.Contains("Anterior") || morphogens[m].name.Contains("AP"))
            {
                for (int x = 0; x < gradientResolutionX; x++)
                {
                    float normalizedPos = (float)x / gradientResolutionX;
                    float concentration = 1.0f - normalizedPos; // High at anterior

                    for (int y = 0; y < gradientResolutionY; y++)
                    {
                        for (int z = 0; z < gradientResolutionZ; z++)
                        {
                            morphogenField[m, x, y, z] = concentration;
                        }
                    }
                }
            }
            // Example: Setup dorsal-ventral gradient
            else if (morphogens[m].name.Contains("Dorsal") || morphogens[m].name.Contains("DV"))
            {
                for (int y = 0; y < gradientResolutionY; y++)
                {
                    float normalizedPos = (float)y / gradientResolutionY;
                    float concentration = normalizedPos; // High at dorsal

                    for (int x = 0; x < gradientResolutionX; x++)
                    {
                        for (int z = 0; z < gradientResolutionZ; z++)
                        {
                            morphogenField[m, x, y, z] = concentration;
                        }
                    }
                }
            }
        }
    }

    private void SetupCellTypes()
    {
        // Clear existing particle types
        particleSystem.particleTypes.Clear();
        cellTypeToParticleType.Clear();

        // Create a particle type for each cell type
        foreach (CellType cellType in Enum.GetValues(typeof(CellType)))
        {
            Color cellColor = GetCellTypeColor(cellType);
            float cellRadius = GetCellTypeRadius(cellType);
            float cellMass = GetCellTypeMass(cellType);

            GPUParticleSimulation.ParticleType particleType = new GPUParticleSimulation.ParticleType
            {
                name = cellType.ToString(),
                color = cellColor,
                mass = cellMass,
                radius = cellRadius,
                // Start with varying amounts depending on cell type
                spawnAmount = GetInitialCellCount(cellType) * spawnMultiplier
            };

            particleSystem.particleTypes.Add(particleType);
            cellTypeToParticleType[cellType] = particleSystem.particleTypes.Count - 1;
        }

        Debug.Log($"Created {particleSystem.particleTypes.Count} cell types for simulation");
    }

    private void GenerateInitialCells()
    {
        // Setup interactions that will guide development
        GenerateDevelopmentalInteractions();

        // Cell generation will be handled by the particle system
        // but we need to use a specific pattern for the initial egg stage
        matrixGenerator.patternType = GPUInteractionMatrixGenerator.PatternType.Lenia;
        matrixGenerator.attractionBias = 0.2f; // Slight bias toward attraction for cohesion
        matrixGenerator.symmetryFactor = 0.7f; // Fairly symmetric interactions
        matrixGenerator.sparsity = 0.1f;       // Few neutral interactions
        matrixGenerator.noiseFactor = 0.05f;   // Little noise for more stable patterns

        // Initial egg is more constrained
        particleSystem.startParticlesAtCenter = true;
        particleSystem.simulationBounds = new Vector3(bodyWidth * 0.5f, bodyHeight * 0.5f, bodyLength * 0.5f) * boundsMultiplier;

        // Generate the matrix with these settings
        matrixGenerator.GenerateMatrix();

        // Request simulation reset
        particleSystem.RequestReset();

        Debug.Log("Generated initial cell configuration");
    }

    private void GenerateDevelopmentalInteractions()
    {
        // Clear existing rules
        particleSystem.interactionRules.Clear();

        foreach (CellType typeA in Enum.GetValues(typeof(CellType)))
        {
            int indexA = cellTypeToParticleType[typeA];

            foreach (CellType typeB in Enum.GetValues(typeof(CellType)))
            {
                int indexB = cellTypeToParticleType[typeB];

                // Skip if same type - self-interactions handled separately
                if (typeA == typeB) continue;

                // Define developmental interaction rules between cell types
                float attractionValue = CalculateInteractionForCellTypes(typeA, typeB);

                GPUParticleSimulation.InteractionRule rule = new GPUParticleSimulation.InteractionRule
                {
                    typeIndexA = indexA,
                    typeIndexB = indexB,
                    attractionValue = attractionValue
                };

                particleSystem.interactionRules.Add(rule);
            }

            // Add self-interaction for this cell type
            GPUParticleSimulation.InteractionRule selfRule = new GPUParticleSimulation.InteractionRule
            {
                typeIndexA = indexA,
                typeIndexB = indexA,
                attractionValue = GetCellTypeSelfAttraction(typeA)
            };

            particleSystem.interactionRules.Add(selfRule);
        }

        Debug.Log($"Generated {particleSystem.interactionRules.Count} developmental interaction rules");
    }

    private void InitializeSegments()
    {
        // Initialize segments if they're not already defined
        if (segments == null || segments.Length != segmentCount)
        {
            segments = new BodySegment[segmentCount];

            // Standard insect segmentation: 3 head, 3 thorax, 7 abdomen
            for (int i = 0; i < segmentCount; i++)
            {
                float relativePos = (float)i / (segmentCount - 1);
                string segmentName;
                bool hasAppendages = false;
                int appendagePairs = 0;

                // Apply naming and appendage rules based on segment position
                if (i < 3)
                {
                    // Head segments
                    segmentName = $"Head_{i + 1}";
                    if (i == 0) { hasAppendages = true; appendagePairs = 1; } // Antennae
                    if (i == 2) { hasAppendages = true; appendagePairs = 1; } // Mouthparts
                }
                else if (i < 6)
                {
                    // Thorax segments
                    segmentName = $"Thorax_{i - 2}";
                    hasAppendages = true;
                    appendagePairs = 1; // Legs
                    if (i == 4 || i == 5) appendagePairs = 2; // Wings on middle and rear thorax
                }
                else
                {
                    // Abdomen segments
                    segmentName = $"Abdomen_{i - 5}";
                    // No appendages on abdomen except possibly terminal segment
                    if (i == segmentCount - 1) { hasAppendages = true; appendagePairs = 1; } // Cerci
                }

                segments[i] = new BodySegment
                {
                    name = segmentName,
                    relativePosition = relativePos,
                    size = (i < 3 || i >= 6) ? 0.8f : 1.2f, // Thorax segments larger
                    allowedCellTypes = GetAllowedCellTypesForSegment(i),
                    hasAppendages = hasAppendages,
                    appendagePairs = appendagePairs
                };
            }
        }

        // Initialize segment cell tracking
        segmentCells = new List<int>[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            segmentCells[i] = new List<int>();
        }
    }

    private void SetupVisualization()
    {
        CleanupVisualization(); // Clear any existing visualization

        // Create visualization objects for each cell type
        foreach (CellType cellType in Enum.GetValues(typeof(CellType)))
        {
            GameObject visualizer = new GameObject(cellType.ToString() + "_Visualizer");
            visualizer.transform.parent = transform;
            visualizer.transform.localPosition = Vector3.zero;

            // Add components for visualization
            // This is just a placeholder - actual visualization will depend on your needs
            cellTypeVisualizers[cellType] = visualizer;
        }

        // Create visualization for each segment
        for (int i = 0; i < segments.Length; i++)
        {
            GameObject segmentObj = new GameObject("Segment_" + segments[i].name);
            segmentObj.transform.parent = transform;

            float position = (segments[i].relativePosition * 2 - 1) * bodyLength * 0.5f;
            segmentObj.transform.localPosition = bodyAxisPrimary.normalized * position;

            // Add visual indicator
            segmentVisualizers[i] = segmentObj;
        }
    }

    #endregion

    #region Simulation Logic

    private void UpdateDevelopmentalStage()
    {
        // Determine current stage based on developmental age
        if (developmentalAge < 10f)
        {
            currentStage = DevelopmentalStage.Egg;
        }
        else if (developmentalAge < 30f)
        {
            currentStage = DevelopmentalStage.Embryo;
        }
        else if (developmentalAge < 60f)
        {
            currentStage = DevelopmentalStage.Larva;
        }
        else if (developmentalAge < 90f)
        {
            currentStage = DevelopmentalStage.Pupa;
        }
        else
        {
            currentStage = DevelopmentalStage.Adult;
        }

        // Update simulation parameters based on developmental stage
        switch (currentStage)
        {
            case DevelopmentalStage.Egg:
                // Tight clustering, high division rate, low mobility
                particleSystem.interactionStrength = 2.0f;
                particleSystem.dampening = 0.9f;
                particleSystem.interactionRadius = 3f;
                divisionRate = 0.1f;
                migrationRate = 0.05f;
                break;

            case DevelopmentalStage.Embryo:
                // Pattern formation, high differentiation
                particleSystem.interactionStrength = 1.5f;
                particleSystem.dampening = 0.92f;
                particleSystem.interactionRadius = 5f;
                divisionRate = 0.08f;
                differentiationRate = 0.15f;
                migrationRate = 0.1f;
                break;

            case DevelopmentalStage.Larva:
                // Growth, moderate differentiation
                particleSystem.interactionStrength = 1.2f;
                particleSystem.dampening = 0.95f;
                particleSystem.interactionRadius = 8f;
                divisionRate = 0.05f;
                differentiationRate = 0.1f;
                migrationRate = 0.15f;
                break;

            case DevelopmentalStage.Pupa:
                // Major reorganization for metamorphosis
                particleSystem.interactionStrength = 1.8f;
                particleSystem.dampening = 0.9f;
                particleSystem.interactionRadius = 12f;
                divisionRate = 0.02f;
                differentiationRate = 0.2f;
                migrationRate = 0.25f;
                break;

            case DevelopmentalStage.Adult:
                // Stabilization, low division, low differentiation
                particleSystem.interactionStrength = 1.0f;
                particleSystem.dampening = 0.98f;
                particleSystem.interactionRadius = 15f;
                divisionRate = 0.01f;
                differentiationRate = 0.05f;
                migrationRate = 0.1f;
                break;
        }

        // Adjust bounds as the insect grows
        float stageFactor = (currentStage == DevelopmentalStage.Egg) ? 0.3f :
                          (currentStage == DevelopmentalStage.Embryo) ? 0.5f :
                          (currentStage == DevelopmentalStage.Larva) ? 0.7f :
                          (currentStage == DevelopmentalStage.Pupa) ? 0.9f : 1.0f;

        particleSystem.simulationBounds = new Vector3(
            bodyWidth * stageFactor,
            bodyHeight * stageFactor,
            bodyLength * stageFactor
        ) * boundsMultiplier;
    }

    private void UpdateMorphogenGradients()
    {
        // Diffuse morphogens through the field
        for (int m = 0; m < morphogens.Length; m++)
        {
            Morphogen morphogen = morphogens[m];

            // Skip if inactive
            if (morphogen.diffusionRate <= 0) continue;

            // Simple diffusion algorithm
            float[,,] newField = new float[gradientResolutionX, gradientResolutionY, gradientResolutionZ];

            for (int x = 0; x < gradientResolutionX; x++)
            {
                for (int y = 0; y < gradientResolutionY; y++)
                {
                    for (int z = 0; z < gradientResolutionZ; z++)
                    {
                        // Current concentration
                        float currentConc = morphogenField[m, x, y, z];

                        // Calculate diffusion by averaging with neighbors
                        float diffusion = 0f;
                        int neighborCount = 0;

                        // Check 6 immediate neighbors
                        if (x > 0) { diffusion += morphogenField[m, x - 1, y, z]; neighborCount++; }
                        if (x < gradientResolutionX - 1) { diffusion += morphogenField[m, x + 1, y, z]; neighborCount++; }
                        if (y > 0) { diffusion += morphogenField[m, x, y - 1, z]; neighborCount++; }
                        if (y < gradientResolutionY - 1) { diffusion += morphogenField[m, x, y + 1, z]; neighborCount++; }
                        if (z > 0) { diffusion += morphogenField[m, x, y, z - 1]; neighborCount++; }
                        if (z < gradientResolutionZ - 1) { diffusion += morphogenField[m, x, y, z + 1]; neighborCount++; }

                        if (neighborCount > 0)
                        {
                            diffusion /= neighborCount;
                            float diffusionAmount = (diffusion - currentConc) * morphogen.diffusionRate * Time.deltaTime;
                            float decayAmount = currentConc * morphogen.decayRate * Time.deltaTime;

                            newField[x, y, z] = currentConc + diffusionAmount - decayAmount;
                        }
                        else
                        {
                            newField[x, y, z] = currentConc;
                        }
                    }
                }
            }

            // Update the morphogen field
            for (int x = 0; x < gradientResolutionX; x++)
            {
                for (int y = 0; y < gradientResolutionY; y++)
                {
                    for (int z = 0; z < gradientResolutionZ; z++)
                    {
                        morphogenField[m, x, y, z] = newField[x, y, z];
                    }
                }
            }
        }

        // Add segment-specific morphogens
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].localMorphogens != null)
            {
                foreach (Morphogen localMorphogen in segments[i].localMorphogens)
                {
                    // Find this morphogen in the main array
                    for (int m = 0; m < morphogens.Length; m++)
                    {
                        if (morphogens[m].name == localMorphogen.name)
                        {
                            // Add concentration at segment position
                            int centerX = Mathf.FloorToInt(segments[i].relativePosition * gradientResolutionX);
                            int radius = Mathf.CeilToInt(segments[i].size * gradientResolutionX * 0.1f);

                            for (int x = centerX - radius; x <= centerX + radius; x++)
                            {
                                if (x >= 0 && x < gradientResolutionX)
                                {
                                    for (int y = 0; y < gradientResolutionY; y++)
                                    {
                                        for (int z = 0; z < gradientResolutionZ; z++)
                                        {
                                            float distance = Mathf.Abs(x - centerX) / (float)radius;
                                            float falloff = 1.0f - Mathf.Clamp01(distance);
                                            morphogenField[m, x, y, z] += localMorphogen.concentration * falloff * Time.deltaTime;
                                        }
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }
    }

    private void UpdateGeneExpression()
    {
        // Update gene expression based on morphogen concentrations
        foreach (Gene gene in genes)
        {
            bool shouldExpress = true;

            // Check activators
            if (gene.activators != null && gene.activators.Length > 0)
            {
                foreach (Morphogen activator in gene.activators)
                {
                    // Find this morphogen in the main array
                    for (int m = 0; m < morphogens.Length; m++)
                    {
                        if (morphogens[m].name == activator.name)
                        {
                            // Check average concentration
                            float avgConcentration = CalculateAverageMorphogenConcentration(m);
                            if (avgConcentration < gene.expressionThreshold)
                            {
                                shouldExpress = false;
                                break;
                            }
                        }
                    }

                    if (!shouldExpress) break;
                }
            }

            // Check repressors
            if (shouldExpress && gene.repressors != null && gene.repressors.Length > 0)
            {
                foreach (Morphogen repressor in gene.repressors)
                {
                    // Find this morphogen in the main array
                    for (int m = 0; m < morphogens.Length; m++)
                    {
                        if (morphogens[m].name == repressor.name)
                        {
                            // Check average concentration
                            float avgConcentration = CalculateAverageMorphogenConcentration(m);
                            if (avgConcentration >= gene.expressionThreshold)
                            {
                                shouldExpress = false;
                                break;
                            }
                        }
                    }

                    if (!shouldExpress) break;
                }
            }

            // Update gene expression state
            gene.isExpressed = shouldExpress;
        }
    }

    private float CalculateAverageMorphogenConcentration(int morphogenIndex)
    {
        float sum = 0f;
        int count = gradientResolutionX * gradientResolutionY * gradientResolutionZ;

        for (int x = 0; x < gradientResolutionX; x++)
        {
            for (int y = 0; y < gradientResolutionY; y++)
            {
                for (int z = 0; z < gradientResolutionZ; z++)
                {
                    sum += morphogenField[morphogenIndex, x, y, z];
                }
            }
        }

        return sum / count;
    }

    private void UpdateCellInteractions()
    {
        // Get particle data from GPU - note this is expensive, should be optimized
        GPUParticleSimulation.GPUParticleData[] particleData = particleSystem.GetParticleData();
        if (particleData == null) return;

        // Update interaction rules based on current gene expression
        List<GPUParticleSimulation.InteractionRule> newRules = new List<GPUParticleSimulation.InteractionRule>();

        foreach (Gene gene in genes)
        {
            if (!gene.isExpressed) continue;

            // Apply gene effects on cell interactions
            if (gene.expressionResults != null)
            {
                foreach (CellType cellType in gene.expressionResults)
                {
                    if (!cellTypeToParticleType.ContainsKey(cellType)) continue;

                    int typeIndex = cellTypeToParticleType[cellType];

                    // Modify interaction rules for this cell type based on gene expression
                    // This is a simplified example - you would have more complex logic here
                    foreach (CellType otherType in Enum.GetValues(typeof(CellType)))
                    {
                        if (!cellTypeToParticleType.ContainsKey(otherType)) continue;

                        int otherIndex = cellTypeToParticleType[otherType];

                        // Calculate interaction based on developmental rules
                        float baseAttraction = CalculateInteractionForCellTypes(cellType, otherType);

                        // Modify based on gene expression
                        float geneFactor = 1.0f; // default

                        // Example: If gene promotes cell adhesion
                        if (gene.name.Contains("Adhesion"))
                        {
                            geneFactor = 1.5f;
                        }
                        // Example: If gene promotes cell migration
                        else if (gene.name.Contains("Migration"))
                        {
                            geneFactor = 0.5f;
                        }

                        // Create rule
                        GPUParticleSimulation.InteractionRule rule = new GPUParticleSimulation.InteractionRule
                        {
                            typeIndexA = typeIndex,
                            typeIndexB = otherIndex,
                            attractionValue = baseAttraction * geneFactor
                        };

                        newRules.Add(rule);
                    }
                }
            }
        }

        // Only update if we have new rules to apply
        if (newRules.Count > 0)
        {
            particleSystem.interactionRules.Clear();
            particleSystem.interactionRules.AddRange(newRules);
            particleSystem.SyncMatrixFromGenerator(matrixGenerator);
        }

        // Assign cells to segments based on position
        for (int i = 0; i < segmentCount; i++)
        {
            segmentCells[i].Clear();
        }

        for (int i = 0; i < particleData.Length; i++)
        {
            if (particleData[i].typeIndex < 0) continue; // Skip inactive particles

            Vector3 position = particleData[i].position;

            // Project position onto AP axis to determine segment
            float projectionAP = Vector3.Dot(position, bodyAxisPrimary.normalized) / bodyLength + 0.5f;
            int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(projectionAP * segmentCount), 0, segmentCount - 1);

            segmentCells[segmentIndex].Add(i);
        }
    }

    private void DivideCells()
    {
        // This is simplified - in a real implementation, you would use a compute shader
        // to handle cell division on the GPU directly

        // For demonstration, we'll just modify spawn amounts of different cell types
        // based on developmental stage

        if (currentStage == DevelopmentalStage.Egg || currentStage == DevelopmentalStage.Embryo)
        {
            // Increase stem cells
            int stemTypeIndex = cellTypeToParticleType[CellType.Stem];
            float currentAmount = particleSystem.particleTypes[stemTypeIndex].spawnAmount;
            particleSystem.UpdateParticleCount(stemTypeIndex, currentAmount * (1f + divisionRate));
        }
        else if (currentStage == DevelopmentalStage.Larva)
        {
            // Increase epithelial, muscle, and tracheal cells
            int epithelialTypeIndex = cellTypeToParticleType[CellType.Epithelial];
            int muscleTypeIndex = cellTypeToParticleType[CellType.Muscle];
            int trachealTypeIndex = cellTypeToParticleType[CellType.Tracheal];

            particleSystem.UpdateParticleCount(epithelialTypeIndex,
                particleSystem.particleTypes[epithelialTypeIndex].spawnAmount * (1f + divisionRate * 0.5f));

            particleSystem.UpdateParticleCount(muscleTypeIndex,
                particleSystem.particleTypes[muscleTypeIndex].spawnAmount * (1f + divisionRate * 0.3f));

            particleSystem.UpdateParticleCount(trachealTypeIndex,
                particleSystem.particleTypes[trachealTypeIndex].spawnAmount * (1f + divisionRate * 0.2f));
        }
        else if (currentStage == DevelopmentalStage.Pupa)
        {
            // Decrease larval cells, increase adult cells
            int muscleTypeIndex = cellTypeToParticleType[CellType.Muscle];
            int appendageTypeIndex = cellTypeToParticleType[CellType.Appendage];

            particleSystem.UpdateParticleCount(muscleTypeIndex,
                particleSystem.particleTypes[muscleTypeIndex].spawnAmount * (1f + divisionRate * 0.5f));

            particleSystem.UpdateParticleCount(appendageTypeIndex,
                particleSystem.particleTypes[appendageTypeIndex].spawnAmount * (1f + divisionRate));
        }
    }

    private float CalculateDevelopmentProgress()
    {
        // Map developmental age to 0-1 progress
        switch (currentStage)
        {
            case DevelopmentalStage.Egg:
                return developmentalAge / 10f;
            case DevelopmentalStage.Embryo:
                return (developmentalAge - 10f) / 20f + 0.2f;
            case DevelopmentalStage.Larva:
                return (developmentalAge - 30f) / 30f + 0.4f;
            case DevelopmentalStage.Pupa:
                return (developmentalAge - 60f) / 30f + 0.7f;
            case DevelopmentalStage.Adult:
                return 1.0f;
            default:
                return 0f;
        }
    }

    #endregion

    #region Visualization

    private void UpdateMorphogenVisualization()
    {
        // In a real implementation, you would visualize morphogen gradients using
        // a volume renderer or particle effects

        // For now, we'll just update object colors
        for (int m = 0; m < morphogens.Length; m++)
        {
            if (!morphogens[m].isVisible) continue;

            // Get average morphogen level
            float avgLevel = CalculateAverageMorphogenConcentration(m);

            // Color any target cell types
            if (morphogens[m].targetCellTypes != null)
            {
                foreach (CellType cellType in morphogens[m].targetCellTypes)
                {
                    if (cellTypeVisualizers.ContainsKey(cellType))
                    {
                        // Color intensity based on concentration
                        GameObject visualizer = cellTypeVisualizers[cellType];
                        if (visualizer.GetComponent<Renderer>() != null)
                        {
                            Color color = morphogens[m].visualizationColor;
                            color.a = avgLevel;
                            visualizer.GetComponent<Renderer>().material.color = color;
                        }
                    }
                }
            }
        }
    }

    private void CleanupVisualization()
    {
        // Destroy any existing visualization objects
        foreach (var visualizer in cellTypeVisualizers.Values)
        {
            if (visualizer != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(visualizer);
                }
                else
                {
                    DestroyImmediate(visualizer);
                }
            }
        }

        foreach (var visualizer in segmentVisualizers.Values)
        {
            if (visualizer != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(visualizer);
                }
                else
                {
                    DestroyImmediate(visualizer);
                }
            }
        }

        cellTypeVisualizers.Clear();
        segmentVisualizers.Clear();
    }

    #endregion

    #region Helper Methods

    private Color GetCellTypeColor(CellType cellType)
    {
        // Return appropriate color for each cell type
        switch (cellType)
        {
            case CellType.Stem:
                return new Color(0.9f, 0.9f, 0.9f);
            case CellType.Epithelial:
                return new Color(0.8f, 0.5f, 0.5f);
            case CellType.Neural:
                return new Color(0.5f, 0.5f, 0.9f);
            case CellType.Muscle:
                return new Color(0.9f, 0.3f, 0.3f);
            case CellType.Tracheal:
                return new Color(0.3f, 0.7f, 0.9f);
            case CellType.Fat:
                return new Color(0.9f, 0.9f, 0.6f);
            case CellType.Cuticle:
                return new Color(0.7f, 0.7f, 0.5f);
            case CellType.Hemolymph:
                return new Color(0.7f, 0.9f, 0.7f);
            case CellType.Segment:
                return new Color(0.6f, 0.8f, 0.9f);
            case CellType.Appendage:
                return new Color(0.5f, 0.9f, 0.5f);
            default:
                return Color.white;
        }
    }

    private float GetCellTypeRadius(CellType cellType)
    {
        // Return appropriate radius for each cell type
        switch (cellType)
        {
            case CellType.Stem:
                return 0.3f;
            case CellType.Epithelial:
                return 0.25f;
            case CellType.Neural:
                return 0.2f;
            case CellType.Muscle:
                return 0.4f;
            case CellType.Tracheal:
                return 0.3f;
            case CellType.Fat:
                return 0.45f;
            case CellType.Cuticle:
                return 0.35f;
            case CellType.Hemolymph:
                return 0.2f;
            case CellType.Segment:
                return 0.4f;
            case CellType.Appendage:
                return 0.35f;
            default:
                return 0.3f;
        }
    }

    private float GetCellTypeMass(CellType cellType)
    {
        // Return appropriate mass for each cell type
        switch (cellType)
        {
            case CellType.Stem:
                return 1.0f;
            case CellType.Epithelial:
                return 0.8f;
            case CellType.Neural:
                return 0.7f;
            case CellType.Muscle:
                return 1.5f;
            case CellType.Tracheal:
                return 0.9f;
            case CellType.Fat:
                return 1.8f;
            case CellType.Cuticle:
                return 1.2f;
            case CellType.Hemolymph:
                return 0.5f;
            case CellType.Segment:
                return 1.3f;
            case CellType.Appendage:
                return 1.1f;
            default:
                return 1.0f;
        }
    }

    private int GetInitialCellCount(CellType cellType)
    {
        // Return initial cell count for each type at the start of simulation
        switch (cellType)
        {
            case CellType.Stem:
                return 100;
            case CellType.Epithelial:
                return 50;
            case CellType.Neural:
                return 20;
            case CellType.Muscle:
                return 30;
            case CellType.Tracheal:
                return 10;
            case CellType.Fat:
                return 15;
            case CellType.Cuticle:
                return 25;
            case CellType.Hemolymph:
                return 20;
            case CellType.Segment:
                return 40;
            case CellType.Appendage:
                return 10;
            default:
                return 20;
        }
    }

    private float CalculateInteractionForCellTypes(CellType typeA, CellType typeB)
    {
        // Define interaction coefficients between cell types
        // This would be based on developmental biology principles

        // Default to slight repulsion
        float baseInteraction = -0.1f;

        // Cell-specific interactions based on biological rules

        // Rule: Epithelial cells adhere to each other
        if (typeA == CellType.Epithelial && typeB == CellType.Epithelial)
        {
            return 0.8f;
        }

        // Rule: Muscle cells attract each other
        if (typeA == CellType.Muscle && typeB == CellType.Muscle)
        {
            return 0.7f;
        }

        // Rule: Neural cells form networks
        if (typeA == CellType.Neural && typeB == CellType.Neural)
        {
            return 0.6f;
        }

        // Rule: Appendage cells attract appendage and segment cells
        if (typeA == CellType.Appendage &&
            (typeB == CellType.Appendage || typeB == CellType.Segment))
        {
            return 0.9f;
        }

        // Rule: Stem cells differentiate into other types (slight attraction)
        if (typeA == CellType.Stem && typeB != CellType.Stem)
        {
            return 0.3f;
        }

        // Rule: Cuticle forms outer layer (slight attraction to epithelial)
        if (typeA == CellType.Cuticle && typeB == CellType.Epithelial)
        {
            return 0.5f;
        }

        // Rule: Tracheal cells form branching networks
        if (typeA == CellType.Tracheal && typeB == CellType.Tracheal)
        {
            return 0.4f;
        }

        // Rule: Hemolymph flows through other tissues (mild repulsion)
        if (typeA == CellType.Hemolymph && typeB != CellType.Hemolymph)
        {
            return -0.3f;
        }

        // Rule: Fat cells cluster together
        if (typeA == CellType.Fat && typeB == CellType.Fat)
        {
            return 0.7f;
        }

        // Rule: Segment cells organize body plan
        if (typeA == CellType.Segment && typeB == CellType.Segment)
        {
            return 0.6f;
        }

        return baseInteraction;
    }

    private float GetCellTypeSelfAttraction(CellType cellType)
    {
        // Define how strongly each cell type adheres to its own kind
        switch (cellType)
        {
            case CellType.Stem:
                return 0.4f; // Moderate adhesion
            case CellType.Epithelial:
                return 0.8f; // Strong adhesion for layers
            case CellType.Neural:
                return 0.6f; // Network forming
            case CellType.Muscle:
                return 0.7f; // Strong adhesion for fiber formation
            case CellType.Tracheal:
                return 0.5f; // Moderate for networks
            case CellType.Fat:
                return 0.9f; // Very strong adhesion for storage
            case CellType.Cuticle:
                return 0.8f; // Strong for outer layer
            case CellType.Hemolymph:
                return -0.1f; // Slight repulsion for flow
            case CellType.Segment:
                return 0.6f; // Moderate for organization
            case CellType.Appendage:
                return 0.7f; // Strong for structure
            default:
                return 0.3f;
        }
    }

    private CellType[] GetAllowedCellTypesForSegment(int segmentIndex)
    {
        // Define which cell types are allowed in each segment
        if (segmentIndex < 3)
        {
            // Head segments - has neural, epithelial, etc.
            return new CellType[] {
                CellType.Stem,
                CellType.Epithelial,
                CellType.Neural,
                CellType.Tracheal,
                CellType.Cuticle,
                CellType.Hemolymph,
                CellType.Segment,
                CellType.Appendage
            };
        }
        else if (segmentIndex < 6)
        {
            // Thorax segments - has muscle, appendages, etc.
            return new CellType[] {
                CellType.Stem,
                CellType.Epithelial,
                CellType.Neural,
                CellType.Muscle,
                CellType.Tracheal,
                CellType.Fat,
                CellType.Cuticle,
                CellType.Hemolymph,
                CellType.Segment,
                CellType.Appendage
            };
        }
        else
        {
            // Abdomen segments - has digestive, fat storage, etc.
            return new CellType[] {
                CellType.Stem,
                CellType.Epithelial,
                CellType.Neural,
                CellType.Muscle,
                CellType.Tracheal,
                CellType.Fat,
                CellType.Cuticle,
                CellType.Hemolymph,
                CellType.Segment
            };
        }
    }

    #endregion

    // Public API for external control or sequencing of the development process
    #region Public API

    public void SetDevelopmentalStage(DevelopmentalStage stage)
    {
        // Force a specific stage
        currentStage = stage;

        // Set developmental age to match
        switch (stage)
        {
            case DevelopmentalStage.Egg:
                developmentalAge = 5f;
                break;
            case DevelopmentalStage.Embryo:
                developmentalAge = 20f;
                break;
            case DevelopmentalStage.Larva:
                developmentalAge = 45f;
                break;
            case DevelopmentalStage.Pupa:
                developmentalAge = 75f;
                break;
            case DevelopmentalStage.Adult:
                developmentalAge = 100f;
                break;
        }

        // Update simulation parameters
        UpdateDevelopmentalStage();
    }

    public void ActivateMorphogen(string morphogenName, float concentration)
    {
        // Find and activate a specific morphogen
        for (int m = 0; m < morphogens.Length; m++)
        {
            if (morphogens[m].name == morphogenName)
            {
                morphogens[m].concentration = concentration;

                // Add to the entire field
                for (int x = 0; x < gradientResolutionX; x++)
                {
                    for (int y = 0; y < gradientResolutionY; y++)
                    {
                        for (int z = 0; z < gradientResolutionZ; z++)
                        {
                            morphogenField[m, x, y, z] += concentration;
                        }
                    }
                }

                break;
            }
        }
    }

    public void ExpressGene(string geneName, bool express)
    {
        // Force a gene to be expressed or suppressed
        foreach (Gene gene in genes)
        {
            if (gene.name == geneName)
            {
                gene.isExpressed = express;
                break;
            }
        }
    }

    public void Reset()
    {
        // Reset the entire simulation
        developmentalAge = 0f;
        currentStage = DevelopmentalStage.Egg;
        needsReset = true;
    }

    #endregion
}