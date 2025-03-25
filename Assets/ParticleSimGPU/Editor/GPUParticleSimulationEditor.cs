using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

#if UNITY_EDITOR
[CustomEditor(typeof(GPUParticleSimulation))]
public class GPUParticleSimulationEditor : Editor
{
    private bool showInteractionMatrix = false;
    private bool showPerformanceSettings = false;
    private bool showRenderingSettings = false;
    private bool showDebugInfo = false;
    private bool showLODSettings = false;

    // Performance monitoring
    private float lastFrameTime = 0f;
    private float avgFrameTime = 0f;
    private float minFrameTime = float.MaxValue;
    private float maxFrameTime = 0f;
    private int frameCount = 0;
    private readonly int frameWindow = 60; // Number of frames to average
    private readonly Queue<float> frameTimes = new Queue<float>();

    public override void OnInspectorGUI()
    {
        GPUParticleSimulation simulation = (GPUParticleSimulation)target;

        // Check that required assets are assigned
        CheckRequiredAssets(simulation);

        // Draw default inspector for most properties
        serializedObject.Update();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("GPU Particle Simulation", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Scale to millions of particles with GPU acceleration", MessageType.Info);

        EditorGUILayout.Space(5);

        DrawComputeResourcesSection(simulation);

        EditorGUILayout.Space(10);

        DrawSimulationSettingsSection(simulation);

        EditorGUILayout.Space(10);

        // Performance settings foldout
        DrawPerformanceSection(simulation);

        EditorGUILayout.Space(10);

        // LOD Settings foldout
        DrawLODSection(simulation);

        EditorGUILayout.Space(10);

        // Rendering settings foldout
        DrawRenderingSection(simulation);

        EditorGUILayout.Space(10);

        // Debug information foldout
        if (Application.isPlaying)
        {
            DrawDebugSection(simulation);
        }

        EditorGUILayout.Space(10);

        // Custom matrix editor for interactions
        DrawInteractionMatrixSection(simulation);

        // Apply changes
        if (GUI.changed)
        {
            EditorUtility.SetDirty(simulation);
        }

        // Force repaint for live updates
        if (Application.isPlaying && showDebugInfo)
        {
            Repaint();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void CheckRequiredAssets(GPUParticleSimulation simulation)
    {
        if (simulation.simulationShader == null)
        {
            EditorGUILayout.HelpBox("Please assign a Compute Shader to run the simulation", MessageType.Error);
        }

        if (simulation.particleMesh == null)
        {
            EditorGUILayout.HelpBox("Please assign a Mesh for particle rendering", MessageType.Error);
        }
    }

    private void DrawComputeResourcesSection(GPUParticleSimulation simulation)
    {
        EditorGUILayout.LabelField("Compute Resources", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        simulation.simulationShader = (ComputeShader)EditorGUILayout.ObjectField("Simulation Shader", simulation.simulationShader, typeof(ComputeShader), false);
        if (EditorGUI.EndChangeCheck() && Application.isPlaying)
        {
            simulation.RequestReset();
        }

        EditorGUI.BeginChangeCheck();
        simulation.particleMesh = (Mesh)EditorGUILayout.ObjectField("Particle Mesh", simulation.particleMesh, typeof(Mesh), false);
        if (EditorGUI.EndChangeCheck() && Application.isPlaying)
        {
            simulation.RequestReset();
        }

        EditorGUI.BeginChangeCheck();
        simulation.particleShader = (Shader)EditorGUILayout.ObjectField("Particle Shader", simulation.particleShader, typeof(Shader), false);
        if (EditorGUI.EndChangeCheck() && Application.isPlaying)
        {
            simulation.RequestReset();
        }
    }

    private void DrawSimulationSettingsSection(GPUParticleSimulation simulation)
    {
        EditorGUILayout.LabelField("Simulation Settings", EditorStyles.boldLabel);

        simulation.simulationSpeed = EditorGUILayout.Slider("Simulation Speed", simulation.simulationSpeed, 0f, 5f);
        simulation.collisionElasticity = EditorGUILayout.Slider("Collision Elasticity", simulation.collisionElasticity, 0f, 1f);

        EditorGUI.BeginChangeCheck();
        simulation.simulationBounds = EditorGUILayout.Vector3Field("Simulation Bounds", simulation.simulationBounds);
        if (EditorGUI.EndChangeCheck() && Application.isPlaying)
        {
            simulation.RequestReset();
        }

        simulation.dampening = EditorGUILayout.Slider("Dampening", simulation.dampening, 0.5f, 1f);
        simulation.interactionStrength = EditorGUILayout.Slider("Interaction Strength", simulation.interactionStrength, 0f, 5f);
        simulation.minDistance = EditorGUILayout.Slider("Min Distance", simulation.minDistance, 0.01f, 5f);
        simulation.bounceForce = EditorGUILayout.Slider("Bounce Force", simulation.bounceForce, 0f, 1f);
        simulation.maxForce = EditorGUILayout.Slider("Max Force", simulation.maxForce, 1f, 1000f);
        simulation.maxVelocity = EditorGUILayout.Slider("Max Velocity", simulation.maxVelocity, 1f, 100f);
        simulation.interactionRadius = EditorGUILayout.Slider("Interaction Radius", simulation.interactionRadius, 1f, 1000f);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Particle Types", EditorStyles.boldLabel);
        SerializedProperty particleTypesProperty = serializedObject.FindProperty("particleTypes");
        EditorGUILayout.PropertyField(particleTypesProperty, true);

        EditorGUILayout.LabelField("Interaction Rules", EditorStyles.boldLabel);
        SerializedProperty interactionRulesProperty = serializedObject.FindProperty("interactionRules");
        EditorGUILayout.PropertyField(interactionRulesProperty, true);
    }

    private void DrawPerformanceSection(GPUParticleSimulation simulation)
    {
        showPerformanceSettings = EditorGUILayout.Foldout(showPerformanceSettings, "Performance Optimization Settings", true);
        if (showPerformanceSettings)
        {
            EditorGUI.indentLevel++;

            // Cell size setting with explanation
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            simulation.cellSize = EditorGUILayout.Slider("Cell Size", simulation.cellSize, 0.1f, 10f);
            if (EditorGUI.EndChangeCheck() && Application.isPlaying)
            {
                simulation.RequestReset();
            }

            if (GUILayout.Button("?", GUILayout.Width(25)))
            {
                EditorUtility.DisplayDialog("Grid Cell Size",
                    "Size of each spatial partitioning grid cell. Should be approximately equal to interaction radius.\n\n" +
                    "Smaller cells: More precise neighborhood searches but higher memory usage\n" +
                    "Larger cells: Less memory but more particles per cell to test\n\n" +
                    "Optimal: Usually around half of interaction radius for dense simulations.", "OK");
            }
            EditorGUILayout.EndHorizontal();

            // Thread group size
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            simulation.threadGroupSize = EditorGUILayout.IntSlider("Thread Group Size", simulation.threadGroupSize, 1, 8);
            if (EditorGUI.EndChangeCheck() && Application.isPlaying)
            {
                simulation.RequestReset();
            }

            if (GUILayout.Button("?", GUILayout.Width(25)))
            {
                EditorUtility.DisplayDialog("Thread Group Size",
                    "Size of thread groups for 3D spatial grid.\n\n" +
                    "This affects GPU compute shader performance and memory usage. Optimal value depends on your GPU architecture.\n\n" +
                    "Try different values to find the best performance for your specific hardware.", "OK");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }
    }

    private void DrawLODSection(GPUParticleSimulation simulation)
    {
        showLODSettings = EditorGUILayout.Foldout(showLODSettings, "Level of Detail (LOD) Settings", true);
        if (showLODSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            simulation.enableLOD = EditorGUILayout.Toggle("Enable LOD", simulation.enableLOD);
            if (GUILayout.Button("?", GUILayout.Width(25)))
            {
                EditorUtility.DisplayDialog("Level of Detail",
                    "LOD dynamically reduces simulation quality for distant particles to maintain performance.\n\n" +
                    "ON: Better performance with millions of particles\n" +
                    "OFF: Consistent quality everywhere but potential performance impact\n\n" +
                    "Use this feature when scaling to very large particle counts.", "OK");
            }
            EditorGUILayout.EndHorizontal();

            if (simulation.enableLOD)
            {
                simulation.lodLevels = EditorGUILayout.IntSlider("LOD Levels", simulation.lodLevels, 0, 3);

                simulation.dynamicLOD = EditorGUILayout.Toggle("Dynamic LOD", simulation.dynamicLOD);

                if (simulation.dynamicLOD)
                {
                    EditorGUI.indentLevel++;
                    simulation.targetFPS = EditorGUILayout.Slider("Target FPS", simulation.targetFPS, 30f, 144f);
                    simulation.lodAdjustSpeed = EditorGUILayout.Slider("Adjust Speed", simulation.lodAdjustSpeed, 0.1f, 1.0f);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawRenderingSection(GPUParticleSimulation simulation)
    {
        showRenderingSettings = EditorGUILayout.Foldout(showRenderingSettings, "Rendering Settings", true);
        if (showRenderingSettings)
        {
            EditorGUI.indentLevel++;

            // Additional rendering options can be added here later
            EditorGUILayout.HelpBox("Particles are rendered using GPU instancing for optimal performance.", MessageType.Info);

            EditorGUI.indentLevel--;
        }
    }

    private void DrawDebugSection(GPUParticleSimulation simulation)
    {
        showDebugInfo = EditorGUILayout.Foldout(showDebugInfo, "Performance Diagnostics", true);

        if (showDebugInfo)
        {
            EditorGUI.indentLevel++;

            simulation.debugDrawCells = EditorGUILayout.Toggle("Enable Debug Cells", simulation.debugDrawCells);
            simulation.debugDrawParticles = EditorGUILayout.Toggle("Enable Debug Particles", simulation.debugDrawParticles);

            // Update frame time stats
            UpdateFrameTimeStats();

            // Display particle count
            EditorGUILayout.LabelField("Particle Count", simulation.particleCount.ToString("N0"));
            EditorGUILayout.LabelField("Active Particles", simulation.activeParticleCount.ToString("N0"));
            EditorGUILayout.LabelField("Grid Cells", simulation.gridCellCount.ToString("N0"));

            // Display frame time stats
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Current Frame Time", $"{lastFrameTime * 1000f:F2} ms ({1f / Mathf.Max(0.001f, lastFrameTime):F1} FPS)");
            EditorGUILayout.LabelField("Average Frame Time", $"{avgFrameTime * 1000f:F2} ms ({1f / Mathf.Max(0.001f, avgFrameTime):F1} FPS)");
            EditorGUILayout.LabelField("Min/Max Frame Time", $"{minFrameTime * 1000f:F2} ms / {maxFrameTime * 1000f:F2} ms");

            if (simulation.enableLOD && simulation.dynamicLOD)
            {
                EditorGUILayout.LabelField("Current LOD Factor", simulation.currentLODFactor.ToString("F2"));
            }

            // Performance warnings
            if (1f / Mathf.Max(0.001f, avgFrameTime) < 30f)
            {
                EditorGUILayout.HelpBox("Performance is below 30 FPS. Consider reducing particle count, increasing cell size, or enabling LOD for better performance.", MessageType.Warning);
            }

            // Add button to dump debug info to console
            if (GUILayout.Button("Log Debug Info"))
            {
                Debug.Log($"=== GPU Particle Simulation Debug Info ===");
                Debug.Log($"Particle Count: {simulation.particleCount:N0}");
                Debug.Log($"Active Particles: {simulation.activeParticleCount:N0}");
                Debug.Log($"Grid Size: {simulation.gridCellCount:N0} cells");
                Debug.Log($"Frame Time: {simulation.frameTimeMs:F2} ms");
                Debug.Log($"LOD Factor: {simulation.currentLODFactor:F2}");
                Debug.Log($"Interaction Radius: {simulation.interactionRadius:F2}");
                Debug.Log($"Cell Size: {simulation.cellSize:F2}");
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawInteractionMatrixSection(GPUParticleSimulation simulation)
    {
        EditorGUILayout.Space(10);
        showInteractionMatrix = EditorGUILayout.Foldout(showInteractionMatrix, "Interaction Matrix Editor", true);

        if (showInteractionMatrix && simulation.particleTypes.Count > 0)
        {
            EditorGUILayout.HelpBox("Set attraction values between particle types. Positive values attract, negative values repel.", MessageType.Info);
            EditorGUILayout.Space(5);

            // Create a dictionary for quick lookup
            Dictionary<(int, int), float> interactionValues = new Dictionary<(int, int), float>();
            foreach (var rule in simulation.interactionRules)
            {
                interactionValues[(rule.typeIndexA, rule.typeIndexB)] = rule.attractionValue;
            }

            // Matrix header row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Effect of ↓ on →", GUILayout.Width(100));

            for (int i = 0; i < simulation.particleTypes.Count; i++)
            {
                EditorGUILayout.LabelField(simulation.particleTypes[i].name, EditorStyles.boldLabel, GUILayout.Width(80));
            }

            EditorGUILayout.EndHorizontal();

            // Matrix rows (supporting asymmetric relationships)
            for (int i = 0; i < simulation.particleTypes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(simulation.particleTypes[i].name, EditorStyles.boldLabel, GUILayout.Width(100));

                for (int j = 0; j < simulation.particleTypes.Count; j++)
                {
                    float value = 0;
                    interactionValues.TryGetValue((i, j), out value);

                    // Use custom colored field to indicate attraction/repulsion
                    EditorGUI.BeginChangeCheck();

                    // Gradient colors: red (-1.0) to white (0.0) to green (1.0)
                    Color fieldColor = value > 0
                        ? Color.Lerp(Color.white, new Color(0.7f, 1f, 0.7f), Mathf.Abs(value))
                        : Color.Lerp(Color.white, new Color(1f, 0.7f, 0.7f), Mathf.Abs(value));

                    GUI.color = fieldColor;
                    float newValue = EditorGUILayout.FloatField(value, GUILayout.Width(80));
                    GUI.color = Color.white;

                    if (EditorGUI.EndChangeCheck())
                    {
                        // Update just this specific direction
                        if (Application.isPlaying)
                        {
                            simulation.UpdateInteractionRule(i, j, newValue);
                        }
                        else
                        {
                            SetDirectionalInteractionValue(simulation, i, j, newValue);
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            // Buttons for matrix operations
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset All Interactions"))
            {
                if (EditorUtility.DisplayDialog("Reset Interactions",
                    "Are you sure you want to clear all interaction rules?", "Yes", "Cancel"))
                {
                    simulation.interactionRules.Clear();
                    if (Application.isPlaying)
                    {
                        simulation.RequestReset();
                    }
                    else
                    {
                        EditorUtility.SetDirty(simulation);
                    }
                }
            }

            if (GUILayout.Button("Randomize Interactions"))
            {
                if (EditorUtility.DisplayDialog("Randomize Interactions",
                    "Are you sure you want to randomize all interaction values?", "Yes", "Cancel"))
                {
                    RandomizeInteractions(simulation);
                    if (Application.isPlaying)
                    {
                        simulation.RequestReset();
                    }
                    else
                    {
                        EditorUtility.SetDirty(simulation);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            // Preset buttons
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Presets:", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Orbit System"))
            {
                CreateOrbitSystemPreset(simulation);
                if (Application.isPlaying)
                {
                    simulation.RequestReset();
                }
            }

            if (GUILayout.Button("Galaxy Formation"))
            {
                CreateGalaxyPreset(simulation);
                if (Application.isPlaying)
                {
                    simulation.RequestReset();
                }
            }

            if (GUILayout.Button("Fluid Simulation"))
            {
                CreateFluidPreset(simulation);
                if (Application.isPlaying)
                {
                    simulation.RequestReset();
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void UpdateFrameTimeStats()
    {
        lastFrameTime = Time.deltaTime;

        // Add to queue and maintain window size
        frameTimes.Enqueue(lastFrameTime);
        if (frameTimes.Count > frameWindow)
        {
            frameTimes.Dequeue();
        }

        // Calculate stats
        float sum = 0f;
        minFrameTime = float.MaxValue;
        maxFrameTime = 0f;

        foreach (float time in frameTimes)
        {
            sum += time;
            minFrameTime = Mathf.Min(minFrameTime, time);
            maxFrameTime = Mathf.Max(maxFrameTime, time);
        }

        avgFrameTime = sum / frameTimes.Count;
    }

    private void SetDirectionalInteractionValue(GPUParticleSimulation simulation, int typeA, int typeB, float value)
    {
        // Look for existing rule
        for (int i = 0; i < simulation.interactionRules.Count; i++)
        {
            var rule = simulation.interactionRules[i];
            if (rule.typeIndexA == typeA && rule.typeIndexB == typeB)
            {
                // Update existing rule
                rule.attractionValue = value;
                simulation.interactionRules[i] = rule;
                return;
            }
        }

        // Create new rule
        var newRule = new GPUParticleSimulation.InteractionRule
        {
            typeIndexA = typeA,
            typeIndexB = typeB,
            attractionValue = value
        };

        simulation.interactionRules.Add(newRule);
    }

    private void RandomizeInteractions(GPUParticleSimulation simulation)
    {
        simulation.interactionRules.Clear();

        for (int i = 0; i < simulation.particleTypes.Count; i++)
        {
            for (int j = 0; j < simulation.particleTypes.Count; j++)
            {
                if (i == j) continue; // Skip self-interactions for now

                float value = Random.Range(-1f, 1f);

                var rule = new GPUParticleSimulation.InteractionRule
                {
                    typeIndexA = i,
                    typeIndexB = j,
                    attractionValue = value
                };

                simulation.interactionRules.Add(rule);
            }
        }
    }

    private void CreateOrbitSystemPreset(GPUParticleSimulation simulation)
    {
        // Ensure we have at least 2 particle types
        while (simulation.particleTypes.Count < 2)
        {
            simulation.particleTypes.Add(new GPUParticleSimulation.ParticleType
            {
                name = simulation.particleTypes.Count == 0 ? "Star" : "Planet"
            });
        }

        // Configure types
        simulation.particleTypes[0].name = "Star";
        simulation.particleTypes[0].color = new Color(1f, 0.8f, 0.2f); // Yellow
        simulation.particleTypes[0].mass = 100f;
        simulation.particleTypes[0].radius = 2f;
        simulation.particleTypes[0].spawnAmount = 1;

        simulation.particleTypes[1].name = "Planet";
        simulation.particleTypes[1].color = new Color(0.2f, 0.4f, 0.8f); // Blue
        simulation.particleTypes[1].mass = 1f;
        simulation.particleTypes[1].radius = 0.5f;
        simulation.particleTypes[1].spawnAmount = 100;

        // Set up rules
        simulation.interactionRules.Clear();

        // Star attracts planets
        SetDirectionalInteractionValue(simulation, 0, 1, 10f);
        // Planets are attracted to star (set both directions)
        SetDirectionalInteractionValue(simulation, 1, 0, 10f);

        // Star self-interaction (none)
        SetDirectionalInteractionValue(simulation, 0, 0, 0f);

        // Planet self-interaction (slight repulsion)
        SetDirectionalInteractionValue(simulation, 1, 1, -0.1f);

        // Adjust simulation parameters
        simulation.interactionStrength = 0.1f;
        simulation.dampening = 1.0f; // No energy loss
        simulation.minDistance = 1.0f;

        EditorUtility.SetDirty(simulation);
    }

    private void CreateGalaxyPreset(GPUParticleSimulation simulation)
    {
        // Ensure we have at least 3 particle types
        while (simulation.particleTypes.Count < 3)
        {
            simulation.particleTypes.Add(new GPUParticleSimulation.ParticleType
            {
                name = "Type " + simulation.particleTypes.Count
            });
        }

        // Configure types
        simulation.particleTypes[0].name = "Black Hole";
        simulation.particleTypes[0].color = new Color(0.1f, 0.0f, 0.2f); // Dark purple
        simulation.particleTypes[0].mass = 500f;
        simulation.particleTypes[0].radius = 3f;
        simulation.particleTypes[0].spawnAmount = 1;

        simulation.particleTypes[1].name = "Stars";
        simulation.particleTypes[1].color = new Color(0.9f, 0.9f, 1.0f); // White
        simulation.particleTypes[1].mass = 1f;
        simulation.particleTypes[1].radius = 0.3f;
        simulation.particleTypes[1].spawnAmount = 200;

        simulation.particleTypes[2].name = "Dust";
        simulation.particleTypes[2].color = new Color(0.5f, 0.3f, 0.7f); // Purple
        simulation.particleTypes[2].mass = 0.1f;
        simulation.particleTypes[2].radius = 0.1f;
        simulation.particleTypes[2].spawnAmount = 300;

        // Set up rules
        simulation.interactionRules.Clear();

        // Black hole strongly attracts everything
        SetDirectionalInteractionValue(simulation, 0, 1, 5.0f);
        SetDirectionalInteractionValue(simulation, 0, 2, 5.0f);
        SetDirectionalInteractionValue(simulation, 1, 0, 5.0f);
        SetDirectionalInteractionValue(simulation, 2, 0, 5.0f);

        // Stars weakly attract each other and dust
        SetDirectionalInteractionValue(simulation, 1, 1, 0.2f);
        SetDirectionalInteractionValue(simulation, 1, 2, 0.3f);
        SetDirectionalInteractionValue(simulation, 2, 1, 0.3f);

        // Dust weakly attracts dust
        SetDirectionalInteractionValue(simulation, 2, 2, 0.1f);

        // Adjust simulation parameters
        simulation.interactionStrength = 0.2f;
        simulation.dampening = 0.99f;
        simulation.minDistance = 0.5f;
        simulation.interactionRadius = 20f;

        EditorUtility.SetDirty(simulation);
    }

    private void CreateFluidPreset(GPUParticleSimulation simulation)
    {
        // Ensure we have at least 3 particle types
        while (simulation.particleTypes.Count < 3)
        {
            simulation.particleTypes.Add(new GPUParticleSimulation.ParticleType
            {
                name = "Type " + simulation.particleTypes.Count
            });
        }

        // Configure types
        simulation.particleTypes[0].name = "Water";
        simulation.particleTypes[0].color = new Color(0.2f, 0.5f, 0.9f); // Blue
        simulation.particleTypes[0].mass = 1f;
        simulation.particleTypes[0].radius = 0.3f;
        simulation.particleTypes[0].spawnAmount = 300;

        simulation.particleTypes[1].name = "Oil";
        simulation.particleTypes[1].color = new Color(0.8f, 0.6f, 0.2f); // Amber
        simulation.particleTypes[1].mass = 0.7f;
        simulation.particleTypes[1].radius = 0.3f;
        simulation.particleTypes[1].spawnAmount = 200;

        simulation.particleTypes[2].name = "Gas";
        simulation.particleTypes[2].color = new Color(0.7f, 0.7f, 0.7f, 0.5f); // Light gray
        simulation.particleTypes[2].mass = 0.2f;
        simulation.particleTypes[2].radius = 0.2f;
        simulation.particleTypes[2].spawnAmount = 150;

        // Set up rules
        simulation.interactionRules.Clear();

        // Water attracts water, repels oil
        SetDirectionalInteractionValue(simulation, 0, 0, 0.4f);
        SetDirectionalInteractionValue(simulation, 0, 1, -0.2f);
        SetDirectionalInteractionValue(simulation, 1, 0, -0.2f);
        SetDirectionalInteractionValue(simulation, 0, 2, -0.05f);
        SetDirectionalInteractionValue(simulation, 2, 0, -0.05f);

        // Oil attracts oil, repels water
        SetDirectionalInteractionValue(simulation, 1, 1, 0.4f);
        SetDirectionalInteractionValue(simulation, 1, 2, -0.05f);
        SetDirectionalInteractionValue(simulation, 2, 1, -0.05f);

        // Gas weakly attracts itself, is otherwise neutral
        SetDirectionalInteractionValue(simulation, 2, 2, 0.1f);

        // Adjust simulation parameters
        simulation.interactionStrength = 0.3f;
        simulation.dampening = 0.8f; // More friction
        simulation.minDistance = 0.2f;
        simulation.maxVelocity = 10f;
        simulation.interactionRadius = 5f;

        EditorUtility.SetDirty(simulation);
    }
}
#endif