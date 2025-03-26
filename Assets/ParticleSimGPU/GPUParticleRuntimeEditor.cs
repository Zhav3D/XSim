using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

/// <summary>
/// Runtime OnGUI editor for GPU Particle System.
/// Attach this to the same GameObject as GPUParticleSimulation.
/// Press Tab to toggle editor visibility.
/// Features include runtime parameter editing and preset management.
/// </summary>
[RequireComponent(typeof(GPUParticleSimulation))]
public class GPUParticleRuntimeEditor : MonoBehaviour
{
    // Editor window properties
    private bool showEditor = true;
    private Rect editorWindowRect = new Rect(20, 20, 600, 700);
    private Vector2 scrollPosition = Vector2.zero;

    // Tracked components
    private GPUParticleSimulation simulation;
    private GPUInteractionMatrixGenerator matrixGenerator;

    // Foldout states
    private bool showSimulationSettings = true;
    private bool showPerformanceSettings = false;
    private bool showLODSettings = false;
    private bool showDebugInfo = false;
    private bool showInteractionMatrix = false;
    private bool showParticleTypes = false;
    private bool showPresets = false;
    private bool showPresetManager = false;

    // Matrix visualization
    private Dictionary<(int, int), float> interactionValues = new Dictionary<(int, int), float>();
    private Color[] colorLegend = new Color[] {
        new Color(1f, 0.7f, 0.7f), // Deep repulsion (red)
        new Color(1f, 0.85f, 0.85f), // Light repulsion (pink)
        Color.white, // Neutral
        new Color(0.85f, 1f, 0.85f), // Light attraction (light green)
        new Color(0.7f, 1f, 0.7f) // Deep attraction (green)
    };

    // Performance tracking
    private float[] frameTimes = new float[60];
    private int frameTimeIndex = 0;
    private float frameTimeAvg = 0;
    private float frameTimeMin = float.MaxValue;
    private float frameTimeMax = 0;

    // New particle type temp variables
    private string newTypeName = "New Type";
    private Color newTypeColor = Color.white;
    private float newTypeMass = 1.0f;
    private float newTypeRadius = 0.5f;
    private float newTypeSpawnAmount = 50f;

    // Editor styles
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle boxStyle;
    private GUIStyle boldLabelStyle;
    private GUIStyle matrixCellStyle;
    private GUIStyle foldoutStyle;
    private GUIStyle buttonStyle;
    private GUIStyle cellValueStyle;
    private GUIStyle presetButtonStyle;

    // Editor colors
    private Color headerColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    private Color sectionColor = new Color(0.25f, 0.25f, 0.25f, 0.8f);
    private Color buttonColor = new Color(0.3f, 0.5f, 0.8f);
    private Color saveButtonColor = new Color(0.2f, 0.7f, 0.3f);
    private Color deleteButtonColor = new Color(0.7f, 0.2f, 0.2f);

    // Constants
    private const float MATRIX_CELL_SIZE = 40f;
    private const int WINDOW_ID = 9876;

    // Runtime parameter changes
    private float interactionStrengthChange = 0f;
    private float interactionRadiusChange = 0f;

    // Preset management
    private List<SimulationPreset> savedPresets = new List<SimulationPreset>();
    private string newPresetName = "My Preset";
    private int selectedPresetIndex = -1;
    private string presetSearchTerm = "";
    private Vector2 presetScrollPosition = Vector2.zero;
    private string presetPath;

    [Serializable]
    public class SimulationPreset
    {
        public string name;
        public string date;

        // Simulation Settings
        public float simulationSpeed;
        public float collisionElasticity;
        public Vector3 simulationBounds;
        public float dampening;
        public float interactionStrength;
        public float minDistance;
        public float bounceForce;
        public float maxForce;
        public float maxVelocity;
        public float interactionRadius;
        public float cellSize;

        // Particle Types (serialized separately)
        public List<SerializedParticleType> particleTypes = new List<SerializedParticleType>();

        // Interaction Rules (serialized separately)
        public List<SerializedInteractionRule> interactionRules = new List<SerializedInteractionRule>();

        [Serializable]
        public class SerializedParticleType
        {
            public string name;
            public float r, g, b, a; // Color components
            public float mass;
            public float radius;
            public float spawnAmount;

            public SerializedParticleType(GPUParticleSimulation.ParticleType type)
            {
                name = type.name;
                r = type.color.r;
                g = type.color.g;
                b = type.color.b;
                a = type.color.a;
                mass = type.mass;
                radius = type.radius;
                spawnAmount = type.spawnAmount;
            }

            public GPUParticleSimulation.ParticleType ToParticleType()
            {
                return new GPUParticleSimulation.ParticleType
                {
                    name = name,
                    color = new Color(r, g, b, a),
                    mass = mass,
                    radius = radius,
                    spawnAmount = spawnAmount
                };
            }
        }

        [Serializable]
        public class SerializedInteractionRule
        {
            public int typeIndexA;
            public int typeIndexB;
            public float attractionValue;

            public SerializedInteractionRule(GPUParticleSimulation.InteractionRule rule)
            {
                typeIndexA = rule.typeIndexA;
                typeIndexB = rule.typeIndexB;
                attractionValue = rule.attractionValue;
            }

            public GPUParticleSimulation.InteractionRule ToInteractionRule()
            {
                return new GPUParticleSimulation.InteractionRule
                {
                    typeIndexA = typeIndexA,
                    typeIndexB = typeIndexB,
                    attractionValue = attractionValue
                };
            }
        }
    }

    void Start()
    {
        simulation = GetComponent<GPUParticleSimulation>();
        matrixGenerator = GetComponent<GPUInteractionMatrixGenerator>();

        if (simulation == null)
        {
            Debug.LogError("GPUParticleRuntimeEditor requires GPUParticleSimulation component");
            enabled = false;
            return;
        }

        if (matrixGenerator == null)
        {
            Debug.LogWarning("GPUInteractionMatrixGenerator not found, some features will be disabled");
        }

        // Initialize presets directory
        presetPath = Path.Combine(Application.persistentDataPath, "ParticlePresets");
        if (!Directory.Exists(presetPath))
        {
            Directory.CreateDirectory(presetPath);
        }

        // Load saved presets
        LoadPresetsList();

        InitializeStyles();
    }

    void Update()
    {
        // Toggle editor visibility with Tab key
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            showEditor = !showEditor;
        }

        // Update performance metrics
        if (showEditor && showDebugInfo)
        {
            UpdatePerformanceMetrics();
        }

        // Apply any runtime parameter changes that are more efficient to do in Update
        if (interactionStrengthChange != 0)
        {
            simulation.interactionStrength = Mathf.Clamp(simulation.interactionStrength + interactionStrengthChange, 0f, 10f);
            interactionStrengthChange = 0;
        }

        if (interactionRadiusChange != 0)
        {
            simulation.interactionRadius = Mathf.Clamp(simulation.interactionRadius + interactionRadiusChange, 0.1f, 50f);
            interactionRadiusChange = 0;
        }
    }

    void OnGUI()
    {
        if (!showEditor) return;

        InitializeStyles(); // Ensure styles are initialized even after domain reload

        // Draw the main editor window
        editorWindowRect = GUILayout.Window(WINDOW_ID, editorWindowRect, DrawEditorWindow, "GPU Particle System Editor", GUI.skin.window);

        // Make sure window stays within screen bounds
        editorWindowRect.x = Mathf.Clamp(editorWindowRect.x, 0, Screen.width - editorWindowRect.width);
        editorWindowRect.y = Mathf.Clamp(editorWindowRect.y, 0, Screen.height - editorWindowRect.height);
    }

    void DrawEditorWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Space(10);

        // Preset Manager Section (always at top for quick access)
        showPresetManager = DrawFoldoutSection("Preset Manager", showPresetManager, DrawPresetManager);

        GUILayout.Space(10);

        // Simulation Settings Section
        showSimulationSettings = DrawFoldoutSection("Simulation Settings", showSimulationSettings, DrawSimulationSettings);

        GUILayout.Space(10);

        // Particle Types Section
        showParticleTypes = DrawFoldoutSection("Particle Types", showParticleTypes, DrawParticleTypes);

        GUILayout.Space(10);

        // Interaction Matrix Section
        showInteractionMatrix = DrawFoldoutSection("Interaction Matrix", showInteractionMatrix, DrawInteractionMatrix);

        GUILayout.Space(10);

        // Performance Settings Section
        showPerformanceSettings = DrawFoldoutSection("Performance Settings", showPerformanceSettings, DrawPerformanceSettings);

        GUILayout.Space(10);

        // LOD Settings
        showLODSettings = DrawFoldoutSection("LOD Settings", showLODSettings, DrawLODSettings);

        GUILayout.Space(10);

        // Presets Section
        showPresets = DrawFoldoutSection("Pattern Presets", showPresets, DrawPresets);

        GUILayout.Space(10);

        // Debug Information Section
        showDebugInfo = DrawFoldoutSection("Performance Diagnostics", showDebugInfo, DrawDebugInfo);

        GUILayout.Space(20);

        // Controls section at the bottom
        DrawControls();

        GUILayout.EndScrollView();

        // Make the window draggable
        GUI.DragWindow(new Rect(0, 0, editorWindowRect.width, 20));
    }

    #region Draw Section Methods

    void DrawPresetManager()
    {
        GUILayout.BeginVertical(boxStyle);

        // Save current state section
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

        GUILayout.Space(15);

        // Search box
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(60));
        presetSearchTerm = GUILayout.TextField(presetSearchTerm, GUILayout.Width(240));
        if (GUILayout.Button("Clear", GUILayout.Width(60)))
        {
            presetSearchTerm = "";
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Preset List
        GUILayout.Label("Saved Presets", subHeaderStyle);

        if (savedPresets.Count == 0)
        {
            GUILayout.Label("No presets found. Save a preset to see it here.");
        }
        else
        {
            // List header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(150));
            GUILayout.Label("Date", GUILayout.Width(150));
            GUILayout.Label("Actions", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            // Draw scrollable list of presets
            presetScrollPosition = GUILayout.BeginScrollView(presetScrollPosition, GUILayout.Height(140));

            bool isAnyPresetSelected = false;

            for (int i = 0; i < savedPresets.Count; i++)
            {
                var preset = savedPresets[i];

                // Apply search filter
                if (!string.IsNullOrEmpty(presetSearchTerm) &&
                    !preset.name.ToLower().Contains(presetSearchTerm.ToLower()))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();

                // Select button - shows as pressed when selected
                bool isSelected = selectedPresetIndex == i;
                if (isSelected) isAnyPresetSelected = true;

                GUI.backgroundColor = isSelected ? buttonColor : Color.white;
                if (GUILayout.Toggle(isSelected, preset.name, "Button", GUILayout.Width(150)))
                {
                    if (!isSelected)
                    {
                        selectedPresetIndex = i;
                    }
                }
                else if (isSelected)
                {
                    selectedPresetIndex = -1;
                }
                GUI.backgroundColor = Color.white;

                // Date saved
                GUILayout.Label(preset.date, GUILayout.Width(150));

                // Delete button
                GUI.backgroundColor = deleteButtonColor;
                if (GUILayout.Button("Delete", GUILayout.Width(60)))
                {
                    if (EditorConfirmDialog("Delete Preset", $"Are you sure you want to delete preset '{preset.name}'?"))
                    {
                        DeletePreset(i);
                        if (selectedPresetIndex == i)
                        {
                            selectedPresetIndex = -1;
                        }
                        break; // Exit loop after modifying the collection
                    }
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            // Load preset button
            GUILayout.BeginHorizontal();

            if (isAnyPresetSelected)
            {
                if (GUILayout.Button("Load Selected Preset", buttonStyle))
                {
                    if (EditorConfirmDialog("Load Preset", "This will replace your current settings. Continue?"))
                    {
                        LoadPreset(selectedPresetIndex);
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("Select a preset to load", buttonStyle);
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    void DrawSimulationSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Simulation Parameters", subHeaderStyle);
        GUILayout.Space(5);

        // Start particles at center
        GUILayout.BeginHorizontal();
        simulation.startParticlesAtCenter = GUILayout.Toggle(simulation.startParticlesAtCenter, "Start Particles At Center");
        GUILayout.EndHorizontal();

        // Simulation speed
        GUILayout.BeginHorizontal();
        GUILayout.Label("Simulation Speed:", GUILayout.Width(150));
        simulation.simulationSpeed = GUILayout.HorizontalSlider(simulation.simulationSpeed, 0f, 5f, GUILayout.Width(200));
        GUILayout.Label(simulation.simulationSpeed.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Elasticity
        GUILayout.BeginHorizontal();
        GUILayout.Label("Collision Elasticity:", GUILayout.Width(150));
        simulation.collisionElasticity = GUILayout.HorizontalSlider(simulation.collisionElasticity, 0f, 1f, GUILayout.Width(200));
        GUILayout.Label(simulation.collisionElasticity.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Dampening
        GUILayout.BeginHorizontal();
        GUILayout.Label("Dampening:", GUILayout.Width(150));
        simulation.dampening = GUILayout.HorizontalSlider(simulation.dampening, 0.5f, 1f, GUILayout.Width(200));
        GUILayout.Label(simulation.dampening.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Interaction Strength with +/- buttons
        GUILayout.BeginHorizontal();
        GUILayout.Label("Interaction Strength:", GUILayout.Width(150));
        if (GUILayout.Button("-", GUILayout.Width(25)))
        {
            interactionStrengthChange = -0.1f;
        }
        simulation.interactionStrength = GUILayout.HorizontalSlider(simulation.interactionStrength, 0f, 10f, GUILayout.Width(150));
        if (GUILayout.Button("+", GUILayout.Width(25)))
        {
            interactionStrengthChange = 0.1f;
        }
        GUILayout.Label(simulation.interactionStrength.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Min Distance
        GUILayout.BeginHorizontal();
        GUILayout.Label("Min Distance:", GUILayout.Width(150));
        simulation.minDistance = GUILayout.HorizontalSlider(simulation.minDistance, 0.01f, 5f, GUILayout.Width(200));
        GUILayout.Label(simulation.minDistance.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Bounce Force
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bounce Force:", GUILayout.Width(150));
        simulation.bounceForce = GUILayout.HorizontalSlider(simulation.bounceForce, 0f, 1f, GUILayout.Width(200));
        GUILayout.Label(simulation.bounceForce.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Max Force
        GUILayout.BeginHorizontal();
        GUILayout.Label("Max Force:", GUILayout.Width(150));
        simulation.maxForce = GUILayout.HorizontalSlider(simulation.maxForce, 1f, 1000f, GUILayout.Width(200));
        GUILayout.Label(simulation.maxForce.ToString("F0"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Max Velocity
        GUILayout.BeginHorizontal();
        GUILayout.Label("Max Velocity:", GUILayout.Width(150));
        simulation.maxVelocity = GUILayout.HorizontalSlider(simulation.maxVelocity, 1f, 100f, GUILayout.Width(200));
        GUILayout.Label(simulation.maxVelocity.ToString("F0"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Interaction Radius with +/- buttons
        GUILayout.BeginHorizontal();
        GUILayout.Label("Interaction Radius:", GUILayout.Width(150));
        if (GUILayout.Button("-", GUILayout.Width(25)))
        {
            interactionRadiusChange = -1f;
        }
        simulation.interactionRadius = GUILayout.HorizontalSlider(simulation.interactionRadius, 1f, 50f, GUILayout.Width(150));
        if (GUILayout.Button("+", GUILayout.Width(25)))
        {
            interactionRadiusChange = 1f;
        }
        GUILayout.Label(simulation.interactionRadius.ToString("F1"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("Simulation Bounds", subHeaderStyle);

        // X Bound
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bounds X:", GUILayout.Width(150));
        float newBoundsX = float.Parse(GUILayout.TextField(simulation.simulationBounds.x.ToString("F1"), GUILayout.Width(60)));
        float boundsDiffX = GUILayout.HorizontalSlider(simulation.simulationBounds.x, 1f, 100f, GUILayout.Width(140));
        if (Mathf.Abs(boundsDiffX - simulation.simulationBounds.x) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.x = boundsDiffX;
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        else if (Mathf.Abs(newBoundsX - simulation.simulationBounds.x) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.x = Mathf.Max(1f, newBoundsX);
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.EndHorizontal();

        // Y Bound
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bounds Y:", GUILayout.Width(150));
        float newBoundsY = float.Parse(GUILayout.TextField(simulation.simulationBounds.y.ToString("F1"), GUILayout.Width(60)));
        float boundsDiffY = GUILayout.HorizontalSlider(simulation.simulationBounds.y, 1f, 100f, GUILayout.Width(140));
        if (Mathf.Abs(boundsDiffY - simulation.simulationBounds.y) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.y = boundsDiffY;
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        else if (Mathf.Abs(newBoundsY - simulation.simulationBounds.y) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.y = Mathf.Max(1f, newBoundsY);
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.EndHorizontal();

        // Z Bound
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bounds Z:", GUILayout.Width(150));
        float newBoundsZ = float.Parse(GUILayout.TextField(simulation.simulationBounds.z.ToString("F1"), GUILayout.Width(60)));
        float boundsDiffZ = GUILayout.HorizontalSlider(simulation.simulationBounds.z, 1f, 100f, GUILayout.Width(140));
        if (Mathf.Abs(boundsDiffZ - simulation.simulationBounds.z) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.z = boundsDiffZ;
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        else if (Mathf.Abs(newBoundsZ - simulation.simulationBounds.z) > 0.01f)
        {
            Vector3 newBounds = simulation.simulationBounds;
            newBounds.z = Mathf.Max(1f, newBoundsZ);
            simulation.simulationBounds = newBounds;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.EndHorizontal();

        // Reset bounds button
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Bounds to Default (10,10,10)", GUILayout.Width(250)))
        {
            simulation.simulationBounds = new Vector3(10f, 10f, 10f);
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.EndHorizontal();

        // Add a section for quick simulation speed control
        GUILayout.Space(15);
        GUILayout.Label("Quick Controls", subHeaderStyle);

        // Quick speed controls
        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed Control:", GUILayout.Width(120));
        if (GUILayout.Button("0.25x", GUILayout.Width(60)))
        {
            simulation.simulationSpeed = 0.25f;
        }
        if (GUILayout.Button("0.5x", GUILayout.Width(60)))
        {
            simulation.simulationSpeed = 0.5f;
        }
        if (GUILayout.Button("1x", GUILayout.Width(60)))
        {
            simulation.simulationSpeed = 1f;
        }
        if (GUILayout.Button("2x", GUILayout.Width(60)))
        {
            simulation.simulationSpeed = 2f;
        }
        if (GUILayout.Button("4x", GUILayout.Width(60)))
        {
            simulation.simulationSpeed = 4f;
        }
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    void DrawParticleTypes()
    {
        GUILayout.BeginVertical(boxStyle);

        // Particle Types List
        GUILayout.Label("Current Particle Types", subHeaderStyle);
        GUILayout.Space(5);

        if (simulation.particleTypes.Count == 0)
        {
            GUILayout.Label("No particle types defined.");
        }
        else
        {
            // Headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(100));
            GUILayout.Label("Color", GUILayout.Width(70));
            GUILayout.Label("Mass", GUILayout.Width(70));
            GUILayout.Label("Radius", GUILayout.Width(70));
            GUILayout.Label("Count", GUILayout.Width(70));
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // List each particle type
            for (int i = 0; i < simulation.particleTypes.Count; i++)
            {
                var type = simulation.particleTypes[i];

                GUILayout.BeginHorizontal();

                // Name field
                string newName = GUILayout.TextField(type.name, GUILayout.Width(100));
                if (newName != type.name)
                {
                    type.name = newName;
                }

                // Color field (show color as background)
                GUI.backgroundColor = type.color;
                if (GUILayout.Button("", GUILayout.Width(30), GUILayout.Height(18)))
                {
                    // We can't show a color picker in OnGUI, so we'll just cycle through some preset colors
                    type.color = CycleColor(type.color);
                }
                GUI.backgroundColor = Color.white;

                // Basic color controls (RGB)
                GUILayout.Label("", GUILayout.Width(5));

                // Mass field
                float newMass = float.Parse(GUILayout.TextField(type.mass.ToString("F1"), GUILayout.Width(40)));
                if (Mathf.Abs(newMass - type.mass) > 0.01f)
                {
                    type.mass = Mathf.Max(0.1f, newMass);
                }

                // Radius field
                float newRadius = float.Parse(GUILayout.TextField(type.radius.ToString("F1"), GUILayout.Width(40)));
                if (Mathf.Abs(newRadius - type.radius) > 0.01f)
                {
                    type.radius = Mathf.Max(0.1f, newRadius);
                }

                // Spawn amount field
                float newSpawnAmount = float.Parse(GUILayout.TextField(type.spawnAmount.ToString("F0"), GUILayout.Width(40)));
                if (Mathf.Abs(newSpawnAmount - type.spawnAmount) > 0.1f)
                {
                    type.spawnAmount = Mathf.Max(1f, newSpawnAmount);
                    if (Application.isPlaying)
                    {
                        simulation.UpdateParticleCount(i, newSpawnAmount);
                    }
                }

                // Delete button
                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    if (simulation.particleTypes.Count > 1) // Don't delete the last type
                    {
                        simulation.particleTypes.RemoveAt(i);
                        if (Application.isPlaying)
                        {
                            simulation.RequestReset();
                        }
                        break; // Exit the loop as we modified the collection
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
        }

        // Add new type section
        GUILayout.Label("Add New Particle Type", subHeaderStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(60));
        newTypeName = GUILayout.TextField(newTypeName, GUILayout.Width(100));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Color:", GUILayout.Width(60));
        GUI.backgroundColor = newTypeColor;
        if (GUILayout.Button("", GUILayout.Width(30), GUILayout.Height(18)))
        {
            newTypeColor = CycleColor(newTypeColor);
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Mass:", GUILayout.Width(60));
        newTypeMass = float.Parse(GUILayout.TextField(newTypeMass.ToString("F1"), GUILayout.Width(60)));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Radius:", GUILayout.Width(60));
        newTypeRadius = float.Parse(GUILayout.TextField(newTypeRadius.ToString("F1"), GUILayout.Width(60)));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Count:", GUILayout.Width(60));
        newTypeSpawnAmount = float.Parse(GUILayout.TextField(newTypeSpawnAmount.ToString("F0"), GUILayout.Width(60)));
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Add Particle Type", buttonStyle))
        {
            AddNewParticleType();
        }

        GUILayout.EndVertical();
    }

    void DrawInteractionMatrix()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Interaction Matrix", subHeaderStyle);
        GUILayout.Space(5);

        if (simulation.particleTypes.Count == 0)
        {
            GUILayout.Label("No particle types to display matrix.");
        }
        else
        {
            // Create a dictionary for quick lookup
            UpdateInteractionDictionary();

            // Display matrix legend
            GUILayout.BeginHorizontal();
            GUILayout.Label("Red: Repulsion", GUILayout.Width(100));
            GUILayout.Label("White: Neutral", GUILayout.Width(100));
            GUILayout.Label("Green: Attraction", GUILayout.Width(130));
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Matrix header row for column labels
            GUILayout.BeginHorizontal();
            GUILayout.Label("Effect of ↓ on →", boldLabelStyle, GUILayout.Width(120));

            for (int i = 0; i < simulation.particleTypes.Count; i++)
            {
                // Display each particle type name as column header
                GUI.backgroundColor = simulation.particleTypes[i].color;
                GUILayout.Label(simulation.particleTypes[i].name, matrixCellStyle, GUILayout.Width(MATRIX_CELL_SIZE), GUILayout.Height(MATRIX_CELL_SIZE));
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndHorizontal();

            // Matrix rows
            for (int i = 0; i < simulation.particleTypes.Count; i++)
            {
                GUILayout.BeginHorizontal();

                // Row header
                GUI.backgroundColor = simulation.particleTypes[i].color;
                GUILayout.Label(simulation.particleTypes[i].name, matrixCellStyle, GUILayout.Width(120), GUILayout.Height(MATRIX_CELL_SIZE));
                GUI.backgroundColor = Color.white;

                // Cells
                for (int j = 0; j < simulation.particleTypes.Count; j++)
                {
                    // Get interaction value or default to 0
                    float value = 0;
                    interactionValues.TryGetValue((i, j), out value);

                    // Display value with color based on attraction/repulsion
                    DisplayMatrixCell(i, j, value);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            // Matrix controls
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Matrix", GUILayout.Width(120)))
            {
                simulation.interactionRules.Clear();
                if (Application.isPlaying)
                {
                    simulation.RequestReset();
                }
            }

            if (GUILayout.Button("Randomize", GUILayout.Width(120)))
            {
                RandomizeInteractions();
            }

            if (matrixGenerator != null && GUILayout.Button("Generate Pattern", GUILayout.Width(150)))
            {
                matrixGenerator.GenerateMatrix();
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    void DrawPerformanceSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Performance Parameters", subHeaderStyle);
        GUILayout.Space(5);

        // Cell Size
        GUILayout.BeginHorizontal();
        GUILayout.Label("Cell Size:", GUILayout.Width(150));
        float newCellSize = GUILayout.HorizontalSlider(simulation.cellSize, 0.1f, 10f, GUILayout.Width(200));
        if (Mathf.Abs(newCellSize - simulation.cellSize) > 0.01f)
        {
            simulation.cellSize = newCellSize;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.Label(simulation.cellSize.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Thread Group Size
        GUILayout.BeginHorizontal();
        GUILayout.Label("Thread Group Size:", GUILayout.Width(150));
        int newThreadGroupSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(simulation.threadGroupSize, 1, 8, GUILayout.Width(200)));
        if (newThreadGroupSize != simulation.threadGroupSize)
        {
            simulation.threadGroupSize = newThreadGroupSize;
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }
        GUILayout.Label(simulation.threadGroupSize.ToString(), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Enable LOD
        GUILayout.BeginHorizontal();
        bool newEnableLOD = GUILayout.Toggle(simulation.enableLOD, "Enable Level of Detail (LOD)");
        if (newEnableLOD != simulation.enableLOD)
        {
            simulation.enableLOD = newEnableLOD;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Debug visualization toggles
        GUILayout.BeginHorizontal();
        simulation.debugDrawCells = GUILayout.Toggle(simulation.debugDrawCells, "Debug Draw Cells");
        simulation.debugDrawParticles = GUILayout.Toggle(simulation.debugDrawParticles, "Debug Draw Particles");
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    void DrawLODSettings()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Level of Detail Settings", subHeaderStyle);
        GUILayout.Space(5);

        GUI.enabled = simulation.enableLOD;

        // LOD Levels
        GUILayout.BeginHorizontal();
        GUILayout.Label("LOD Levels:", GUILayout.Width(150));
        simulation.lodLevels = Mathf.RoundToInt(GUILayout.HorizontalSlider(simulation.lodLevels, 0, 3, GUILayout.Width(200)));
        GUILayout.Label(simulation.lodLevels.ToString(), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        // Dynamic LOD
        GUILayout.BeginHorizontal();
        simulation.dynamicLOD = GUILayout.Toggle(simulation.dynamicLOD, "Dynamic LOD (adjust based on performance)");
        GUILayout.EndHorizontal();

        if (simulation.dynamicLOD)
        {
            // Target FPS
            GUILayout.BeginHorizontal();
            GUILayout.Label("Target FPS:", GUILayout.Width(150));
            simulation.targetFPS = GUILayout.HorizontalSlider(simulation.targetFPS, 30f, 144f, GUILayout.Width(200));
            GUILayout.Label(simulation.targetFPS.ToString("F0"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // LOD Adjust Speed
            GUILayout.BeginHorizontal();
            GUILayout.Label("Adjust Speed:", GUILayout.Width(150));
            simulation.lodAdjustSpeed = GUILayout.HorizontalSlider(simulation.lodAdjustSpeed, 0.1f, 1f, GUILayout.Width(200));
            GUILayout.Label(simulation.lodAdjustSpeed.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();
        }

        // Current LOD Factor display
        GUILayout.BeginHorizontal();
        GUILayout.Label("Current LOD Factor:", GUILayout.Width(150));
        GUILayout.Label(simulation.currentLODFactor.ToString("F2"), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUI.enabled = true;

        GUILayout.EndVertical();
    }

    void DrawPresets()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Pattern Presets", subHeaderStyle);
        GUILayout.Space(5);

        bool hasMatrixGenerator = matrixGenerator != null;

        if (!hasMatrixGenerator)
        {
            GUILayout.Label("GPUInteractionMatrixGenerator not found.\nPresets require this component.");
        }
        else
        {
            // Pattern Type
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pattern Type:", GUILayout.Width(150));

            int patternTypeIndex = (int)matrixGenerator.patternType;
            string[] patternNames = System.Enum.GetNames(typeof(GPUInteractionMatrixGenerator.PatternType));

            patternTypeIndex = GUILayout.SelectionGrid(patternTypeIndex, patternNames, 2);

            if (patternTypeIndex != (int)matrixGenerator.patternType)
            {
                matrixGenerator.patternType = (GPUInteractionMatrixGenerator.PatternType)patternTypeIndex;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Pattern settings
            GUILayout.BeginHorizontal();
            matrixGenerator.generateParticleTypes = GUILayout.Toggle(matrixGenerator.generateParticleTypes, "Generate Particle Types");
            matrixGenerator.applyRecommendedSettings = GUILayout.Toggle(matrixGenerator.applyRecommendedSettings, "Apply Recommended Settings");
            GUILayout.EndHorizontal();

            // Matrix configuration sliders
            GUILayout.Label("Matrix Configuration", subHeaderStyle);

            // Attraction Bias
            GUILayout.BeginHorizontal();
            GUILayout.Label("Attraction Bias:", GUILayout.Width(150));
            matrixGenerator.attractionBias = GUILayout.HorizontalSlider(matrixGenerator.attractionBias, -1f, 1f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.attractionBias.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Symmetry Factor
            GUILayout.BeginHorizontal();
            GUILayout.Label("Symmetry Factor:", GUILayout.Width(150));
            matrixGenerator.symmetryFactor = GUILayout.HorizontalSlider(matrixGenerator.symmetryFactor, 0f, 1f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.symmetryFactor.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Sparsity
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sparsity:", GUILayout.Width(150));
            matrixGenerator.sparsity = GUILayout.HorizontalSlider(matrixGenerator.sparsity, 0f, 1f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.sparsity.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Noise Factor
            GUILayout.BeginHorizontal();
            GUILayout.Label("Noise Factor:", GUILayout.Width(150));
            matrixGenerator.noiseFactor = GUILayout.HorizontalSlider(matrixGenerator.noiseFactor, 0f, 1f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.noiseFactor.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Particle Scaling
            GUILayout.Label("Particle Scaling", subHeaderStyle);

            // Spawn Multiplier
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Multiplier:", GUILayout.Width(150));
            matrixGenerator.particleSpawnMultiplier = GUILayout.HorizontalSlider(matrixGenerator.particleSpawnMultiplier, 0.1f, 500f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.particleSpawnMultiplier.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Radius Multiplier
            GUILayout.BeginHorizontal();
            GUILayout.Label("Radius Multiplier:", GUILayout.Width(150));
            matrixGenerator.particleRadiusMultiplier = GUILayout.HorizontalSlider(matrixGenerator.particleRadiusMultiplier, 0.1f, 3f, GUILayout.Width(200));
            GUILayout.Label(matrixGenerator.particleRadiusMultiplier.ToString("F2"), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // Generate button
            if (GUILayout.Button("Generate Pattern", buttonStyle))
            {
                matrixGenerator.GenerateMatrix();
            }
        }

        GUILayout.EndVertical();
    }

    void DrawDebugInfo()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Performance Metrics", subHeaderStyle);

        // Particle counts
        GUILayout.BeginHorizontal();
        GUILayout.Label("Particle Count:", GUILayout.Width(150));
        GUILayout.Label(simulation.particleCount.ToString("N0"), GUILayout.Width(100));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Active Particles:", GUILayout.Width(150));
        GUILayout.Label(simulation.activeParticleCount.ToString("N0"), GUILayout.Width(100));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Grid Cells:", GUILayout.Width(150));
        GUILayout.Label(simulation.gridCellCount.ToString("N0"), GUILayout.Width(100));
        GUILayout.EndHorizontal();

        // Draw frame time graph
        GUILayout.Space(10);
        GUILayout.Label("Frame Time (ms)", subHeaderStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Current:", GUILayout.Width(80));
        GUILayout.Label(Time.deltaTime * 1000f + " ms", GUILayout.Width(60));
        GUILayout.Label("(" + (1.0f / Mathf.Max(0.001f, Time.deltaTime)).ToString("F1") + " FPS)", GUILayout.Width(80));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Average:", GUILayout.Width(80));
        GUILayout.Label(frameTimeAvg + " ms", GUILayout.Width(60));
        GUILayout.Label("(" + (1.0f / Mathf.Max(0.001f, frameTimeAvg / 1000f)).ToString("F1") + " FPS)", GUILayout.Width(80));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Min/Max:", GUILayout.Width(80));
        GUILayout.Label(frameTimeMin + "/" + frameTimeMax + " ms", GUILayout.Width(100));
        GUILayout.EndHorizontal();

        // Reset simulation button
        GUILayout.Space(10);
        if (GUILayout.Button("Reset Simulation", buttonStyle))
        {
            if (Application.isPlaying)
            {
                simulation.RequestReset();
            }
        }

        GUILayout.EndVertical();
    }

    void DrawControls()
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
                simulation.RequestReset();
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

    /// <summary>
    /// Creates a foldout section with a header and content
    /// </summary>
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

    /// <summary>
    /// Initialize all the custom GUI styles
    /// </summary>
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

            // Matrix cell style
            matrixCellStyle = new GUIStyle(GUI.skin.box);
            matrixCellStyle.alignment = TextAnchor.MiddleCenter;
            matrixCellStyle.normal.textColor = Color.black;
            matrixCellStyle.fontSize = 10;
            matrixCellStyle.wordWrap = false;
            matrixCellStyle.clipping = TextClipping.Clip;

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

            // Matrix cell value style
            cellValueStyle = new GUIStyle(GUI.skin.textField);
            cellValueStyle.alignment = TextAnchor.MiddleCenter;
            cellValueStyle.fontSize = 9;

            // Preset button style
            presetButtonStyle = new GUIStyle(GUI.skin.button);
            presetButtonStyle.fixedHeight = 24;
            presetButtonStyle.fontSize = 11;
        }
    }

    /// <summary>
    /// Create a texture of specified color
    /// </summary>
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

    /// <summary>
    /// Update performance metrics
    /// </summary>
    private void UpdatePerformanceMetrics()
    {
        float currentFrameTime = Time.unscaledDeltaTime * 1000f; // ms

        // Add to ring buffer
        frameTimes[frameTimeIndex] = currentFrameTime;
        frameTimeIndex = (frameTimeIndex + 1) % frameTimes.Length;

        // Calculate stats
        float sum = 0;
        frameTimeMin = float.MaxValue;
        frameTimeMax = 0;

        for (int i = 0; i < frameTimes.Length; i++)
        {
            float time = frameTimes[i];
            if (time > 0) // Skip uninitialized values
            {
                sum += time;
                frameTimeMin = Mathf.Min(frameTimeMin, time);
                frameTimeMax = Mathf.Max(frameTimeMax, time);
            }
        }

        // Calculate average
        int validFrames = 0;
        for (int i = 0; i < frameTimes.Length; i++)
        {
            if (frameTimes[i] > 0) validFrames++;
        }

        if (validFrames > 0)
        {
            frameTimeAvg = sum / validFrames;
        }
    }

    /// <summary>
    /// Simple color cycling for particle types
    /// </summary>
    private Color CycleColor(Color currentColor)
    {
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
            Color.white,
            new Color(0.5f, 0.5f, 0.5f) // Gray
        };

        // Find closest current color and cycle to next
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

    /// <summary>
    /// Calculate simple distance between colors
    /// </summary>
    private float ColorDistance(Color a, Color b)
    {
        return Mathf.Sqrt(
            Mathf.Pow(a.r - b.r, 2) +
            Mathf.Pow(a.g - b.g, 2) +
            Mathf.Pow(a.b - b.b, 2)
        );
    }

    /// <summary>
    /// Add a new particle type with the current settings
    /// </summary>
    private void AddNewParticleType()
    {
        var newType = new GPUParticleSimulation.ParticleType
        {
            name = newTypeName,
            color = newTypeColor,
            mass = Mathf.Max(0.1f, newTypeMass),
            radius = Mathf.Max(0.1f, newTypeRadius),
            spawnAmount = Mathf.Max(1f, newTypeSpawnAmount)
        };

        simulation.particleTypes.Add(newType);

        // Reset for next use
        newTypeName = "New Type " + simulation.particleTypes.Count;
        newTypeColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);
        newTypeMass = 1.0f;
        newTypeRadius = 0.5f;
        newTypeSpawnAmount = 50f;

        if (Application.isPlaying)
        {
            simulation.RequestReset();
        }
    }

    /// <summary>
    /// Update interaction dictionary for matrix visualization
    /// </summary>
    private void UpdateInteractionDictionary()
    {
        interactionValues.Clear();

        foreach (var rule in simulation.interactionRules)
        {
            interactionValues[(rule.typeIndexA, rule.typeIndexB)] = rule.attractionValue;
        }
    }

    /// <summary>
    /// Display a matrix cell with color based on value
    /// </summary>
    private void DisplayMatrixCell(int i, int j, float value)
    {
        // Calculate color
        Color cellColor = Color.white; // Neutral

        if (value > 0)
        {
            // Green for attraction
            cellColor = Color.Lerp(Color.white, colorLegend[4], Mathf.Min(1f, value));
        }
        else if (value < 0)
        {
            // Red for repulsion
            cellColor = Color.Lerp(Color.white, colorLegend[0], Mathf.Min(1f, Mathf.Abs(value)));
        }

        // Background color for the cell
        GUI.backgroundColor = cellColor;

        // Display the value with a slider instead of a text field
        float newValue = GUILayout.HorizontalSlider(value, -1f, 1f, GUILayout.Width(MATRIX_CELL_SIZE));

        if (Mathf.Abs(newValue - value) > 0.01f)
        {
            // Value changed, update the simulation
            if (Application.isPlaying)
            {
                simulation.UpdateInteractionRule(i, j, newValue);
            }
            else
            {
                UpdateInteractionRule(i, j, newValue);
            }
        }

        GUI.backgroundColor = Color.white;
    }

    /// <summary>
    /// Update interaction rule (editor-only version)
    /// </summary>
    private void UpdateInteractionRule(int typeA, int typeB, float value)
    {
        // Find existing rule
        bool found = false;

        for (int i = 0; i < simulation.interactionRules.Count; i++)
        {
            var rule = simulation.interactionRules[i];
            if (rule.typeIndexA == typeA && rule.typeIndexB == typeB)
            {
                rule.attractionValue = value;
                simulation.interactionRules[i] = rule;
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Create new rule
            simulation.interactionRules.Add(new GPUParticleSimulation.InteractionRule
            {
                typeIndexA = typeA,
                typeIndexB = typeB,
                attractionValue = value
            });
        }

        // Update the dictionary
        interactionValues[(typeA, typeB)] = value;
    }

    /// <summary>
    /// Randomize the interaction matrix
    /// </summary>
    private void RandomizeInteractions()
    {
        simulation.interactionRules.Clear();

        for (int i = 0; i < simulation.particleTypes.Count; i++)
        {
            for (int j = 0; j < simulation.particleTypes.Count; j++)
            {
                // Skip self-interactions (optional)
                // if (i == j) continue;

                float value = UnityEngine.Random.Range(-1f, 1f);

                simulation.interactionRules.Add(new GPUParticleSimulation.InteractionRule
                {
                    typeIndexA = i,
                    typeIndexB = j,
                    attractionValue = value
                });
            }
        }

        if (Application.isPlaying)
        {
            simulation.RequestReset();
        }
    }

    #endregion

    #region Preset Management

    /// <summary>
    /// Display a simple confirmation dialog
    /// </summary>
    private bool EditorConfirmDialog(string title, string message)
    {
        // Simple implementation using two buttons
        float width = 300;
        float height = 150;

        Rect windowRect = new Rect(
            (Screen.width - width) / 2,
            (Screen.height - height) / 2,
            width, height
        );

        // Store current editor visibility to restore after dialog
        bool wasVisible = showEditor;
        showEditor = false;

        bool confirmed = false;
        bool dialogActive = true;

        while (dialogActive)
        {
            // Process events to keep UI responsive
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                dialogActive = false;
            }

            windowRect = GUI.Window(9999, windowRect, (id) =>
            {
                GUILayout.BeginVertical();

                GUILayout.Space(10);
                GUILayout.Label(title, subHeaderStyle);
                GUILayout.Space(10);

                GUILayout.Label(message);

                GUILayout.Space(20);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Yes", GUILayout.Height(30)))
                {
                    confirmed = true;
                    dialogActive = false;
                }

                if (GUILayout.Button("No", GUILayout.Height(30)))
                {
                    dialogActive = false;
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();

                // Make the window draggable
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
            }, "Confirm");

            // If the dialog is no longer active, break the loop
            if (!dialogActive)
            {
                break;
            }

            // Give up control to Unity
            System.Threading.Thread.Sleep(10);
        }

        // Restore editor visibility
        showEditor = wasVisible;

        return confirmed;
    }

    /// <summary>
    /// Saves the current simulation state as a preset
    /// </summary>
    private void SaveCurrentAsPreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            Debug.LogWarning("Preset name cannot be empty.");
            return;
        }

        SimulationPreset preset = new SimulationPreset
        {
            name = presetName,
            date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),

            // Simulation settings
            simulationSpeed = simulation.simulationSpeed,
            collisionElasticity = simulation.collisionElasticity,
            simulationBounds = simulation.simulationBounds,
            dampening = simulation.dampening,
            interactionStrength = simulation.interactionStrength,
            minDistance = simulation.minDistance,
            bounceForce = simulation.bounceForce,
            maxForce = simulation.maxForce,
            maxVelocity = simulation.maxVelocity,
            interactionRadius = simulation.interactionRadius,
            cellSize = simulation.cellSize
        };

        // Serialize particle types
        foreach (var type in simulation.particleTypes)
        {
            preset.particleTypes.Add(new SimulationPreset.SerializedParticleType(type));
        }

        // Serialize interaction rules
        foreach (var rule in simulation.interactionRules)
        {
            preset.interactionRules.Add(new SimulationPreset.SerializedInteractionRule(rule));
        }

        // Save to file with JSON
        string presetJson = JsonUtility.ToJson(preset, true);
        string filePath = Path.Combine(presetPath, SanitizeFileName(presetName) + ".json");

        try
        {
            File.WriteAllText(filePath, presetJson);
            Debug.Log("Preset saved: " + filePath);

            // Reload presets
            LoadPresetsList();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to save preset: " + e.Message);
        }

        // Generate a new default name for the next preset
        newPresetName = "My Preset " + DateTime.Now.ToString("MMdd_HHmm");
    }

    /// <summary>
    /// Load a preset from saved presets
    /// </summary>
    private void LoadPreset(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= savedPresets.Count)
        {
            return;
        }

        SimulationPreset preset = savedPresets[presetIndex];

        // Apply simulation settings
        simulation.simulationSpeed = preset.simulationSpeed;
        simulation.collisionElasticity = preset.collisionElasticity;
        simulation.simulationBounds = preset.simulationBounds;
        simulation.dampening = preset.dampening;
        simulation.interactionStrength = preset.interactionStrength;
        simulation.minDistance = preset.minDistance;
        simulation.bounceForce = preset.bounceForce;
        simulation.maxForce = preset.maxForce;
        simulation.maxVelocity = preset.maxVelocity;
        simulation.interactionRadius = preset.interactionRadius;
        simulation.cellSize = preset.cellSize;

        // Apply particle types
        simulation.particleTypes.Clear();
        foreach (var serializedType in preset.particleTypes)
        {
            simulation.particleTypes.Add(serializedType.ToParticleType());
        }

        // Apply interaction rules
        simulation.interactionRules.Clear();
        foreach (var serializedRule in preset.interactionRules)
        {
            simulation.interactionRules.Add(serializedRule.ToInteractionRule());
        }

        // Reset simulation to apply changes
        if (Application.isPlaying)
        {
            simulation.RequestReset();
        }

        Debug.Log("Preset loaded: " + preset.name);
    }

    /// <summary>
    /// Delete a preset
    /// </summary>
    private void DeletePreset(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= savedPresets.Count)
        {
            return;
        }

        SimulationPreset preset = savedPresets[presetIndex];
        string filePath = Path.Combine(presetPath, SanitizeFileName(preset.name) + ".json");

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.Log("Preset deleted: " + preset.name);
            }

            // Remove from list
            savedPresets.RemoveAt(presetIndex);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to delete preset: " + e.Message);
        }
    }

    /// <summary>
    /// Load the list of available presets
    /// </summary>
    private void LoadPresetsList()
    {
        savedPresets.Clear();

        if (!Directory.Exists(presetPath))
        {
            Directory.CreateDirectory(presetPath);
            return;
        }

        // Find all JSON files in the presets directory
        string[] files = Directory.GetFiles(presetPath, "*.json");

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                SimulationPreset preset = JsonUtility.FromJson<SimulationPreset>(json);

                if (preset != null)
                {
                    savedPresets.Add(preset);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Failed to load preset from file " + file + ": " + e.Message);
            }
        }

        // Sort presets by date (newest first)
        savedPresets.Sort((a, b) => DateTime.Parse(b.date).CompareTo(DateTime.Parse(a.date)));

        Debug.Log("Loaded " + savedPresets.Count + " presets.");
    }

    /// <summary>
    /// Sanitize filename to be safe for file system
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        // Remove invalid characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Ensure the name is not empty
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Preset_" + DateTime.Now.Ticks;
        }

        return sanitized;
    }

    #endregion
}