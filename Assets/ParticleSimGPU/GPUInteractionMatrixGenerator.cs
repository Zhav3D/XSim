using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(GPUParticleSimulation))]
public class GPUInteractionMatrixGenerator : MonoBehaviour
{
    // Pattern type to generate
    public enum PatternType
    {
        Random,
        Clusters,
        Chains,
        PredatorPrey,
        Crystalline,
        Flocking,
        Lenia,
        Segregation
    }

    [Header("Generation Settings")]
    public PatternType patternType = PatternType.PredatorPrey;
    public bool generateOnAwake = true;
    public bool generateParticleTypes = true;
    public bool applyRecommendedSettings = true;

    [Header("Particle Scaling")]
    [Range(0.1f, 1000f)] public float particleSpawnMultiplier = 1.0f;
    [Range(0.1f, 100f)] public float particleRadiusMultiplier = 1.0f;

    [Header("Matrix Configuration")]
    [Range(-1f, 1f)] public float attractionBias = 0f;  // Bias toward attraction (1) or repulsion (-1)
    [Range(0f, 1f)] public float symmetryFactor = 0.1f; // How symmetric should the matrix be
    [Range(0f, 1f)] public float sparsity = 0.2f;       // Proportion of neutral (0) interactions
    [Range(0f, 1f)] public float noiseFactor = 0.1f;    // Add some noise to deterministic patterns

    private GPUParticleSimulation simulation;

    void Awake()
    {
        simulation = GetComponent<GPUParticleSimulation>();

        if (generateOnAwake)
        {
            GenerateMatrix();
        }
    }

    // Replace the GenerateMatrix method in GPUInteractionMatrixGenerator.cs
    public void GenerateMatrix()
    {
        if (simulation == null)
        {
            simulation = GetComponent<GPUParticleSimulation>();
        }

        // Generate particle types if requested
        if (generateParticleTypes)
        {
            GenerateParticleTypes();
        }

        // Apply recommended simulation settings if requested
        if (applyRecommendedSettings)
        {
            ApplyRecommendedSettings(simulation, patternType);
        }

        // Clear existing interaction rules
        simulation.interactionRules.Clear();

        // Generate new rules based on pattern type
        switch (patternType)
        {
            case PatternType.Random:
                GenerateRandomMatrix();
                break;
            case PatternType.Clusters:
                GenerateClusterMatrix();
                break;
            case PatternType.Chains:
                GenerateChainMatrix();
                break;
            case PatternType.PredatorPrey:
                GeneratePredatorPreyMatrix();
                break;
            case PatternType.Crystalline:
                GenerateCrystallineMatrix();
                break;
            case PatternType.Flocking:
                GenerateFlockingMatrix();
                break;
            case PatternType.Lenia:
                GenerateLeniaMatrix();
                break;
            case PatternType.Segregation:
                GenerateSegregationMatrix();
                break;
        }

        // Reset the simulation to apply changes
        if (Application.isPlaying)
        {
            // Debug the rules that were created
            Debug.Log($"Matrix Generator created {simulation.interactionRules.Count} rules");

            // Show some examples of the rules
            int debugCount = Mathf.Min(10, simulation.interactionRules.Count);
            for (int i = 0; i < debugCount; i++)
            {
                var rule = simulation.interactionRules[i];
                Debug.Log($"Rule {i}: Type {rule.typeIndexA} → Type {rule.typeIndexB} = {rule.attractionValue}");
            }

            // Explicitly sync the matrix to the GPU
            simulation.SyncMatrixFromGenerator(this);

            // Request a reset of the simulation
            simulation.RequestReset();
        }
    }

    // Visualize the matrix in the inspector (editor only)
    public float[,] VisualizeMatrix()
    {
        int typeCount = simulation.particleTypes.Count;
        float[,] matrix = new float[typeCount, typeCount];

        // Initialize with zeros
        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                matrix[i, j] = 0f;
            }
        }

        // Fill from interaction rules
        foreach (var rule in simulation.interactionRules)
        {
            matrix[rule.typeIndexA, rule.typeIndexB] = rule.attractionValue;
        }

        return matrix;
    }

    private void AddInteractionRule(int typeA, int typeB, float value)
    {
        // Add a rule with the specified values
        var rule = new GPUParticleSimulation.InteractionRule
        {
            typeIndexA = typeA,
            typeIndexB = typeB,
            attractionValue = value
        };

        simulation.interactionRules.Add(rule);
    }

    private void GenerateRandomMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                // Skip self-interactions if needed
                if (i == j) continue;

                // Determine if this interaction should be neutral
                if (Random.value < sparsity)
                {
                    // Skip this interaction (will be 0 by default)
                    continue;
                }

                // Generate interaction with bias
                float threshold = (1f + attractionBias) / 2f; // Convert -1,1 to 0,1 range
                float value = Random.value < threshold ? 1f : -1f;

                // Add interaction rule
                AddInteractionRule(i, j, value);

                // Apply symmetry factor
                if (Random.value < symmetryFactor)
                {
                    // Make the reverse interaction the same
                    AddInteractionRule(j, i, value);
                }
                else if (Random.value < symmetryFactor / 2f)
                {
                    // Sometimes make it antisymmetric (opposite)
                    AddInteractionRule(j, i, -value);
                }
            }
        }
    }

    private void GenerateClusterMatrix()
    {
        int typeCount = simulation.particleTypes.Count;
        int groups = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(typeCount)));

        // Assign particles to groups
        int[] particleGroups = new int[typeCount];
        for (int i = 0; i < typeCount; i++)
        {
            particleGroups[i] = Random.Range(0, groups);
        }

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                // Skip self-interactions
                if (i == j) continue;

                // Determine if this interaction should be neutral
                if (Random.value < sparsity)
                {
                    continue;
                }

                // Same group attracts, different group repels
                float value = (particleGroups[i] == particleGroups[j]) ? 1f : -1f;

                // Add some noise
                if (Random.value < noiseFactor)
                {
                    value *= -1f;
                }

                AddInteractionRule(i, j, value);
            }
        }
    }

    private void GenerateChainMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // Create a random ordering of particles
        List<int> order = new List<int>();
        for (int i = 0; i < typeCount; i++)
        {
            order.Add(i);
        }

        // Shuffle the order
        for (int i = 0; i < order.Count; i++)
        {
            int j = Random.Range(i, order.Count);
            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                // Skip self-interactions
                if (i == j) continue;

                // Determine if this interaction should be neutral
                if (Random.value < sparsity)
                {
                    continue;
                }

                // Find positions in the chain
                int posI = order.IndexOf(i);
                int posJ = order.IndexOf(j);

                // Neighbors in the chain attract
                float value = (Mathf.Abs(posI - posJ) == 1) ? 1f : -1f;

                // Add some noise
                if (Random.value < noiseFactor)
                {
                    value *= -1f;
                }

                AddInteractionRule(i, j, value);
            }
        }
    }

    private void GeneratePredatorPreyMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // Create a circular predator-prey relationship
        for (int i = 0; i < typeCount; i++)
        {
            int prey = (i + 1) % typeCount;
            int predator = (i - 1 + typeCount) % typeCount;

            // Predator attracts to prey
            AddInteractionRule(i, prey, 1f);

            // Prey repels from predator
            AddInteractionRule(i, predator, -1f);

            // Neutral or mild interactions with others
            for (int j = 0; j < typeCount; j++)
            {
                if (j != prey && j != predator && j != i)
                {
                    // Most interactions are neutral, some weak attraction/repulsion
                    float r = Random.value;
                    if (r < 0.7f)
                    {
                        // Leave as neutral (skip)
                    }
                    else if (r < 0.85f)
                    {
                        AddInteractionRule(i, j, 0.5f);
                    }
                    else
                    {
                        AddInteractionRule(i, j, -0.5f);
                    }
                }
            }
        }
    }

    private void GenerateCrystallineMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // For crystalline structure, we want alternating attractions and repulsions
        // based on "distance" in type space

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                if (i == j) continue;

                // Calculate "distance" between types (in a circular arrangement)
                int dist = Mathf.Min(Mathf.Abs(i - j), typeCount - Mathf.Abs(i - j));

                // Even distances attract, odd distances repel
                float value = (dist % 2 == 0) ? 1f : -1f;

                // Random neutral interactions
                if (Random.value < sparsity)
                {
                    continue;
                }

                AddInteractionRule(i, j, value);
            }
        }
    }

    private void GenerateFlockingMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // For flocking, particles of the same type should align (weak attraction)
        // Different types should generally ignore each other with some exceptions

        // First, make all particles weakly attract their own type
        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                if (i == j) continue;

                if (i % 3 == j % 3) // Same "family"
                {
                    AddInteractionRule(i, j, 0.8f);
                }
                // Different families mostly neutral (skip)
            }
        }

        // Add some "leaders" that others follow
        int leaderCount = Mathf.Max(1, Mathf.FloorToInt(typeCount / 5));
        for (int k = 0; k < leaderCount; k++)
        {
            int leader = Random.Range(0, typeCount);

            for (int i = 0; i < typeCount; i++)
            {
                if (i != leader && Random.value < 0.7f)
                {
                    AddInteractionRule(i, leader, 1f); // Follow the leader
                }
            }
        }

        // Add some "avoiders" that others avoid
        int avoiderCount = Mathf.Max(1, Mathf.FloorToInt(typeCount / 5));
        for (int k = 0; k < avoiderCount; k++)
        {
            int avoider = Random.Range(0, typeCount);

            for (int i = 0; i < typeCount; i++)
            {
                if (i != avoider && Random.value < 0.7f)
                {
                    AddInteractionRule(i, avoider, -1f); // Avoid this type
                }
            }
        }
    }

    private void GenerateLeniaMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // In Lenia, cells are influenced by their neighbors based on a kernel
        // We'll adapt this by creating "neighborhoods" of particle types

        // First create a circular distance matrix
        int[,] distance = new int[typeCount, typeCount];

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                // Calculate circular distance
                distance[i, j] = Mathf.Min(Mathf.Abs(i - j), typeCount - Mathf.Abs(i - j));
            }
        }

        // Now set interactions based on distance
        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                if (i == j) continue;

                int dist = distance[i, j];

                // Create a pattern where close types attract, medium distances repel,
                // and far distances are neutral
                if (dist <= typeCount / 6)
                {
                    AddInteractionRule(i, j, 1f); // Close types attract
                }
                else if (dist <= typeCount / 3)
                {
                    AddInteractionRule(i, j, -1f); // Medium distances repel
                }
                else if (Random.value > 0.8f)
                {
                    // Far distances are mostly neutral with some noise
                    AddInteractionRule(i, j, Random.value < 0.5f ? 0.5f : -0.5f);
                }
            }
        }

        // Add some asymmetry for more interesting dynamics
        for (int i = 0; i < typeCount; i++)
        {
            for (int j = i + 1; j < typeCount; j++)
            {
                if (Random.value < 0.3f)
                {
                    // Find if there's an existing rule
                    GPUParticleSimulation.InteractionRule ruleIJ = null;
                    GPUParticleSimulation.InteractionRule ruleJI = null;

                    foreach (var rule in simulation.interactionRules)
                    {
                        if (rule.typeIndexA == i && rule.typeIndexB == j)
                        {
                            ruleIJ = rule;
                        }
                        if (rule.typeIndexA == j && rule.typeIndexB == i)
                        {
                            ruleJI = rule;
                        }
                    }

                    // 30% chance to flip one direction
                    if (Random.value < 0.5f && ruleIJ != null)
                    {
                        ruleIJ.attractionValue *= -1f;
                    }
                    else if (ruleJI != null)
                    {
                        ruleJI.attractionValue *= -1f;
                    }
                }
            }
        }
    }

    private void GenerateSegregationMatrix()
    {
        int typeCount = simulation.particleTypes.Count;

        // Create groups that self-attract and repel others
        int numGroups = Mathf.Min(Mathf.Max(2, Mathf.FloorToInt(typeCount / 3)), 5);
        int[] groups = new int[typeCount];

        for (int i = 0; i < typeCount; i++)
        {
            groups[i] = Random.Range(0, numGroups);
        }

        for (int i = 0; i < typeCount; i++)
        {
            for (int j = 0; j < typeCount; j++)
            {
                if (i == j) continue;

                if (groups[i] == groups[j])
                {
                    // Same group - strongly attract
                    AddInteractionRule(i, j, 1f);
                }
                else
                {
                    // Different group - strongly repel
                    AddInteractionRule(i, j, -1f);
                }
            }
        }

        // Add a few "bridge" particles that attract multiple groups
        int numBridges = Mathf.Max(1, Mathf.FloorToInt(typeCount / 10));
        for (int b = 0; b < numBridges; b++)
        {
            int bridge = Random.Range(0, typeCount);
            int attractsGroup = Random.Range(0, numGroups);

            for (int j = 0; j < typeCount; j++)
            {
                if (j == bridge) continue;

                if (groups[j] == attractsGroup)
                {
                    // Find and modify or add new rules
                    bool foundBridgeToJ = false;
                    bool foundJToBridge = false;

                    foreach (var rule in simulation.interactionRules)
                    {
                        if (rule.typeIndexA == bridge && rule.typeIndexB == j)
                        {
                            rule.attractionValue = 1f;
                            foundBridgeToJ = true;
                        }
                        if (rule.typeIndexA == j && rule.typeIndexB == bridge)
                        {
                            rule.attractionValue = 1f;
                            foundJToBridge = true;
                        }
                    }

                    if (!foundBridgeToJ)
                    {
                        AddInteractionRule(bridge, j, 1f);
                    }
                    if (!foundJToBridge)
                    {
                        AddInteractionRule(j, bridge, 1f);
                    }
                }
            }
        }
    }

    // Add editor buttons to regenerate the matrix
    void OnValidate()
    {
        // Enforce valid ranges
        attractionBias = Mathf.Clamp(attractionBias, -1f, 1f);
        symmetryFactor = Mathf.Clamp01(symmetryFactor);
        sparsity = Mathf.Clamp01(sparsity);
        noiseFactor = Mathf.Clamp01(noiseFactor);

        // Ensure particle spawn multiplier stays positive
        particleSpawnMultiplier = Mathf.Max(0.1f, particleSpawnMultiplier);

        // Ensure particle radius multiplier stays positive
        particleRadiusMultiplier = Mathf.Max(0.2f, particleRadiusMultiplier);
    }

    // Generate appropriate particle types based on the pattern
    private void GenerateParticleTypes()
    {
        // Clear existing particle types
        simulation.particleTypes.Clear();

        // Number of particle types to generate (varies by pattern)
        int typeCount = 0;

        switch (patternType)
        {
            case PatternType.Random:
                typeCount = 5;
                CreateRandomParticleTypes(typeCount);
                break;

            case PatternType.Clusters:
                typeCount = 6;
                CreateClusterParticleTypes(typeCount);
                break;

            case PatternType.Chains:
                typeCount = 6;
                CreateChainParticleTypes(typeCount);
                break;

            case PatternType.PredatorPrey:
                typeCount = 5;
                CreatePredatorPreyParticleTypes(typeCount);
                break;

            case PatternType.Crystalline:
                typeCount = 8;
                CreateCrystallineParticleTypes(typeCount);
                break;

            case PatternType.Flocking:
                typeCount = 7;
                CreateFlockingParticleTypes(typeCount);
                break;

            case PatternType.Lenia:
                typeCount = 10;
                CreateLeniaParticleTypes(typeCount);
                break;

            case PatternType.Segregation:
                typeCount = 6;
                CreateSegregationParticleTypes(typeCount);
                break;
        }
    }

    // Helper method to add a particle type
    private void AddParticleType(string name, Color color, float mass, float radius, float spawnAmount)
    {
        var type = new GPUParticleSimulation.ParticleType
        {
            name = name,
            color = color,
            mass = mass,
            // Apply global radius multiplier
            radius = radius * particleRadiusMultiplier,
            // Apply global spawn multiplier
            spawnAmount = Mathf.Round(spawnAmount * particleSpawnMultiplier)
        };

        simulation.particleTypes.Add(type);
    }

    // Helper to generate a random color with good saturation and brightness
    private Color GenerateRandomColor(float saturation = 0.7f, float brightness = 0.9f)
    {
        return Color.HSVToRGB(Random.value, saturation, brightness);
    }

    #region Particle Type Generation Methods

    // Create random particle types
    private void CreateRandomParticleTypes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            string name = "Type" + i;
            Color color = GenerateRandomColor();
            float mass = Random.Range(0.8f, 1.2f);
            float radius = Random.Range(0.4f, 0.6f);
            float spawnAmount = Random.Range(40f, 60f);

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create cluster-optimized particle types
    private void CreateClusterParticleTypes(int count)
    {
        // Define group count (usually 2-3 groups work well)
        int groups = Mathf.Min(3, Mathf.FloorToInt(count / 2));

        // Generate colors for each group with similar hues within groups
        float[] groupHues = new float[groups];
        for (int g = 0; g < groups; g++)
        {
            groupHues[g] = Random.value;
        }

        for (int i = 0; i < count; i++)
        {
            int group = i % groups;

            // Create similar colors within group with slight variations
            float hue = groupHues[group] + Random.Range(-0.05f, 0.05f);
            hue = (hue + 1) % 1; // Ensure hue stays in 0-1 range

            string name = "Cluster" + group + "_" + i;
            Color color = Color.HSVToRGB(hue, 0.7f + Random.Range(-0.1f, 0.1f), 0.9f);
            float mass = 1.0f;
            float radius = 0.5f;
            float spawnAmount = 50f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create chain-optimized particle types
    private void CreateChainParticleTypes(int count)
    {
        // Create a gradient of colors for the chain
        for (int i = 0; i < count; i++)
        {
            float hue = (float)i / count; // Spread hues across the spectrum

            string name = "Link" + i;
            Color color = Color.HSVToRGB(hue, 0.8f, 0.9f);

            // Make each adjacent pair in the chain have similar mass
            float massVariation = Mathf.Sin((float)i / count * Mathf.PI * 2) * 0.3f;
            float mass = 1.0f + massVariation;

            float radius = 0.5f;
            float spawnAmount = 40f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create predator-prey optimized particle types
    private void CreatePredatorPreyParticleTypes(int count)
    {
        // Use a color wheel for predator-prey cycle
        // Predators are slightly larger than their prey
        for (int i = 0; i < count; i++)
        {
            float hue = (float)i / count;
            string name = "Species" + i;
            Color color = Color.HSVToRGB(hue, 0.9f, 0.9f);

            // Predators are slightly larger but slower (heavier)
            float mass = 1.0f + (i % 2) * 0.5f; // Alternating masses
            float radius = 0.4f + (i % 2) * 0.2f; // Alternating sizes

            // Fewer predators, more prey
            float spawnAmount = (i % 2 == 0) ? 60f : 40f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create crystalline optimized particle types
    private void CreateCrystallineParticleTypes(int count)
    {
        // For crystalline patterns, we want distinct particle types
        // with clear visual differentiation

        // Use primary and secondary colors with high contrast
        Color[] baseColors = new Color[] {
            Color.red, Color.green, Color.blue,
            Color.cyan, Color.magenta, Color.yellow,
            new Color(1f, 0.5f, 0f), // Orange
            new Color(0.5f, 0f, 1f)  // Purple
        };

        for (int i = 0; i < count; i++)
        {
            int colorIndex = i % baseColors.Length;
            Color baseColor = baseColors[colorIndex];

            // Add slight variations
            Color color = new Color(
                baseColor.r * Random.Range(0.9f, 1.0f),
                baseColor.g * Random.Range(0.9f, 1.0f),
                baseColor.b * Random.Range(0.9f, 1.0f)
            );

            string name = "Crystal" + i;
            // Uniform mass and size for crystalline structures
            float mass = 1.0f;
            float radius = 0.5f;
            float spawnAmount = 40f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create flocking optimized particle types
    private void CreateFlockingParticleTypes(int count)
    {
        // For flocking, group particles into 2-3 "species"
        int flockTypes = Mathf.Min(3, Mathf.CeilToInt(count / 3f));

        // Generate a base color for each flock
        Color[] flockColors = new Color[flockTypes];
        for (int i = 0; i < flockTypes; i++)
        {
            flockColors[i] = GenerateRandomColor(0.8f, 0.9f);
        }

        for (int i = 0; i < count; i++)
        {
            int flockIndex = i % flockTypes;
            Color baseColor = flockColors[flockIndex];

            // Slight color variation within a flock
            Color color = new Color(
                Mathf.Clamp01(baseColor.r + Random.Range(-0.1f, 0.1f)),
                Mathf.Clamp01(baseColor.g + Random.Range(-0.1f, 0.1f)),
                Mathf.Clamp01(baseColor.b + Random.Range(-0.1f, 0.1f))
            );

            string name = "Flock" + flockIndex + "_" + i;

            // One heavier "leader" particle per flock
            float mass = (i % flockTypes == 0) ? 2.0f : 1.0f;
            float radius = (i % flockTypes == 0) ? 0.8f : 0.5f;
            float spawnAmount = (i % flockTypes == 0) ? 10f : 60f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create Lenia optimized particle types
    private void CreateLeniaParticleTypes(int count)
    {
        // For Lenia patterns, a gradient of related colors works well
        // with particles of varying sizes
        float baseHue = Random.value; // Starting hue

        for (int i = 0; i < count; i++)
        {
            // Create a gradient around the color wheel
            float hue = (baseHue + (float)i / count * 0.6f) % 1.0f;

            string name = "Lenia" + i;
            Color color = Color.HSVToRGB(hue, 0.7f, 0.9f);

            // Lenia works well with varying particle sizes and masses
            float massVariation = Mathf.PerlinNoise(i * 0.5f, 0f) * 0.6f;
            float mass = 0.7f + massVariation;

            float radiusVariation = Mathf.PerlinNoise(i * 0.5f, 1f) * 0.3f;
            float radius = 0.4f + radiusVariation;

            // More small particles, fewer large ones
            float spawnAmount = 60f - (radius - 0.4f) * 50f;

            AddParticleType(name, color, mass, radius, spawnAmount);
        }
    }

    // Create segregation optimized particle types
    private void CreateSegregationParticleTypes(int count)
    {
        // For segregation, create 2-3 distinct groups with very different colors
        int groups = Mathf.Min(3, Mathf.CeilToInt(count / 2f));

        // Define highly contrasting colors for groups
        Color[] groupColors = new Color[groups];
        float hueStep = 1.0f / groups;

        for (int g = 0; g < groups; g++)
        {
            groupColors[g] = Color.HSVToRGB(g * hueStep, 1.0f, 1.0f);
        }

        // Add one "bridge" particle type per simulation
        bool addedBridge = false;

        for (int i = 0; i < count; i++)
        {
            if (i == count - 1 && !addedBridge)
            {
                // Last type is a "bridge" particle
                string name = "Bridge";
                Color color = Color.white; // Neutral color
                float mass = 1.5f;
                float radius = 0.7f;
                float spawnAmount = 5f; // Few bridge particles

                AddParticleType(name, color, mass, radius, spawnAmount);
                addedBridge = true;
            }
            else
            {
                // Regular group particles
                int group = i % groups;
                Color baseColor = groupColors[group];

                // Slight variation within group
                Color color = new Color(
                    Mathf.Clamp01(baseColor.r * Random.Range(0.9f, 1.0f)),
                    Mathf.Clamp01(baseColor.g * Random.Range(0.9f, 1.0f)),
                    Mathf.Clamp01(baseColor.b * Random.Range(0.9f, 1.0f))
                );

                string name = "Group" + group + "_" + i;
                float mass = 1.0f;
                float radius = 0.5f;
                float spawnAmount = 40f;

                AddParticleType(name, color, mass, radius, spawnAmount);
            }
        }
    }

    #endregion

    #region Recommended Settings

    // Apply recommended settings for different pattern types
    private void ApplyRecommendedSettings(GPUParticleSimulation simulation, PatternType patternType)
    {
        switch (patternType)
        {
            case PatternType.Random:
                simulation.simulationSpeed = 1.0f;
                simulation.interactionStrength = 1.0f;
                simulation.dampening = 0.95f;
                simulation.minDistance = 0.5f;
                simulation.bounceForce = 0.8f;
                simulation.maxForce = 100f;
                simulation.maxVelocity = 20f;
                simulation.interactionRadius = 10f;
                simulation.cellSize = 2.5f;
                break;

            case PatternType.Clusters:
                simulation.simulationSpeed = 1.2f;
                simulation.interactionStrength = 1.5f;
                simulation.dampening = 0.9f;
                simulation.minDistance = 0.6f;
                simulation.bounceForce = 0.8f;
                simulation.maxForce = 120f;
                simulation.maxVelocity = 15f;
                simulation.interactionRadius = 12f;
                simulation.cellSize = 3.0f;
                break;

            case PatternType.Chains:
                simulation.simulationSpeed = 1.1f;
                simulation.interactionStrength = 2.0f;
                simulation.dampening = 0.9f;
                simulation.minDistance = 0.4f;
                simulation.bounceForce = 0.8f;
                simulation.maxForce = 150f;
                simulation.maxVelocity = 18f;
                simulation.interactionRadius = 15f;
                simulation.cellSize = 3.75f;
                break;

            case PatternType.PredatorPrey:
                simulation.simulationSpeed = 1.5f;
                simulation.interactionStrength = 2.0f;
                simulation.dampening = 0.92f;
                simulation.minDistance = 0.5f;
                simulation.bounceForce = 0.9f;
                simulation.maxForce = 120f;
                simulation.maxVelocity = 25f;
                simulation.interactionRadius = 10f;
                simulation.cellSize = 2.5f;
                break;

            case PatternType.Crystalline:
                simulation.simulationSpeed = 0.8f;
                simulation.interactionStrength = 3.0f;
                simulation.dampening = 0.85f;
                simulation.minDistance = 0.6f;
                simulation.bounceForce = 0.5f;
                simulation.maxForce = 200f;
                simulation.maxVelocity = 15f;
                simulation.interactionRadius = 8f;
                simulation.cellSize = 2.0f;
                break;

            case PatternType.Flocking:
                simulation.simulationSpeed = 1.8f;
                simulation.interactionStrength = 1.2f;
                simulation.dampening = 0.98f; // Less damping for smoother movement
                simulation.minDistance = 0.4f;
                simulation.bounceForce = 0.9f;
                simulation.maxForce = 80f;
                simulation.maxVelocity = 20f;
                simulation.interactionRadius = 12f;
                simulation.cellSize = 3.0f;
                break;

            case PatternType.Lenia:
                simulation.simulationSpeed = 0.8f;
                simulation.interactionStrength = 1.5f;
                simulation.dampening = 0.9f;
                simulation.minDistance = 0.4f;
                simulation.bounceForce = 0.7f;
                simulation.maxForce = 100f;
                simulation.maxVelocity = 12f;
                simulation.interactionRadius = 15f;
                simulation.cellSize = 3.75f;
                break;

            case PatternType.Segregation:
                simulation.simulationSpeed = 1.4f;
                simulation.interactionStrength = 2.5f;
                simulation.dampening = 0.85f;
                simulation.minDistance = 0.6f;
                simulation.bounceForce = 0.7f;
                simulation.maxForce = 150f;
                simulation.maxVelocity = 18f;
                simulation.interactionRadius = 10f;
                simulation.cellSize = 2.5f;
                break;
        }
    }

    #endregion
}

#if UNITY_EDITOR
[CustomEditor(typeof(GPUInteractionMatrixGenerator))]
public class GPUInteractionMatrixGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GPUInteractionMatrixGenerator generator = (GPUInteractionMatrixGenerator)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate New Matrix"))
        {
            generator.GenerateMatrix();
            EditorUtility.SetDirty(generator.gameObject);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate All Matrix Types"))
        {
            foreach (GPUInteractionMatrixGenerator.PatternType patternType in
                     System.Enum.GetValues(typeof(GPUInteractionMatrixGenerator.PatternType)))
            {
                generator.patternType = patternType;
                generator.GenerateMatrix();
                Debug.Log("Generated matrix type: " + patternType.ToString());
                EditorUtility.SetDirty(generator.gameObject);
            }
        }

        // Add description of the current pattern type
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            GetPatternDescription(generator.patternType),
            MessageType.Info
        );

        // Show recommended particle count
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("GPU Simulation Scaling:", EditorStyles.boldLabel);

        int baseParticleCount = 0;

        switch (generator.patternType)
        {
            case GPUInteractionMatrixGenerator.PatternType.Random:
                baseParticleCount = 50000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Clusters:
                baseParticleCount = 100000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Chains:
                baseParticleCount = 75000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.PredatorPrey:
                baseParticleCount = 250000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Crystalline:
                baseParticleCount = 150000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Flocking:
                baseParticleCount = 200000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Lenia:
                baseParticleCount = 300000;
                break;
            case GPUInteractionMatrixGenerator.PatternType.Segregation:
                baseParticleCount = 120000;
                break;
        }

        int totalParticles = Mathf.RoundToInt(baseParticleCount * generator.particleSpawnMultiplier);

        EditorGUILayout.LabelField($"Estimated particle count: ~{totalParticles:N0}");
        EditorGUILayout.HelpBox(
            "For best performance, start with lower particle counts and gradually increase. The performance depends on your GPU capabilities.",
            MessageType.Info
        );
    }

    private string GetPatternDescription(GPUInteractionMatrixGenerator.PatternType patternType)
    {
        switch (patternType)
        {
            case GPUInteractionMatrixGenerator.PatternType.Random:
                return "Random interactions with configurable symmetry and attraction bias. " +
                       "A good starting point but often produces chaotic results unless fine-tuned.";

            case GPUInteractionMatrixGenerator.PatternType.Clusters:
                return "Particles are grouped where members of the same group attract each other " +
                       "while repelling other groups. Creates stable separated clusters.";

            case GPUInteractionMatrixGenerator.PatternType.Chains:
                return "Creates chain-like structures by making certain pairs of particles " +
                       "attract each other in a specific sequence, while repelling others.";

            case GPUInteractionMatrixGenerator.PatternType.PredatorPrey:
                return "Implements a circular food chain where each particle type is attracted " +
                       "to its 'prey' and repelled by its 'predator'. Creates dynamic chase patterns.";

            case GPUInteractionMatrixGenerator.PatternType.Crystalline:
                return "Creates regular lattice-like arrangements through alternating attraction " +
                       "and repulsion based on 'distance' in type space.";

            case GPUInteractionMatrixGenerator.PatternType.Flocking:
                return "Models bird flocking behaviors with particles of the same family attracting " +
                       "each other, plus designated 'leaders' that others follow.";

            case GPUInteractionMatrixGenerator.PatternType.Lenia:
                return "Inspired by the Lenia cellular automaton, creates complex living system-like " +
                       "behaviors with local attraction and medium-range repulsion.";

            case GPUInteractionMatrixGenerator.PatternType.Segregation:
                return "Based on Schelling's segregation model, creates strong group identity with " +
                       "optional 'bridge' particles that can connect different groups.";

            default:
                return "Unknown pattern type.";
        }
    }
}
#endif