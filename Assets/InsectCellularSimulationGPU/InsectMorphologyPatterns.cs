using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Specific developmental pattern rules for creating different insect morphologies.
/// Extends the cellular simulation with biologically-inspired patterning mechanisms.
/// </summary>
[RequireComponent(typeof(InsectCellularSimulation))]
public class InsectMorphologyPatterns : MonoBehaviour
{
    public enum InsectOrder
    {
        Diptera,        // Flies, mosquitoes (2 wings)
        Hymenoptera,    // Bees, wasps, ants (4 wings)
        Lepidoptera,    // Butterflies, moths (4 wings)
        Coleoptera,     // Beetles (hardened forewings)
        Orthoptera,     // Grasshoppers, crickets (jumping legs)
        Hemiptera,      // True bugs (piercing mouthparts)
        Odonata,        // Dragonflies, damselflies (long body)
        Custom          // Custom configuration
    }
    [Header("Body Plan Patterns")]
    public InsectOrder selectedOrder = InsectOrder.Diptera;

    [Header("Pattern Parameters")]
    [Range(0f, 1f)] public float anteriorDominance = 0.7f;
    [Range(0f, 1f)] public float segmentationStrength = 0.8f;
    [Range(0f, 1f)] public float appendageFormationRate = 0.5f;
    [Range(0f, 1f)] public float bodySymmetryFactor = 1.0f;
    [Range(0f, 1f)] public float specializationRate = 0.6f;

    [Header("Specialized Structures")]
    public bool developWings = true;
    public bool developAntennae = true;
    public bool developCompoundEyes = true;
    public bool developExoskeleton = true;
    public bool developSpecializedLegs = false;
    public bool developExtendedAbdomen = false;

    [Header("Evo-Devo Parameters")]
    [Range(0f, 1f)] public float homeoticTransformationRate = 0.0f;
    [Range(-1f, 1f)] public float heterochronyFactor = 0.0f;
    [Range(0f, 1f)] public float allometricGrowthFactor = 0.5f;
    public bool enableDevelopmentalConstraints = true;

    // Reference to the main simulation components
    private InsectCellularSimulation insectSimulation;
    private GPUParticleSimulation particleSimulation;

    // Developmental modules for pattern formation
    private class DevelopmentalModule
    {
        public string name;
        public Action<float> activationFunction;
        public float[] spatialDomain; // Min and max positions along body axis (0-1)
        public float activationLevel;
        public string[] targetGenes;

        public DevelopmentalModule(string name, Action<float> activationFunction,
                                  float[] spatialDomain, string[] targetGenes)
        {
            this.name = name;
            this.activationFunction = activationFunction;
            this.spatialDomain = spatialDomain;
            this.targetGenes = targetGenes;
            this.activationLevel = 0f;
        }

        public void Update(float developmentProgress)
        {
            activationFunction(developmentProgress);
        }
    }

    private List<DevelopmentalModule> developmentalModules = new List<DevelopmentalModule>();

    void Start()
    {
        insectSimulation = GetComponent<InsectCellularSimulation>();
        particleSimulation = GetComponent<GPUParticleSimulation>();

        if (insectSimulation == null)
        {
            Debug.LogError("InsectMorphologyPatterns requires InsectCellularSimulation component");
            enabled = false;
            return;
        }

        SetupDevelopmentalModules();
    }

    void Update()
    {
        if (insectSimulation == null) return;

        // Update developmental modules based on current progress
        foreach (var module in developmentalModules)
        {
            module.Update(insectSimulation.developmentProgress);
        }

        // Apply any transformations based on evo-devo parameters
        if (homeoticTransformationRate > 0)
        {
            ApplyHomeoticTransformations();
        }

        if (Mathf.Abs(heterochronyFactor) > 0.1f)
        {
            ApplyHeterochronicShifts();
        }

        if (allometricGrowthFactor > 0)
        {
            ApplyAllometricGrowth();
        }
    }

    /// <summary>
    /// Apply the selected insect order pattern to the simulation.
    /// </summary>
    public void ApplyInsectOrderPattern()
    {
        switch (selectedOrder)
        {
            case InsectOrder.Diptera:
                ConfigureDipteraPattern();
                break;
            case InsectOrder.Hymenoptera:
                ConfigureHymenopteraPattern();
                break;
            case InsectOrder.Lepidoptera:
                ConfigureLepidopteraPattern();
                break;
            case InsectOrder.Coleoptera:
                ConfigureColeopteraPattern();
                break;
            case InsectOrder.Orthoptera:
                ConfigureOrthopteraPattern();
                break;
            case InsectOrder.Hemiptera:
                ConfigureHemipteraPattern();
                break;
            case InsectOrder.Odonata:
                ConfigureOdonataPattern();
                break;
            case InsectOrder.Custom:
                // Use current configuration
                break;
        }

        // Reset the simulation to apply new pattern
        insectSimulation.Reset();
    }

    #region Insect Order Patterns

    private void ConfigureDipteraPattern()
    {
        // Flies (2 wings, complex eyes, shortened body)
        insectSimulation.bodyLength = 8f;
        insectSimulation.bodyWidth = 2.5f;
        insectSimulation.bodyHeight = 2.5f;
        insectSimulation.segmentCount = 13; // 3 head, 3 thorax, 7 abdomen

        anteriorDominance = 0.8f;
        segmentationStrength = 0.7f;
        appendageFormationRate = 0.6f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.7f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = false;
        developExtendedAbdomen = false;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 13)
        {
            // Enlarged head with large compound eyes
            insectSimulation.segments[0].size = 1.2f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Medium prothorax
            insectSimulation.segments[3].size = 0.9f;
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            // Large mesothorax with wings
            insectSimulation.segments[4].size = 1.3f;
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            // Smaller metathorax
            insectSimulation.segments[5].size = 0.9f;
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair
            insectSimulation.segments[5].appendagePairs = 1;

            // Shortened abdomen
            for (int i = 6; i < 13; i++)
            {
                insectSimulation.segments[i].size = 0.7f - (i - 6) * 0.05f;
                insectSimulation.segments[i].hasAppendages = false;
            }
        }
    }

    private void ConfigureHymenopteraPattern()
    {
        // Bees, Wasps, Ants (4 wings, slim waist, specialized limbs)
        insectSimulation.bodyLength = 12f;
        insectSimulation.bodyWidth = 2.5f;
        insectSimulation.bodyHeight = 2.5f;
        insectSimulation.segmentCount = 14; // 3 head, 3 thorax, 8 abdomen

        anteriorDominance = 0.7f;
        segmentationStrength = 0.9f;
        appendageFormationRate = 0.7f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.8f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = true;
        developExtendedAbdomen = false;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 14)
        {
            // Medium head with compound eyes
            insectSimulation.segments[0].size = 1.0f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Thorax segments with wings
            insectSimulation.segments[3].size = 1.0f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.1f; // Mesothorax
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + first wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.0f; // Metathorax
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair + second wing pair
            insectSimulation.segments[5].appendagePairs = 2;

            // Narrow waist (petiole)
            insectSimulation.segments[6].size = 0.5f;
            insectSimulation.segments[6].hasAppendages = false;

            // Wider abdomen
            for (int i = 7; i < 14; i++)
            {
                float size = (i == 13) ? 0.7f : 0.9f;
                insectSimulation.segments[i].size = size;
                insectSimulation.segments[i].hasAppendages = (i == 13); // Stinger
                insectSimulation.segments[i].appendagePairs = 1;
            }
        }
    }

    private void ConfigureLepidopteraPattern()
    {
        // Butterflies, Moths (4 large wings, slender body)
        insectSimulation.bodyLength = 14f;
        insectSimulation.bodyWidth = 3f;
        insectSimulation.bodyHeight = 2f;
        insectSimulation.segmentCount = 14; // 3 head, 3 thorax, 8 abdomen

        anteriorDominance = 0.6f;
        segmentationStrength = 0.7f;
        appendageFormationRate = 0.9f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.6f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = false;
        developExtendedAbdomen = false;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 14)
        {
            // Small head
            insectSimulation.segments[0].size = 0.8f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Thorax segments with large wings
            insectSimulation.segments[3].size = 0.9f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.3f; // Mesothorax
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + first wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.2f; // Metathorax
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair + second wing pair
            insectSimulation.segments[5].appendagePairs = 2;

            // Slender abdomen
            for (int i = 6; i < 14; i++)
            {
                insectSimulation.segments[i].size = 0.8f - (i - 6) * 0.05f;
                insectSimulation.segments[i].hasAppendages = false;
            }
        }
    }

    private void ConfigureColeopteraPattern()
    {
        // Beetles (hardened forewings, robust body)
        insectSimulation.bodyLength = 12f;
        insectSimulation.bodyWidth = 4f;
        insectSimulation.bodyHeight = 3f;
        insectSimulation.segmentCount = 14; // 3 head, 3 thorax, 8 abdomen

        anteriorDominance = 0.6f;
        segmentationStrength = 0.9f;
        appendageFormationRate = 0.7f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.8f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = false;
        developExtendedAbdomen = false;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 14)
        {
            // Medium head
            insectSimulation.segments[0].size = 1.0f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Thorax segments
            insectSimulation.segments[3].size = 1.1f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.4f; // Mesothorax (elytra)
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + elytra (hardened forewings)
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.2f; // Metathorax
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair + hindwings
            insectSimulation.segments[5].appendagePairs = 2;

            // Robust abdomen covered by elytra
            for (int i = 6; i < 14; i++)
            {
                insectSimulation.segments[i].size = 1.1f - (i - 6) * 0.05f;
                insectSimulation.segments[i].hasAppendages = false;
            }
        }
    }

    private void ConfigureOrthopteraPattern()
    {
        // Grasshoppers, Crickets (large hind legs, straight wings)
        insectSimulation.bodyLength = 16f;
        insectSimulation.bodyWidth = 3f;
        insectSimulation.bodyHeight = 4f;
        insectSimulation.segmentCount = 15; // 3 head, 3 thorax, 9 abdomen

        anteriorDominance = 0.6f;
        segmentationStrength = 0.8f;
        appendageFormationRate = 0.8f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.7f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = true;
        developExtendedAbdomen = true;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 15)
        {
            // Medium head oriented downward
            insectSimulation.segments[0].size = 1.0f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Thorax segments
            insectSimulation.segments[3].size = 1.1f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.2f; // Mesothorax
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.5f; // Metathorax with jumping legs
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair (jumping) + wing pair
            insectSimulation.segments[5].appendagePairs = 2;

            // Extended abdomen
            for (int i = 6; i < 15; i++)
            {
                insectSimulation.segments[i].size = 1.0f - (i - 6) * 0.05f;
                insectSimulation.segments[i].hasAppendages = (i == 14); // Ovipositor
                insectSimulation.segments[i].appendagePairs = 1;
            }
        }
    }

    private void ConfigureHemipteraPattern()
    {
        // True bugs (piercing mouthparts, half-hardened forewings)
        insectSimulation.bodyLength = 10f;
        insectSimulation.bodyWidth = 3.5f;
        insectSimulation.bodyHeight = 2f;
        insectSimulation.segmentCount = 14; // 3 head, 3 thorax, 8 abdomen

        anteriorDominance = 0.7f;
        segmentationStrength = 0.8f;
        appendageFormationRate = 0.6f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.7f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = false;
        developExtendedAbdomen = false;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 14)
        {
            // Elongated head with piercing mouthparts
            insectSimulation.segments[0].size = 1.1f;
            insectSimulation.segments[0].hasAppendages = true; // Antennae
            insectSimulation.segments[0].appendagePairs = 1;

            insectSimulation.segments[2].size = 1.0f;
            insectSimulation.segments[2].hasAppendages = true; // Piercing mouthparts
            insectSimulation.segments[2].appendagePairs = 1;

            // Thorax segments
            insectSimulation.segments[3].size = 1.0f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.2f; // Mesothorax
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + first wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.1f; // Metathorax
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair + second wing pair
            insectSimulation.segments[5].appendagePairs = 2;

            // Broad, flattened abdomen
            for (int i = 6; i < 14; i++)
            {
                insectSimulation.segments[i].size = 1.0f - (i - 6) * 0.05f;
                insectSimulation.segments[i].hasAppendages = false;
            }
        }
    }

    private void ConfigureOdonataPattern()
    {
        // Dragonflies, Damselflies (long body, 4 large wings, huge eyes)
        insectSimulation.bodyLength = 20f;
        insectSimulation.bodyWidth = 2f;
        insectSimulation.bodyHeight = 2f;
        insectSimulation.segmentCount = 17; // 3 head, 3 thorax, 11 abdomen

        anteriorDominance = 0.5f;
        segmentationStrength = 0.9f;
        appendageFormationRate = 0.8f;
        bodySymmetryFactor = 1.0f;
        specializationRate = 0.7f;

        developWings = true;
        developAntennae = true;
        developCompoundEyes = true;
        developExoskeleton = true;
        developSpecializedLegs = false;
        developExtendedAbdomen = true;

        // Configure segments
        if (insectSimulation.segments != null && insectSimulation.segments.Length >= 17)
        {
            // Large head with huge eyes
            insectSimulation.segments[0].size = 1.3f;
            insectSimulation.segments[0].hasAppendages = true; // Small antennae
            insectSimulation.segments[0].appendagePairs = 1;

            // Thorax segments with large wings
            insectSimulation.segments[3].size = 1.1f; // Prothorax
            insectSimulation.segments[3].hasAppendages = true; // First leg pair
            insectSimulation.segments[3].appendagePairs = 1;

            insectSimulation.segments[4].size = 1.4f; // Mesothorax
            insectSimulation.segments[4].hasAppendages = true; // Second leg pair + first wing pair
            insectSimulation.segments[4].appendagePairs = 2;

            insectSimulation.segments[5].size = 1.3f; // Metathorax
            insectSimulation.segments[5].hasAppendages = true; // Third leg pair + second wing pair
            insectSimulation.segments[5].appendagePairs = 2;

            // Very long, slender abdomen
            for (int i = 6; i < 17; i++)
            {
                float size = (i < 15) ? 0.9f : 0.7f;
                insectSimulation.segments[i].size = size;
                insectSimulation.segments[i].hasAppendages = (i == 16); // Terminal appendages
                insectSimulation.segments[i].appendagePairs = 1;
            }
        }
    }

    #endregion

    #region Pattern Formation Mechanisms

    private void SetupDevelopmentalModules()
    {
        // Clear existing modules
        developmentalModules.Clear();

        // Add head formation module
        developmentalModules.Add(new DevelopmentalModule(
            "Head Formation",
            (progress) => {
                // Activates early and remains active
                float activation = Mathf.Clamp01(progress * 2f) * anteriorDominance;
                DevelopmentalModule module = GetModule("Head Formation");
                if (module != null) module.activationLevel = activation;

                // Apply to simulation
                if (progress > 0.1f && insectSimulation.morphogens != null && insectSimulation.morphogens.Length > 0)
                {
                    // Find anterior morphogen
                    for (int i = 0; i < insectSimulation.morphogens.Length; i++)
                    {
                        if (insectSimulation.morphogens[i].name.Contains("Anterior"))
                        {
                            insectSimulation.ActivateMorphogen(insectSimulation.morphogens[i].name,
                                                             activation * insectSimulation.morphogens[i].concentration);
                            break;
                        }
                    }
                }
            },
            new float[] { 0f, 0.3f }, // Active in anterior 30% of body
            new string[] { "Hox1", "Neural_Dev" }
        ));

        // Add thorax formation module
        developmentalModules.Add(new DevelopmentalModule(
            "Thorax Formation",
            (progress) => {
                // Activates after head, peaks at mid-development
                float activation = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * 0.8f;
                DevelopmentalModule module = GetModule("Thorax Formation");
                if (module != null) module.activationLevel = activation;

                // Apply to simulation during appropriate stages
                if (progress > 0.2f && progress < 0.8f && insectSimulation.morphogens != null)
                {
                    // Activate segmentation and appendage morphogens
                    ActivateMorphogen("Segmentation", activation * segmentationStrength);

                    if (developWings && progress > 0.3f)
                    {
                        ActivateMorphogen("Appendage", activation * appendageFormationRate);
                    }
                }
            },
            new float[] { 0.3f, 0.5f }, // Active in middle section of body
            new string[] { "Appendage_Dev", "Epithelial_Dev" }
        ));

        // Add abdomen formation module
        developmentalModules.Add(new DevelopmentalModule(
            "Abdomen Formation",
            (progress) => {
                // Activates later in development
                float activation = Mathf.Clamp01((progress - 0.3f) * 1.5f) * 0.7f;
                DevelopmentalModule module = GetModule("Abdomen Formation");
                if (module != null) module.activationLevel = activation;

                // Apply to simulation during appropriate stages
                if (progress > 0.3f && insectSimulation.morphogens != null)
                {
                    // Activate segmentation morphogen
                    ActivateMorphogen("Segmentation", activation * segmentationStrength * 0.8f);

                    if (developExtendedAbdomen && progress > 0.5f)
                    {
                        // Special activation for extended abdomen development
                        ActivateMorphogen("Neural", activation * 0.5f);
                    }
                }

                // Activate specific genes during metamorphosis
                if (progress > 0.7f && insectSimulation.genes != null)
                {
                    ActivateGene("Epithelial_Dev", activation > 0.4f);
                }
            },
            new float[] { 0.5f, 1.0f }, // Active in posterior 50% of body
            new string[] { "Segmentation" }
        ));

        // Add wing formation module
        if (developWings)
        {
            developmentalModules.Add(new DevelopmentalModule(
                "Wing Formation",
                (progress) => {
                    // Only activates during specific developmental window
                    float activation = 0f;
                    if (progress > 0.4f && progress < 0.8f)
                    {
                        activation = Mathf.Sin((progress - 0.4f) * 2.5f * Mathf.PI) * appendageFormationRate;
                    }

                    DevelopmentalModule module = GetModule("Wing Formation");
                    if (module != null) module.activationLevel = activation;

                    // Apply to simulation during appropriate stages
                    if (activation > 0.1f)
                    {
                        ActivateMorphogen("Appendage", activation);
                        ActivateGene("Appendage_Dev", true);
                    }
                },
                new float[] { 0.35f, 0.45f }, // Specifically in wing-forming segments
                new string[] { "Appendage_Dev" }
            ));
        }

        // Add compound eye formation module
        if (developCompoundEyes)
        {
            developmentalModules.Add(new DevelopmentalModule(
                "Eye Formation",
                (progress) => {
                    // Activates early in head development
                    float activation = Mathf.Clamp01(progress * 3f) *
                                      Mathf.Clamp01((0.7f - progress) * 3f) *
                                      specializationRate;

                    DevelopmentalModule module = GetModule("Eye Formation");
                    if (module != null) module.activationLevel = activation;

                    // Apply to simulation during appropriate stages
                    if (activation > 0.1f)
                    {
                        ActivateMorphogen("Neural", activation);
                        ActivateGene("Neural_Dev", true);
                    }
                },
                new float[] { 0.05f, 0.15f }, // Active in anterior head region
                new string[] { "Neural_Dev" }
            ));
        }

        // Add specialized leg module
        if (developSpecializedLegs)
        {
            developmentalModules.Add(new DevelopmentalModule(
                "Specialized Legs",
                (progress) => {
                    // Activates during mid-development
                    float activation = 0f;
                    if (progress > 0.5f && progress < 0.9f)
                    {
                        activation = Mathf.Sin((progress - 0.5f) * 2.5f * Mathf.PI) *
                                    specializationRate * appendageFormationRate;
                    }

                    DevelopmentalModule module = GetModule("Specialized Legs");
                    if (module != null) module.activationLevel = activation;

                    // Apply to simulation during appropriate stages
                    if (activation > 0.2f)
                    {
                        ActivateMorphogen("Appendage", activation * 1.5f);
                        ActivateGene("Appendage_Dev", true);
                    }
                },
                new float[] { 0.4f, 0.5f }, // Active in thoracic leg segments
                new string[] { "Appendage_Dev" }
            ));
        }
    }

    private DevelopmentalModule GetModule(string name)
    {
        foreach (var module in developmentalModules)
        {
            if (module.name == name)
            {
                return module;
            }
        }
        return null;
    }

    private void ActivateMorphogen(string name, float level)
    {
        if (insectSimulation.morphogens == null) return;

        foreach (var morphogen in insectSimulation.morphogens)
        {
            if (morphogen.name.Contains(name))
            {
                insectSimulation.ActivateMorphogen(morphogen.name, level);
                break;
            }
        }
    }

    private void ActivateGene(string name, bool express)
    {
        if (insectSimulation.genes == null) return;

        foreach (var gene in insectSimulation.genes)
        {
            if (gene.name.Contains(name))
            {
                insectSimulation.ExpressGene(gene.name, express);
                break;
            }
        }
    }

    #endregion

    #region Evo-Devo Mechanisms

    /// <summary>
    /// Apply homeotic transformations (segment identity changes)
    /// </summary>
    private void ApplyHomeoticTransformations()
    {
        if (homeoticTransformationRate <= 0.01f) return;
        if (insectSimulation.segments == null) return;

        // Only apply during specific developmental windows
        if (insectSimulation.developmentProgress < 0.3f ||
            insectSimulation.developmentProgress > 0.7f) return;

        // Calculate random chance for a transformation
        if (UnityEngine.Random.value < homeoticTransformationRate * Time.deltaTime)
        {
            // Choose a segment to transform
            int segmentIndex = UnityEngine.Random.Range(3, insectSimulation.segments.Length - 2);

            // Determine transformation type:
            // 1. Segment to segment-1 identity (anteriorization)
            // 2. Segment to segment+1 identity (posteriorization)
            bool anteriorize = UnityEngine.Random.value < 0.5f;

            if (anteriorize && segmentIndex > 3)
            {
                // Transform to anterior segment identity
                TransformSegmentIdentity(segmentIndex, segmentIndex - 1);
            }
            else if (!anteriorize && segmentIndex < insectSimulation.segments.Length - 1)
            {
                // Transform to posterior segment identity
                TransformSegmentIdentity(segmentIndex, segmentIndex + 1);
            }
        }
    }

    /// <summary>
    /// Transform a segment's identity to match another segment
    /// </summary>
    private void TransformSegmentIdentity(int targetSegmentIndex, int sourceSegmentIndex)
    {
        if (insectSimulation.segments == null) return;
        if (targetSegmentIndex < 0 || targetSegmentIndex >= insectSimulation.segments.Length) return;
        if (sourceSegmentIndex < 0 || sourceSegmentIndex >= insectSimulation.segments.Length) return;

        var targetSegment = insectSimulation.segments[targetSegmentIndex];
        var sourceSegment = insectSimulation.segments[sourceSegmentIndex];

        // Transform properties (maintain position)
        float originalPosition = targetSegment.relativePosition;

        targetSegment.hasAppendages = sourceSegment.hasAppendages;
        targetSegment.appendagePairs = sourceSegment.appendagePairs;
        targetSegment.size = sourceSegment.size;
        targetSegment.allowedCellTypes = sourceSegment.allowedCellTypes;

        // Restore original position
        targetSegment.relativePosition = originalPosition;

        // Update segment
        insectSimulation.segments[targetSegmentIndex] = targetSegment;

        Debug.Log($"Applied homeotic transformation: Segment {targetSegmentIndex} transformed to identity of segment {sourceSegmentIndex}");
    }

    /// <summary>
    /// Apply heterochronic shifts (timing changes in development)
    /// </summary>
    private void ApplyHeterochronicShifts()
    {
        if (Mathf.Abs(heterochronyFactor) < 0.1f) return;

        // Positive factor: accelerate development (neoteny - adult features appear early)
        // Negative factor: delay development (hypermorphosis - juvenile features persist longer)

        // Apply to division rates
        float divisionModifier = 1.0f + heterochronyFactor * 0.5f;
        insectSimulation.divisionRate = Mathf.Clamp(insectSimulation.divisionRate * divisionModifier, 0.01f, 0.3f);

        // Apply to differentiation rates
        float diffModifier = 1.0f + heterochronyFactor * 0.7f;
        insectSimulation.differentiationRate = Mathf.Clamp(insectSimulation.differentiationRate * diffModifier, 0.01f, 0.3f);

        // Apply to migration rates
        float migrationModifier = 1.0f + heterochronyFactor * 0.3f;
        insectSimulation.migrationRate = Mathf.Clamp(insectSimulation.migrationRate * migrationModifier, 0.01f, 0.5f);
    }

    /// <summary>
    /// Apply allometric growth (different growth rates for different body parts)
    /// </summary>
    private void ApplyAllometricGrowth()
    {
        if (allometricGrowthFactor < 0.1f) return;
        if (insectSimulation.segments == null) return;

        // Apply only during growth phases
        if (insectSimulation.currentStage != InsectCellularSimulation.DevelopmentalStage.Embryo &&
            insectSimulation.currentStage != InsectCellularSimulation.DevelopmentalStage.Larva) return;

        // Calculate how much growth to apply
        float growthAmount = allometricGrowthFactor * Time.deltaTime * 0.1f;

        // Apply differential growth based on insect order and pattern
        switch (selectedOrder)
        {
            case InsectOrder.Diptera:
                // Enlarged head, reduced abdomen
                GrowSegmentRange(0, 2, growthAmount); // Head segments
                GrowSegmentRange(6, insectSimulation.segments.Length - 1, -growthAmount * 0.5f); // Abdomen
                break;

            case InsectOrder.Hymenoptera:
                // Enlarged thorax, narrow waist
                GrowSegmentRange(3, 5, growthAmount); // Thorax
                GrowSegmentRange(6, 6, -growthAmount * 2f); // Narrow waist
                break;

            case InsectOrder.Lepidoptera:
                // Enlarged wing-bearing thorax
                GrowSegmentRange(4, 5, growthAmount * 1.5f); // Wing segments
                break;

            case InsectOrder.Coleoptera:
                // Enlarged mesothorax (elytra), robust body
                GrowSegment(4, growthAmount * 2f); // Elytra segment
                GrowSegmentRange(6, 10, growthAmount * 0.5f); // Covered abdomen
                break;

            case InsectOrder.Orthoptera:
                // Enlarged jumping leg segment
                GrowSegment(5, growthAmount * 2f); // Metathorax with jumping legs
                GrowSegmentRange(6, 14, growthAmount * 0.5f); // Extended abdomen
                break;

            case InsectOrder.Hemiptera:
                // Enlarged head and forewings
                GrowSegmentRange(0, 2, growthAmount); // Head
                GrowSegment(4, growthAmount * 1.5f); // Forewings
                break;

            case InsectOrder.Odonata:
                // Enlarged head, thorax with wings, elongated abdomen
                GrowSegment(0, growthAmount * 1.5f); // Head with large eyes
                GrowSegmentRange(4, 5, growthAmount); // Wing-bearing segments
                GrowSegmentRange(6, insectSimulation.segments.Length - 1, growthAmount * 0.3f); // Long abdomen
                break;

            case InsectOrder.Custom:
                // Apply custom allometric growth pattern
                if (developWings)
                {
                    GrowSegmentRange(4, 5, growthAmount); // Wing segments
                }
                if (developCompoundEyes)
                {
                    GrowSegment(0, growthAmount); // Head with eyes
                }
                if (developSpecializedLegs)
                {
                    GrowSegment(5, growthAmount * 1.5f); // Leg segment
                }
                break;
        }
    }

    /// <summary>
    /// Apply growth to a specific segment
    /// </summary>
    private void GrowSegment(int index, float amount)
    {
        if (insectSimulation.segments == null) return;
        if (index < 0 || index >= insectSimulation.segments.Length) return;

        if (enableDevelopmentalConstraints)
        {
            // Apply constrains to prevent extreme growth
            float maxSize = 2.0f;
            float minSize = 0.3f;

            var segment = insectSimulation.segments[index];
            segment.size = Mathf.Clamp(segment.size + amount, minSize, maxSize);
            insectSimulation.segments[index] = segment;
        }
        else
        {
            // Unconstrained growth
            var segment = insectSimulation.segments[index];
            segment.size += amount;
            insectSimulation.segments[index] = segment;
        }
    }

    /// <summary>
    /// Apply growth to a range of segments
    /// </summary>
    private void GrowSegmentRange(int startIndex, int endIndex, float amount)
    {
        if (insectSimulation.segments == null) return;

        startIndex = Mathf.Clamp(startIndex, 0, insectSimulation.segments.Length - 1);
        endIndex = Mathf.Clamp(endIndex, 0, insectSimulation.segments.Length - 1);

        for (int i = startIndex; i <= endIndex; i++)
        {
            GrowSegment(i, amount);
        }
    }

    #endregion
}