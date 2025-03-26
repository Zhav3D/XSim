using UnityEngine;
using System.Collections.Generic;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runtime editor for insect morphogen gradients and gene expression networks.
/// Provides UI for controlling the developmental process.
/// </summary>
[RequireComponent(typeof(InsectCellularSimulation))]
public class InsectDevelopmentEditor : MonoBehaviour
{
    private bool showEditor = true;
    private Rect editorWindowRect = new Rect(20, 20, 600, 700);
    private Vector2 morphogenScrollPosition = Vector2.zero;
    private Vector2 geneScrollPosition = Vector2.zero;
    private Vector2 segmentScrollPosition = Vector2.zero;
    private Vector2 mainScrollPosition = Vector2.zero;

    // Tracked components
    private InsectCellularSimulation insectSimulation;
    private GPUParticleSimulation particleSimulation;

    // Foldout states
    private bool showDevelopmentSettings = true;
    private bool showMorphogenSettings = true;
    private bool showGeneSettings = false;
    private bool showSegmentSettings = false;
    private bool showVisualizationSettings = false;
    private bool showPresetManager = false;

    // New morphogen temp variables
    private string newMorphogenName = "New Morphogen";
    private float newMorphogenConcentration = 1.0f;
    private float newMorphogenDiffusionRate = 0.5f;
    private float newMorphogenDecayRate = 0.1f;
    private Color newMorphogenColor = Color.cyan;

    // New gene temp variables
    private string newGeneName = "New Gene";
    private float newGeneThreshold = 0.5f;
    private int newGeneActivatorIndex = 0;
    private int newGeneRepressorIndex = 0;
    private int newGeneResultIndex = 0;

    // Editor colors
    private Color headerColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    private Color sectionColor = new Color(0.25f, 0.25f, 0.25f, 0.8f);
    private Color buttonColor = new Color(0.3f, 0.5f, 0.8f);
    private Color saveButtonColor = new Color(0.2f, 0.7f, 0.3f);
    private Color deleteButtonColor = new Color(0.7f, 0.2f, 0.2f);
    private Color stageColors = new Color(0.4f, 0.7f, 0.9f);

    // Preset management
    private string newPresetName = "My Insect";
    private string presetSearchTerm = "";
    private int selectedPresetIndex = -1;
    private Vector2 presetScrollPosition = Vector2.zero;

    // Editor styles
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle boxStyle;
    private GUIStyle boldLabelStyle;
    private GUIStyle foldoutStyle;
    private GUIStyle buttonStyle;
    private GUIStyle stageButtonStyle;

    void Start()
    {
        insectSimulation = GetComponent<InsectCellularSimulation>();
        particleSimulation = GetComponent<GPUParticleSimulation>();

        if (insectSimulation == null)
        {
            Debug.LogError("InsectDevelopmentEditor requires InsectCellularSimulation component");
            enabled = false;
            return;
        }

        if (particleSimulation == null)
        {
            Debug.LogWarning("GPUParticleSimulation not found, some features will be disabled");
        }

        InitializeStyles();
    }

    void Update()
    {
        // Toggle editor visibility with Tab key
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            showEditor = !showEditor;
        }
    }

    void OnGUI()
    {
        if (!showEditor) return;

        InitializeStyles(); // Ensure styles are initialized even after domain reload

        // Draw the main editor window
        editorWindowRect = GUILayout.Window(1234, editorWindowRect, DrawEditorWindow, "Insect Development Editor", GUI.skin.window);

        // Make sure window stays within screen bounds
        editorWindowRect.x = Mathf.Clamp(editorWindowRect.x, 0, Screen.width - editorWindowRect.width);
        editorWindowRect.y = Mathf.Clamp(editorWindowRect.y, 0, Screen.height - editorWindowRect.height);
    }

    void DrawEditorWindow(int windowID)
    {
        mainScrollPosition = GUILayout.BeginScrollView(mainScrollPosition);

        GUILayout.Space(10);

        // Development Stage Buttons
        DrawDevelopmentalStageControls();

        GUILayout.Space(15);

        // Development Settings Section
        showDevelopmentSettings = DrawFoldoutSection("Development Settings", showDevelopmentSettings, DrawDevelopmentSettings);

        GUILayout.Space(10);

        // Morphogen Settings Section
        showMorphogenSettings = DrawFoldoutSection("Morphogen Gradients", showMorphogenSettings, DrawMorphogenSettings);

        GUILayout.Space(10);

        // Gene Settings Section
        showGeneSettings = DrawFoldoutSection("Gene Regulatory Network", showGeneSettings, DrawGeneSettings);

        GUILayout.Space(10);

        // Segment Settings Section
        showSegmentSettings = DrawFoldoutSection("Body Segmentation", showSegmentSettings, DrawSegmentSettings);

        GUILayout.Space(10);

        // Visualization Settings Section
        showVisualizationSettings = DrawFoldoutSection("Visualization Options", showVisualizationSettings, DrawVisualizationSettings);

        GUILayout.Space(10);

        // Preset Manager Section
        showPresetManager = DrawFoldoutSection("Preset Manager", showPresetManager, DrawPresetManager);

        GUILayout.Space(20);

        // Controls section at the bottom
        DrawControls();

        GUILayout.EndScrollView();

        // Make the window draggable
        GUI.DragWindow(new Rect(0, 0, editorWindowRect.width, 20));
    }

    #region Draw Section Methods

    private void DrawDevelopmentalStageControls()
    {
        GUILayout.BeginVertical(boxStyle);
        GUILayout.Label("Developmental Stage", subHeaderStyle);

        // Progress bar
        GUILayout.BeginHorizontal();
        GUILayout.Label("Progress:", GUILayout.Width(80));
        float newProgress = GUILayout.HorizontalSlider(insectSimulation.developmentProgress, 0f, 1f, GUILayout.Width(300));
        if (newProgress != insectSimulation.developmentProgress)
        {
            // Calculate developmental age from progress
            float oldAge = insectSimulation.developmentProgress;
            // Don't directly set progress as it's calculated from age
            // insectSimulation.developmentProgress = newProgress;
        }
        GUILayout.Label((insectSimulation.developmentProgress * 100f).ToString("F0") + "%", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Stage buttons
        GUILayout.BeginHorizontal();
        foreach (InsectCellularSimulation.DevelopmentalStage stage in Enum.GetValues(typeof(InsectCellularSimulation.DevelopmentalStage)))
        {
            GUI.backgroundColor = (insectSimulation.currentStage == stage) ? stageColors : Color.white;
            if (GUILayout.Button(stage.ToString(), stageButtonStyle, GUILayout.Height(30)))
            {
                insectSimulation.SetDevelopmentalStage(stage);
            }
            GUI.backgroundColor = Color.white;
        }
        GUILayout.EndHorizontal();

        // Development speed
        GUILayout.BeginHorizontal();
        GUILayout.Label("Development Speed:", GUILayout.Width(150));
        insectSimulation.developmentSpeed = GUILayout.HorizontalSlider(insectSimulation.developmentSpeed, 0f, 5f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.developmentSpeed.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawDevelopmentSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        // Bounding size multiplier
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bounding Size:", GUILayout.Width(100));
        insectSimulation.boundsMultiplier = GUILayout.HorizontalSlider(insectSimulation.boundsMultiplier, 0.5f, 1000f, GUILayout.Width(200));
        GUILayout.Label((insectSimulation.boundsMultiplier * 100f).ToString("F1"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // Spawn multiplier
        GUILayout.BeginHorizontal();
        GUILayout.Label("Spawn Multiplier:", GUILayout.Width(100));
        insectSimulation.spawnMultiplier = GUILayout.HorizontalSlider(insectSimulation.spawnMultiplier, 0.5f, 1000f, GUILayout.Width(200));
        GUILayout.Label((insectSimulation.spawnMultiplier * 100f).ToString("F1"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // Body plan parameters
        GUILayout.Label("Body Plan", subHeaderStyle);

        // Body dimensions
        GUILayout.BeginHorizontal();
        GUILayout.Label("Body Length:", GUILayout.Width(100));
        insectSimulation.bodyLength = GUILayout.HorizontalSlider(insectSimulation.bodyLength, 1f, 20f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.bodyLength.ToString("F1"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Body Width:", GUILayout.Width(100));
        insectSimulation.bodyWidth = GUILayout.HorizontalSlider(insectSimulation.bodyWidth, 1f, 10f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.bodyWidth.ToString("F1"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Body Height:", GUILayout.Width(100));
        insectSimulation.bodyHeight = GUILayout.HorizontalSlider(insectSimulation.bodyHeight, 1f, 10f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.bodyHeight.ToString("F1"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // Segment count
        GUILayout.BeginHorizontal();
        GUILayout.Label("Segment Count:", GUILayout.Width(100));
        int newSegmentCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(insectSimulation.segmentCount, 3, 20, GUILayout.Width(200)));
        GUILayout.Label(newSegmentCount.ToString(), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        if (newSegmentCount != insectSimulation.segmentCount)
        {
            // Only change if button is clicked to avoid constant reinitialization
            if (GUILayout.Button("Apply Segment Count", buttonStyle))
            {
                insectSimulation.segmentCount = newSegmentCount;
                insectSimulation.Reset();
            }
        }

        // Cell behaviors
        GUILayout.Space(10);
        GUILayout.Label("Cell Behaviors", subHeaderStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Division Rate:", GUILayout.Width(150));
        insectSimulation.divisionRate = GUILayout.HorizontalSlider(insectSimulation.divisionRate, 0.01f, 0.3f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.divisionRate.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Differentiation Rate:", GUILayout.Width(150));
        insectSimulation.differentiationRate = GUILayout.HorizontalSlider(insectSimulation.differentiationRate, 0.01f, 0.3f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.differentiationRate.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Migration Rate:", GUILayout.Width(150));
        insectSimulation.migrationRate = GUILayout.HorizontalSlider(insectSimulation.migrationRate, 0.01f, 0.5f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.migrationRate.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Adhesion Factor:", GUILayout.Width(150));
        insectSimulation.adhesionFactor = GUILayout.HorizontalSlider(insectSimulation.adhesionFactor, 0.1f, 1.0f, GUILayout.Width(200));
        GUILayout.Label(insectSimulation.adhesionFactor.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Simulation settings link to particle system
        GUILayout.Space(10);
        GUILayout.Label("Simulation Parameters", subHeaderStyle);

        if (particleSimulation != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Interaction Strength:", GUILayout.Width(150));
            particleSimulation.interactionStrength = GUILayout.HorizontalSlider(particleSimulation.interactionStrength, 0.1f, 5f, GUILayout.Width(200));
            GUILayout.Label(particleSimulation.interactionStrength.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dampening:", GUILayout.Width(150));
            particleSimulation.dampening = GUILayout.HorizontalSlider(particleSimulation.dampening, 0.8f, 1f, GUILayout.Width(200));
            GUILayout.Label(particleSimulation.dampening.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Interaction Radius:", GUILayout.Width(150));
            particleSimulation.interactionRadius = GUILayout.HorizontalSlider(particleSimulation.interactionRadius, 1f, 20f, GUILayout.Width(200));
            GUILayout.Label(particleSimulation.interactionRadius.ToString("F1"), GUILayout.Width(50));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    private void DrawMorphogenSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        // Check if morphogens array exists
        if (insectSimulation.morphogens == null || insectSimulation.morphogens.Length == 0)
        {
            GUILayout.Label("No morphogens defined yet.");

            // Add initial morphogens button
            if (GUILayout.Button("Initialize Default Morphogens", buttonStyle))
            {
                CreateDefaultMorphogens();
            }
        }
        else
        {
            // List existing morphogens
            GUILayout.Label("Current Morphogen Gradients", subHeaderStyle);

            morphogenScrollPosition = GUILayout.BeginScrollView(morphogenScrollPosition, GUILayout.Height(200));

            // Headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", boldLabelStyle, GUILayout.Width(120));
            GUILayout.Label("Concentration", boldLabelStyle, GUILayout.Width(100));
            GUILayout.Label("Diffusion", boldLabelStyle, GUILayout.Width(80));
            GUILayout.Label("Decay", boldLabelStyle, GUILayout.Width(80));
            GUILayout.Label("Color", boldLabelStyle, GUILayout.Width(50));
            GUILayout.Label("Visible", boldLabelStyle, GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // List each morphogen
            for (int i = 0; i < insectSimulation.morphogens.Length; i++)
            {
                InsectCellularSimulation.Morphogen morphogen = insectSimulation.morphogens[i];

                GUILayout.BeginHorizontal();

                // Name
                string newName = GUILayout.TextField(morphogen.name, GUILayout.Width(120));
                if (newName != morphogen.name)
                {
                    morphogen.name = newName;
                }

                // Concentration
                float newConcentration = GUILayout.HorizontalSlider(morphogen.concentration, 0f, 2f, GUILayout.Width(100));
                if (newConcentration != morphogen.concentration)
                {
                    morphogen.concentration = newConcentration;
                }

                // Diffusion rate
                float newDiffusion = GUILayout.HorizontalSlider(morphogen.diffusionRate, 0f, 1f, GUILayout.Width(80));
                if (newDiffusion != morphogen.diffusionRate)
                {
                    morphogen.diffusionRate = newDiffusion;
                }

                // Decay rate
                float newDecay = GUILayout.HorizontalSlider(morphogen.decayRate, 0f, 0.5f, GUILayout.Width(80));
                if (newDecay != morphogen.decayRate)
                {
                    morphogen.decayRate = newDecay;
                }

                // Color
                GUI.backgroundColor = morphogen.visualizationColor;
                if (GUILayout.Button("", GUILayout.Width(30), GUILayout.Height(18)))
                {
                    // Cycle through colors
                    morphogen.visualizationColor = CycleColor(morphogen.visualizationColor);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(20);

                // Visibility toggle
                morphogen.isVisible = GUILayout.Toggle(morphogen.isVisible, "", GUILayout.Width(20));

                // Activate button
                if (GUILayout.Button("Activate", GUILayout.Width(70)))
                {
                    insectSimulation.ActivateMorphogen(morphogen.name, morphogen.concentration);
                }

                GUILayout.EndHorizontal();

                // Update the morphogen in the array
                insectSimulation.morphogens[i] = morphogen;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);
        }

        // Add new morphogen section
        GUILayout.Label("Add New Morphogen", subHeaderStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(60));
        newMorphogenName = GUILayout.TextField(newMorphogenName, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Concentration:", GUILayout.Width(100));
        newMorphogenConcentration = GUILayout.HorizontalSlider(newMorphogenConcentration, 0f, 2f, GUILayout.Width(150));
        GUILayout.Label(newMorphogenConcentration.ToString("F2"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Diffusion Rate:", GUILayout.Width(100));
        newMorphogenDiffusionRate = GUILayout.HorizontalSlider(newMorphogenDiffusionRate, 0f, 1f, GUILayout.Width(150));
        GUILayout.Label(newMorphogenDiffusionRate.ToString("F2"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Decay Rate:", GUILayout.Width(100));
        newMorphogenDecayRate = GUILayout.HorizontalSlider(newMorphogenDecayRate, 0f, 0.5f, GUILayout.Width(150));
        GUILayout.Label(newMorphogenDecayRate.ToString("F2"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Color:", GUILayout.Width(60));
        GUI.backgroundColor = newMorphogenColor;
        if (GUILayout.Button("", GUILayout.Width(40), GUILayout.Height(20)))
        {
            newMorphogenColor = CycleColor(newMorphogenColor);
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Add Morphogen", buttonStyle))
        {
            AddNewMorphogen();
        }

        GUILayout.EndVertical();
    }

    private void DrawGeneSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        // Check if genes array exists
        if (insectSimulation.genes == null || insectSimulation.genes.Length == 0)
        {
            GUILayout.Label("No genes defined yet.");

            // Add initial genes button
            if (GUILayout.Button("Initialize Default Genes", buttonStyle))
            {
                CreateDefaultGenes();
            }
        }
        else
        {
            // List existing genes
            GUILayout.Label("Current Gene Regulatory Network", subHeaderStyle);

            geneScrollPosition = GUILayout.BeginScrollView(geneScrollPosition, GUILayout.Height(200));

            // Display each gene
            for (int i = 0; i < insectSimulation.genes.Length; i++)
            {
                InsectCellularSimulation.Gene gene = insectSimulation.genes[i];

                GUILayout.BeginVertical(GUI.skin.box);

                // Gene header with name and state
                GUILayout.BeginHorizontal();

                string newName = GUILayout.TextField(gene.name, GUILayout.Width(150));
                if (newName != gene.name)
                {
                    gene.name = newName;
                }

                GUILayout.Label("Threshold:", GUILayout.Width(70));
                gene.expressionThreshold = GUILayout.HorizontalSlider(gene.expressionThreshold, 0f, 1f, GUILayout.Width(100));
                GUILayout.Label(gene.expressionThreshold.ToString("F2"), GUILayout.Width(40));

                GUI.backgroundColor = gene.isExpressed ? Color.green : Color.gray;
                GUILayout.Label(gene.isExpressed ? "Expressed" : "Not Expressed", GUILayout.Width(100));
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();

                // Expression control
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Force Express", GUILayout.Width(120)))
                {
                    insectSimulation.ExpressGene(gene.name, true);
                }
                if (GUILayout.Button("Force Suppress", GUILayout.Width(120)))
                {
                    insectSimulation.ExpressGene(gene.name, false);
                }
                GUILayout.EndHorizontal();

                // Display activators
                GUILayout.Label("Activators:", boldLabelStyle);
                if (gene.activators != null && gene.activators.Length > 0)
                {
                    foreach (var activator in gene.activators)
                    {
                        GUILayout.Label("• " + activator.name);
                    }
                }
                else
                {
                    GUILayout.Label("None");
                }

                // Display repressors
                GUILayout.Label("Repressors:", boldLabelStyle);
                if (gene.repressors != null && gene.repressors.Length > 0)
                {
                    foreach (var repressor in gene.repressors)
                    {
                        GUILayout.Label("• " + repressor.name);
                    }
                }
                else
                {
                    GUILayout.Label("None");
                }

                // Display expression results
                GUILayout.Label("Expression Results:", boldLabelStyle);
                if (gene.expressionResults != null && gene.expressionResults.Length > 0)
                {
                    foreach (var result in gene.expressionResults)
                    {
                        GUILayout.Label("• " + result.ToString());
                    }
                }
                else
                {
                    GUILayout.Label("None");
                }

                GUILayout.EndVertical();

                // Update the gene in the array
                insectSimulation.genes[i] = gene;

                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);
        }

        // Add new gene section
        GUILayout.Label("Add New Gene", subHeaderStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(60));
        newGeneName = GUILayout.TextField(newGeneName, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Threshold:", GUILayout.Width(80));
        newGeneThreshold = GUILayout.HorizontalSlider(newGeneThreshold, 0f, 1f, GUILayout.Width(150));
        GUILayout.Label(newGeneThreshold.ToString("F2"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // Morphogen selection
        if (insectSimulation.morphogens != null && insectSimulation.morphogens.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Activator:", GUILayout.Width(80));

            string[] morphogenNames = new string[insectSimulation.morphogens.Length + 1];
            morphogenNames[0] = "None";
            for (int i = 0; i < insectSimulation.morphogens.Length; i++)
            {
                morphogenNames[i + 1] = insectSimulation.morphogens[i].name;
            }

            newGeneActivatorIndex = GUILayout.SelectionGrid(newGeneActivatorIndex, morphogenNames, 3);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Repressor:", GUILayout.Width(80));
            newGeneRepressorIndex = GUILayout.SelectionGrid(newGeneRepressorIndex, morphogenNames, 3);
            GUILayout.EndHorizontal();
        }

        // Cell type selection
        GUILayout.BeginHorizontal();
        GUILayout.Label("Expression Result:", GUILayout.Width(120));

        string[] cellTypeNames = Enum.GetNames(typeof(InsectCellularSimulation.CellType));
        newGeneResultIndex = GUILayout.SelectionGrid(newGeneResultIndex, cellTypeNames, 3);

        GUILayout.EndHorizontal();

        if (GUILayout.Button("Add Gene", buttonStyle))
        {
            AddNewGene();
        }

        GUILayout.EndVertical();
    }

    private void DrawSegmentSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        // Check if segments array exists
        if (insectSimulation.segments == null || insectSimulation.segments.Length == 0)
        {
            GUILayout.Label("No segments defined yet. Set segment count and initialize.");
        }
        else
        {
            // List existing segments
            GUILayout.Label("Body Segments", subHeaderStyle);

            segmentScrollPosition = GUILayout.BeginScrollView(segmentScrollPosition, GUILayout.Height(300));

            // Display each segment
            for (int i = 0; i < insectSimulation.segments.Length; i++)
            {
                InsectCellularSimulation.BodySegment segment = insectSimulation.segments[i];

                GUILayout.BeginVertical(GUI.skin.box);

                // Segment header and position
                GUILayout.BeginHorizontal();
                GUILayout.Label(segment.name, boldLabelStyle, GUILayout.Width(120));

                GUILayout.Label("Position:", GUILayout.Width(60));
                segment.relativePosition = GUILayout.HorizontalSlider(segment.relativePosition, 0f, 1f, GUILayout.Width(100));
                GUILayout.Label(segment.relativePosition.ToString("F2"), GUILayout.Width(40));

                GUILayout.Label("Size:", GUILayout.Width(40));
                segment.size = GUILayout.HorizontalSlider(segment.size, 0.2f, 2f, GUILayout.Width(100));
                GUILayout.Label(segment.size.ToString("F2"), GUILayout.Width(40));

                GUILayout.EndHorizontal();

                // Appendage settings
                GUILayout.BeginHorizontal();
                segment.hasAppendages = GUILayout.Toggle(segment.hasAppendages, "Has Appendages", GUILayout.Width(120));

                if (segment.hasAppendages)
                {
                    GUILayout.Label("Pairs:", GUILayout.Width(40));
                    segment.appendagePairs = Mathf.RoundToInt(GUILayout.HorizontalSlider(segment.appendagePairs, 1, 3, GUILayout.Width(100)));
                    GUILayout.Label(segment.appendagePairs.ToString(), GUILayout.Width(20));
                }

                GUILayout.EndHorizontal();

                // Allowed cell types (checkboxes)
                GUILayout.Label("Allowed Cell Types:", boldLabelStyle);

                if (segment.allowedCellTypes != null && segment.allowedCellTypes.Length > 0)
                {
                    GUILayout.BeginHorizontal();
                    int columnCount = 0;
                    foreach (var cellType in segment.allowedCellTypes)
                    {
                        GUILayout.Label("• " + cellType.ToString(), GUILayout.Width(100));
                        columnCount++;
                        if (columnCount % 3 == 0)
                        {
                            GUILayout.EndHorizontal();
                            GUILayout.BeginHorizontal();
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();

                // Update the segment in the array
                insectSimulation.segments[i] = segment;

                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();

            // Apply segment changes button
            if (GUILayout.Button("Apply Segment Changes", buttonStyle))
            {
                // Apply segment changes might require recalculating positions
                // For now, we'll just mark as dirty
#if UNITY_EDITOR
                EditorUtility.SetDirty(insectSimulation);
#endif
            }
        }

        GUILayout.EndVertical();
    }

    private void DrawVisualizationSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        // Visualization toggles
        insectSimulation.showMorphogenGradients = GUILayout.Toggle(insectSimulation.showMorphogenGradients, "Show Morphogen Gradients");
        insectSimulation.showGeneExpression = GUILayout.Toggle(insectSimulation.showGeneExpression, "Show Gene Expression");
        insectSimulation.showBodySegments = GUILayout.Toggle(insectSimulation.showBodySegments, "Show Body Segments");
        insectSimulation.labelCellTypes = GUILayout.Toggle(insectSimulation.labelCellTypes, "Label Cell Types");

        GUILayout.Space(10);

        if (particleSimulation != null)
        {
            // Debug visualization toggles
            GUILayout.Label("Debug Visualization", subHeaderStyle);
            particleSimulation.debugDrawCells = GUILayout.Toggle(particleSimulation.debugDrawCells, "Debug Draw Grid Cells");
            particleSimulation.debugDrawParticles = GUILayout.Toggle(particleSimulation.debugDrawParticles, "Debug Draw Particles");
        }

        GUILayout.EndVertical();
    }

    private void DrawPresetManager()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Save Current Configuration", subHeaderStyle);
        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Preset Name:", GUILayout.Width(100));
        newPresetName = GUILayout.TextField(newPresetName, GUILayout.Width(200));

        GUI.backgroundColor = saveButtonColor;
        if (GUILayout.Button("Save Preset", buttonStyle, GUILayout.Width(100)))
        {
            SaveCurrentAsPreset(newPresetName);
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUILayout.Label("Insect Presets", subHeaderStyle);
        GUILayout.Space(5);

        // Load insect presets would go here
        GUILayout.Label("Custom presets will be saved here");

        GUILayout.Space(10);

        // Standard insect body plans
        GUILayout.Label("Standard Insect Body Plans", subHeaderStyle);
        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Fruit Fly (Drosophila)", buttonStyle))
        {
            LoadDrosophilaPreset();
        }

        if (GUILayout.Button("Honey Bee", buttonStyle))
        {
            LoadHoneyBeePreset();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Butterfly", buttonStyle))
        {
            LoadButterflyPreset();
        }

        if (GUILayout.Button("Grasshopper", buttonStyle))
        {
            LoadGrasshopperPreset();
        }
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawControls()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Hide Editor (Tab)", GUILayout.Height(30)))
        {
            showEditor = false;
        }

        if (Application.isPlaying)
        {
            if (GUILayout.Button("Reset Simulation", GUILayout.Height(30)))
            {
                insectSimulation.Reset();
            }

            if (Time.timeScale > 0.01f)
            {
                if (GUILayout.Button("Pause", GUILayout.Height(30)))
                {
                    Time.timeScale = 0f;
                }
            }
            else
            {
                if (GUILayout.Button("Resume", GUILayout.Height(30)))
                {
                    Time.timeScale = 1f;
                }
            }
        }

        GUILayout.EndHorizontal();
    }

    #endregion

    #region Helper Methods

    private bool DrawFoldoutSection(string title, bool expanded, System.Action drawContent)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        // Draw header with toggle
        GUILayout.BeginHorizontal(headerStyle);
        expanded = GUILayout.Toggle(expanded, title, foldoutStyle, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        // Draw content if expanded
        if (expanded)
        {
            drawContent?.Invoke();
        }

        GUILayout.EndVertical();

        return expanded;
    }

    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            // Header style
            headerStyle = new GUIStyle(GUI.skin.box);
            headerStyle.normal.background = MakeTexture(2, 2, headerColor);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.margin = new RectOffset(0, 0, 0, 0);
            headerStyle.padding = new RectOffset(10, 10, 5, 5);

            // SubHeader style
            subHeaderStyle = new GUIStyle(GUI.skin.label);
            subHeaderStyle.fontStyle = FontStyle.Bold;
            subHeaderStyle.fontSize = 12;

            // Box style
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = MakeTexture(2, 2, sectionColor);
            boxStyle.padding = new RectOffset(10, 10, 10, 10);

            // Bold label style
            boldLabelStyle = new GUIStyle(GUI.skin.label);
            boldLabelStyle.fontStyle = FontStyle.Bold;

            // Custom foldout style
            foldoutStyle = new GUIStyle(GUI.skin.label);
            foldoutStyle.fontStyle = FontStyle.Bold;
            foldoutStyle.alignment = TextAnchor.MiddleLeft;

            // Button style
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = MakeTexture(2, 2, buttonColor);
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.padding = new RectOffset(12, 12, 8, 8);

            // Stage button style
            stageButtonStyle = new GUIStyle(GUI.skin.button);
            stageButtonStyle.fontStyle = FontStyle.Bold;
            stageButtonStyle.alignment = TextAnchor.MiddleCenter;
        }
    }

    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private Color CycleColor(Color currentColor)
    {
        // Define preset colors to cycle through
        Color[] presetColors = new Color[]
        {
            Color.red,
            new Color(1f, 0.5f, 0f), // Orange
            Color.yellow,
            Color.green,
            Color.cyan,
            Color.blue,
            new Color(0.5f, 0f, 1f), // Purple
            Color.magenta,
            new Color(1f, 0.7f, 0.7f), // Light pink
            new Color(0.7f, 1f, 0.7f), // Light green
            new Color(0.7f, 0.7f, 1f)  // Light blue
        };

        // Find closest color and cycle to next
        float closestDist = float.MaxValue;
        int closestIdx = 0;

        for (int i = 0; i < presetColors.Length; i++)
        {
            float dist = ColorDistance(currentColor, presetColors[i]);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestIdx = i;
            }
        }

        // Return next color in the cycle
        return presetColors[(closestIdx + 1) % presetColors.Length];
    }

    private float ColorDistance(Color a, Color b)
    {
        return Mathf.Sqrt(
            Mathf.Pow(a.r - b.r, 2) +
            Mathf.Pow(a.g - b.g, 2) +
            Mathf.Pow(a.b - b.b, 2)
        );
    }

    #endregion

    #region Data Management

    private void CreateDefaultMorphogens()
    {
        // Create basic set of developmental morphogens
        InsectCellularSimulation.Morphogen[] defaultMorphogens = new InsectCellularSimulation.Morphogen[]
        {
            new InsectCellularSimulation.Morphogen
            {
                name = "Anterior-Posterior",
                concentration = 1.0f,
                diffusionRate = 0.3f,
                decayRate = 0.05f,
                visualizationColor = Color.blue,
                isVisible = true
            },
            new InsectCellularSimulation.Morphogen
            {
                name = "Dorsal-Ventral",
                concentration = 1.0f,
                diffusionRate = 0.3f,
                decayRate = 0.05f,
                visualizationColor = Color.green,
                isVisible = true
            },
            new InsectCellularSimulation.Morphogen
            {
                name = "Segmentation",
                concentration = 0.8f,
                diffusionRate = 0.2f,
                decayRate = 0.1f,
                visualizationColor = Color.yellow,
                isVisible = true
            },
            new InsectCellularSimulation.Morphogen
            {
                name = "Appendage",
                concentration = 0.5f,
                diffusionRate = 0.1f,
                decayRate = 0.2f,
                visualizationColor = Color.red,
                isVisible = true
            },
            new InsectCellularSimulation.Morphogen
            {
                name = "Neural",
                concentration = 0.7f,
                diffusionRate = 0.4f,
                decayRate = 0.05f,
                visualizationColor = new Color(0.5f, 0f, 1f), // Purple
                isVisible = true
            }
        };

        insectSimulation.morphogens = defaultMorphogens;

#if UNITY_EDITOR
        EditorUtility.SetDirty(insectSimulation);
#endif
    }

    private void CreateDefaultGenes()
    {
        // Check if morphogens are defined first
        if (insectSimulation.morphogens == null || insectSimulation.morphogens.Length == 0)
        {
            CreateDefaultMorphogens();
        }

        // Create a basic set of developmental genes
        InsectCellularSimulation.Gene[] defaultGenes = new InsectCellularSimulation.Gene[]
        {
            new InsectCellularSimulation.Gene
            {
                name = "Hox1",
                expressionThreshold = 0.6f,
                activators = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[0] }, // AP gradient
                expressionResults = new InsectCellularSimulation.CellType[] {
                    InsectCellularSimulation.CellType.Segment,
                    InsectCellularSimulation.CellType.Neural
                },
                isExpressed = false
            },

            new InsectCellularSimulation.Gene
            {
                name = "Appendage_Dev",
                expressionThreshold = 0.5f,
                activators = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[3] }, // Appendage
                repressors = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[0] }, // AP inhibits in wrong zones
                expressionResults = new InsectCellularSimulation.CellType[] {
                    InsectCellularSimulation.CellType.Appendage
                },
                isExpressed = false
            },

            new InsectCellularSimulation.Gene
            {
                name = "Epithelial_Dev",
                expressionThreshold = 0.4f,
                activators = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[1] }, // DV gradient
                expressionResults = new InsectCellularSimulation.CellType[] {
                    InsectCellularSimulation.CellType.Epithelial,
                    InsectCellularSimulation.CellType.Cuticle
                },
                isExpressed = false
            },

            new InsectCellularSimulation.Gene
            {
                name = "Neural_Dev",
                expressionThreshold = 0.6f,
                activators = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[4] }, // Neural
                expressionResults = new InsectCellularSimulation.CellType[] {
                    InsectCellularSimulation.CellType.Neural
                },
                isExpressed = false
            },

            new InsectCellularSimulation.Gene
            {
                name = "Segmentation",
                expressionThreshold = 0.5f,
                activators = new InsectCellularSimulation.Morphogen[] { insectSimulation.morphogens[2] }, // Segmentation
                expressionResults = new InsectCellularSimulation.CellType[] {
                    InsectCellularSimulation.CellType.Segment
                },
                isExpressed = false
            }
        };

        insectSimulation.genes = defaultGenes;

#if UNITY_EDITOR
        EditorUtility.SetDirty(insectSimulation);
#endif
    }

    private void AddNewMorphogen()
    {
        // Create new morphogen instance
        InsectCellularSimulation.Morphogen newMorphogen = new InsectCellularSimulation.Morphogen
        {
            name = newMorphogenName,
            concentration = newMorphogenConcentration,
            diffusionRate = newMorphogenDiffusionRate,
            decayRate = newMorphogenDecayRate,
            visualizationColor = newMorphogenColor,
            isVisible = true
        };

        // Add to array
        if (insectSimulation.morphogens == null)
        {
            insectSimulation.morphogens = new InsectCellularSimulation.Morphogen[1];
            insectSimulation.morphogens[0] = newMorphogen;
        }
        else
        {
            // Resize array
            InsectCellularSimulation.Morphogen[] newArray = new InsectCellularSimulation.Morphogen[insectSimulation.morphogens.Length + 1];
            Array.Copy(insectSimulation.morphogens, newArray, insectSimulation.morphogens.Length);
            newArray[newArray.Length - 1] = newMorphogen;
            insectSimulation.morphogens = newArray;
        }

        // Reset temp values
        newMorphogenName = "New Morphogen " + (insectSimulation.morphogens.Length + 1);
        newMorphogenConcentration = 1.0f;
        newMorphogenDiffusionRate = 0.5f;
        newMorphogenDecayRate = 0.1f;
        newMorphogenColor = CycleColor(newMorphogenColor);

#if UNITY_EDITOR
        EditorUtility.SetDirty(insectSimulation);
#endif
    }

    private void AddNewGene()
    {
        // Ensure morphogens exist
        if (insectSimulation.morphogens == null || insectSimulation.morphogens.Length == 0)
        {
            Debug.LogError("Cannot add gene - no morphogens defined");
            return;
        }

        // Setup activators
        InsectCellularSimulation.Morphogen[] activators = null;
        if (newGeneActivatorIndex > 0) // 0 is "None"
        {
            activators = new InsectCellularSimulation.Morphogen[] {
                insectSimulation.morphogens[newGeneActivatorIndex - 1]
            };
        }

        // Setup repressors
        InsectCellularSimulation.Morphogen[] repressors = null;
        if (newGeneRepressorIndex > 0) // 0 is "None"
        {
            repressors = new InsectCellularSimulation.Morphogen[] {
                insectSimulation.morphogens[newGeneRepressorIndex - 1]
            };
        }

        // Setup expression results
        InsectCellularSimulation.CellType[] expressionResults = new InsectCellularSimulation.CellType[] {
            (InsectCellularSimulation.CellType)newGeneResultIndex
        };

        // Create new gene instance
        InsectCellularSimulation.Gene newGene = new InsectCellularSimulation.Gene
        {
            name = newGeneName,
            expressionThreshold = newGeneThreshold,
            activators = activators,
            repressors = repressors,
            expressionResults = expressionResults,
            isExpressed = false
        };

        // Add to array
        if (insectSimulation.genes == null)
        {
            insectSimulation.genes = new InsectCellularSimulation.Gene[1];
            insectSimulation.genes[0] = newGene;
        }
        else
        {
            // Resize array
            InsectCellularSimulation.Gene[] newArray = new InsectCellularSimulation.Gene[insectSimulation.genes.Length + 1];
            Array.Copy(insectSimulation.genes, newArray, insectSimulation.genes.Length);
            newArray[newArray.Length - 1] = newGene;
            insectSimulation.genes = newArray;
        }

        // Reset temp values
        newGeneName = "New Gene " + (insectSimulation.genes.Length + 1);
        newGeneThreshold = 0.5f;
        newGeneActivatorIndex = 0;
        newGeneRepressorIndex = 0;

#if UNITY_EDITOR
        EditorUtility.SetDirty(insectSimulation);
#endif
    }

    private void SaveCurrentAsPreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            Debug.LogWarning("Preset name cannot be empty.");
            return;
        }

        // This would save to a file in a production implementation

        Debug.Log("Preset saved: " + presetName);

        // Generate a new default name for the next preset
        newPresetName = "My Insect " + DateTime.Now.ToString("MMdd_HHmm");
    }

    #endregion

    #region Insect Presets

    private void LoadDrosophilaPreset()
    {
        // 1. Set basic properties for fruit fly
        insectSimulation.bodyLength = 8f;
        insectSimulation.bodyWidth = 2f;
        insectSimulation.bodyHeight = 2f;
        insectSimulation.segmentCount = 14; // 3 head, 3 thorax, 8 abdomen

        // 2. Reset segmentation
        insectSimulation.Reset();

        // 3. Set development to egg stage to start fresh
        insectSimulation.SetDevelopmentalStage(InsectCellularSimulation.DevelopmentalStage.Egg);

        Debug.Log("Drosophila preset loaded");
    }

    private void LoadHoneyBeePreset()
    {
        // 1. Set basic properties for honey bee
        insectSimulation.bodyLength = 12f;
        insectSimulation.bodyWidth = 3f;
        insectSimulation.bodyHeight = 3f;
        insectSimulation.segmentCount = 16; // 3 head, 3 thorax, 10 abdomen

        // 2. Reset segmentation
        insectSimulation.Reset();

        // 3. Set development to egg stage to start fresh
        insectSimulation.SetDevelopmentalStage(InsectCellularSimulation.DevelopmentalStage.Egg);

        Debug.Log("Honey Bee preset loaded");
    }

    private void LoadButterflyPreset()
    {
        // 1. Set basic properties for butterfly
        insectSimulation.bodyLength = 15f;
        insectSimulation.bodyWidth = 5f;
        insectSimulation.bodyHeight = 2f;
        insectSimulation.segmentCount = 15; // 3 head, 3 thorax, 9 abdomen

        // 2. Reset segmentation
        insectSimulation.Reset();

        // 3. Set development to egg stage to start fresh
        insectSimulation.SetDevelopmentalStage(InsectCellularSimulation.DevelopmentalStage.Egg);

        Debug.Log("Butterfly preset loaded");
    }

    private void LoadGrasshopperPreset()
    {
        // 1. Set basic properties for grasshopper
        insectSimulation.bodyLength = 18f;
        insectSimulation.bodyWidth = 3f;
        insectSimulation.bodyHeight = 4f;
        insectSimulation.segmentCount = 18; // 3 head, 3 thorax, 12 abdomen

        // 2. Reset segmentation
        insectSimulation.Reset();

        // 3. Set development to egg stage to start fresh
        insectSimulation.SetDevelopmentalStage(InsectCellularSimulation.DevelopmentalStage.Egg);

        Debug.Log("Grasshopper preset loaded");
    }

    #endregion
}