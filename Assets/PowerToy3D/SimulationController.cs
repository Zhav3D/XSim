using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace PowderToy3D
{
    /// <summary>
    /// Main simulation controller for the 3D Powder Toy system.
    /// Manages the 3D voxel grid and coordinates physics simulation on the GPU.
    /// </summary>
    public class SimulationController : MonoBehaviour
    {
        #region Serialized Fields
        [Header("Simulation Settings")]
        [SerializeField] private Vector3Int gridDimensions = new Vector3Int(128, 128, 128);
        [SerializeField] private float physicsTimeStep = 0.016f;
        [SerializeField] private bool pauseSimulation = false;
        [SerializeField] private float gravity = 9.8f;
        [SerializeField] private float temperatureDiffusionRate = 0.05f;
        [SerializeField] private float pressureDiffusionRate = 0.1f;

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader simulationShader;
        [SerializeField] private ComputeShader interactionShader;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        [SerializeField] private bool showGridBounds = true;
        #endregion

        #region Private Fields
        private RenderTexture _voxelGridTexture;   // 3D texture storing voxel data
        private RenderTexture _temperatureTexture; // 3D texture for temperature
        private RenderTexture _pressureTexture;    // 3D texture for pressure
        private RenderTexture _velocityTexture;    // 3D texture for velocity vectors

        // Kernel IDs
        private int _simulatePhysicsKernel;
        private int _simulateTemperatureKernel;
        private int _simulatePressureKernel;
        private int _simulateReactionsKernel;

        // Buffer for element properties
        private ComputeBuffer _elementPropertiesBuffer;

        // Thread group sizes
        private int _threadGroupSizeX = 8;
        private int _threadGroupSizeY = 8;
        private int _threadGroupSizeZ = 8;

        // Frame counter for fixed timestep management
        private float _accumulatedTime = 0f;

        // Element data
        private ElementDatabase _elementDatabase;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the current voxel grid texture.
        /// </summary>
        public RenderTexture VoxelGridTexture => _voxelGridTexture;

        /// <summary>
        /// Gets the current temperature texture.
        /// </summary>
        public RenderTexture TemperatureTexture => _temperatureTexture;

        /// <summary>
        /// Gets the current pressure texture.
        /// </summary>
        public RenderTexture PressureTexture => _pressureTexture;

        /// <summary>
        /// Gets the velocity texture.
        /// </summary>
        public RenderTexture VelocityTexture => _velocityTexture;

        /// <summary>
        /// Gets the grid dimensions.
        /// </summary>
        public Vector3Int GridDimensions => gridDimensions;

        /// <summary>
        /// Gets or sets whether the simulation is paused.
        /// </summary>
        public bool IsPaused
        {
            get => pauseSimulation;
            set => pauseSimulation = value;
        }
        #endregion

        #region Unity Lifecycle Methods
        private void Awake()
        {
            // Initialize element database
            _elementDatabase = new ElementDatabase();

            // Initialize 3D textures and compute shaders
            InitializeSimulation();
        }

        private void Update()
        {
            if (pauseSimulation)
                return;

            // Use fixed timestep for simulation
            _accumulatedTime += Time.deltaTime;

            while (_accumulatedTime >= physicsTimeStep)
            {
                RunSimulation(physicsTimeStep);
                _accumulatedTime -= physicsTimeStep;
            }
        }

        private void OnDestroy()
        {
            // Clean up resources
            _voxelGridTexture?.Release();
            _temperatureTexture?.Release();
            _pressureTexture?.Release();
            _velocityTexture?.Release();
            _elementPropertiesBuffer?.Release();
        }

        private void OnDrawGizmos()
        {
            if (showGridBounds)
            {
                Gizmos.color = Color.yellow;
                Vector3 gridSize = new Vector3(gridDimensions.x, gridDimensions.y, gridDimensions.z);
                Gizmos.DrawWireCube(transform.position, gridSize);
            }
        }
        #endregion

        #region Initialization Methods
        /// <summary>
        /// Initializes all simulation resources including textures and compute shaders.
        /// </summary>
        private void InitializeSimulation()
        {
            CreateVoxelTextures();
            InitializeComputeShaders();
            InitializeElementProperties();
        }

        /// <summary>
        /// Creates all 3D textures needed for the simulation.
        /// </summary>
        private void CreateVoxelTextures()
        {
            // Create main voxel grid texture
            _voxelGridTexture = new RenderTexture(
                gridDimensions.x,
                gridDimensions.y,
                0,
                RenderTextureFormat.ARGBFloat
            );
            _voxelGridTexture.dimension = TextureDimension.Tex3D;
            _voxelGridTexture.volumeDepth = gridDimensions.z;
            _voxelGridTexture.enableRandomWrite = true;
            _voxelGridTexture.filterMode = FilterMode.Point;
            _voxelGridTexture.Create();

            // Create temperature texture
            _temperatureTexture = new RenderTexture(
                gridDimensions.x,
                gridDimensions.y,
                0,
                RenderTextureFormat.RFloat
            );
            _temperatureTexture.dimension = TextureDimension.Tex3D;
            _temperatureTexture.volumeDepth = gridDimensions.z;
            _temperatureTexture.enableRandomWrite = true;
            _temperatureTexture.filterMode = FilterMode.Point;
            _temperatureTexture.Create();

            // Create pressure texture
            _pressureTexture = new RenderTexture(
                gridDimensions.x,
                gridDimensions.y,
                0,
                RenderTextureFormat.RFloat
            );
            _pressureTexture.dimension = TextureDimension.Tex3D;
            _pressureTexture.volumeDepth = gridDimensions.z;
            _pressureTexture.enableRandomWrite = true;
            _pressureTexture.filterMode = FilterMode.Point;
            _pressureTexture.Create();

            // Create velocity texture
            _velocityTexture = new RenderTexture(
                gridDimensions.x,
                gridDimensions.y,
                0,
                RenderTextureFormat.ARGBFloat
            );
            _velocityTexture.dimension = TextureDimension.Tex3D;
            _velocityTexture.volumeDepth = gridDimensions.z;
            _velocityTexture.enableRandomWrite = true;
            _velocityTexture.filterMode = FilterMode.Point;
            _velocityTexture.Create();

            // Initialize with empty values
            ClearVoxelTextures();
        }

        /// <summary>
        /// Initializes all compute shader kernels and variables.
        /// </summary>
        private void InitializeComputeShaders()
        {
            // Find all kernel IDs
            _simulatePhysicsKernel = simulationShader.FindKernel("SimulatePhysics");
            _simulateTemperatureKernel = simulationShader.FindKernel("SimulateTemperature");
            _simulatePressureKernel = simulationShader.FindKernel("SimulatePressure");
            _simulateReactionsKernel = simulationShader.FindKernel("SimulateReactions");

            // Get thread group sizes
            uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
            simulationShader.GetKernelThreadGroupSizes(
                _simulatePhysicsKernel,
                out threadGroupSizeX,
                out threadGroupSizeY,
                out threadGroupSizeZ
            );

            _threadGroupSizeX = (int)threadGroupSizeX;
            _threadGroupSizeY = (int)threadGroupSizeY;
            _threadGroupSizeZ = (int)threadGroupSizeZ;

            // Set constant shader parameters
            simulationShader.SetVector("GridDimensions", new Vector4(
                gridDimensions.x,
                gridDimensions.y,
                gridDimensions.z,
                0
            ));

            // Set constant textures
            SetSimulationTextures(_simulatePhysicsKernel);
            SetSimulationTextures(_simulateTemperatureKernel);
            SetSimulationTextures(_simulatePressureKernel);
            SetSimulationTextures(_simulateReactionsKernel);
        }

        /// <summary>
        /// Sets simulation textures for a specific compute shader kernel.
        /// </summary>
        private void SetSimulationTextures(int kernelId)
        {
            simulationShader.SetTexture(kernelId, "VoxelGrid", _voxelGridTexture);
            simulationShader.SetTexture(kernelId, "TemperatureGrid", _temperatureTexture);
            simulationShader.SetTexture(kernelId, "PressureGrid", _pressureTexture);
            simulationShader.SetTexture(kernelId, "VelocityGrid", _velocityTexture);
        }

        /// <summary>
        /// Initializes element properties buffer for the compute shader.
        /// </summary>
        private void InitializeElementProperties()
        {
            // Create element properties buffer
            List<ElementProperties> elementProperties = _elementDatabase.GetAllElementProperties();
            _elementPropertiesBuffer = new ComputeBuffer(elementProperties.Count, ElementProperties.Stride);
            _elementPropertiesBuffer.SetData(elementProperties);

            // Set buffer in all relevant kernels
            simulationShader.SetBuffer(_simulatePhysicsKernel, "ElementProperties", _elementPropertiesBuffer);
            simulationShader.SetBuffer(_simulateTemperatureKernel, "ElementProperties", _elementPropertiesBuffer);
            simulationShader.SetBuffer(_simulatePressureKernel, "ElementProperties", _elementPropertiesBuffer);
            simulationShader.SetBuffer(_simulateReactionsKernel, "ElementProperties", _elementPropertiesBuffer);
        }

        /// <summary>
        /// Clears all voxel textures to their default values.
        /// </summary>
        public void ClearVoxelTextures()
        {
            // Clear main voxel grid
            interactionShader.SetTexture(0, "VoxelGrid", _voxelGridTexture);
            interactionShader.SetVector("ClearColor", new Vector4(0, 0, 0, 0));
            interactionShader.Dispatch(0,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // Clear temperature grid
            interactionShader.SetTexture(1, "TemperatureGrid", _temperatureTexture);
            interactionShader.SetFloat("DefaultTemperature", 25.0f); // Ambient temperature
            interactionShader.Dispatch(1,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // Clear pressure grid
            interactionShader.SetTexture(2, "PressureGrid", _pressureTexture);
            interactionShader.SetFloat("DefaultPressure", 1.0f); // Atmospheric pressure
            interactionShader.Dispatch(2,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // Clear velocity grid
            interactionShader.SetTexture(3, "VelocityGrid", _velocityTexture);
            interactionShader.SetVector("ClearVelocity", new Vector4(0, 0, 0, 0));
            interactionShader.Dispatch(3,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }
        #endregion

        #region Simulation Methods
        /// <summary>
        /// Runs a single step of the physics simulation.
        /// </summary>
        /// <param name="deltaTime">Time step duration</param>
        private void RunSimulation(float deltaTime)
        {
            // Update shader parameters
            simulationShader.SetFloat("DeltaTime", deltaTime);
            simulationShader.SetFloat("Gravity", gravity);
            simulationShader.SetFloat("TemperatureDiffusionRate", temperatureDiffusionRate);
            simulationShader.SetFloat("PressureDiffusionRate", pressureDiffusionRate);
            simulationShader.SetFloat("Time", Time.time);

            // Run simulation passes
            // 1. Simulate physical movement
            simulationShader.Dispatch(_simulatePhysicsKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // 2. Simulate temperature diffusion
            simulationShader.Dispatch(_simulateTemperatureKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // 3. Simulate pressure changes
            simulationShader.Dispatch(_simulatePressureKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );

            // 4. Simulate chemical reactions
            simulationShader.Dispatch(_simulateReactionsKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }

        /// <summary>
        /// Adds an element to the simulation at the specified position.
        /// </summary>
        /// <param name="elementId">The element ID to add</param>
        /// <param name="position">Grid position</param>
        /// <param name="radius">Brush radius</param>
        /// <param name="initialVelocity">Initial velocity for the added particles</param>
        public void AddElement(int elementId, Vector3Int position, float radius, Vector3 initialVelocity)
        {
            int addElementKernel = interactionShader.FindKernel("AddElement");

            // Set parameters
            interactionShader.SetTexture(addElementKernel, "VoxelGrid", _voxelGridTexture);
            interactionShader.SetTexture(addElementKernel, "TemperatureGrid", _temperatureTexture);
            interactionShader.SetTexture(addElementKernel, "PressureGrid", _pressureTexture);
            interactionShader.SetTexture(addElementKernel, "VelocityGrid", _velocityTexture);

            interactionShader.SetVector("BrushPosition", new Vector4(position.x, position.y, position.z, 0));
            interactionShader.SetFloat("BrushRadius", radius);
            interactionShader.SetInt("ElementId", elementId);
            interactionShader.SetVector("InitialVelocity", new Vector4(initialVelocity.x, initialVelocity.y, initialVelocity.z, 0));

            // Add element parameters from database
            ElementProperties elementProps = _elementDatabase.GetElementProperties(elementId);
            interactionShader.SetFloat("ElementTemperature", elementProps.DefaultTemperature);

            // Dispatch
            interactionShader.Dispatch(addElementKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }

        /// <summary>
        /// Adds a heat source to the simulation at the specified position.
        /// </summary>
        /// <param name="position">Grid position</param>
        /// <param name="radius">Brush radius</param>
        /// <param name="temperature">Temperature to set</param>
        public void AddHeat(Vector3Int position, float radius, float temperature)
        {
            int addHeatKernel = interactionShader.FindKernel("AddHeat");

            interactionShader.SetTexture(addHeatKernel, "TemperatureGrid", _temperatureTexture);
            interactionShader.SetVector("BrushPosition", new Vector4(position.x, position.y, position.z, 0));
            interactionShader.SetFloat("BrushRadius", radius);
            interactionShader.SetFloat("TargetTemperature", temperature);

            interactionShader.Dispatch(addHeatKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }

        /// <summary>
        /// Adds pressure to the simulation at the specified position.
        /// </summary>
        /// <param name="position">Grid position</param>
        /// <param name="radius">Brush radius</param>
        /// <param name="pressure">Pressure to add</param>
        public void AddPressure(Vector3Int position, float radius, float pressure)
        {
            int addPressureKernel = interactionShader.FindKernel("AddPressure");

            interactionShader.SetTexture(addPressureKernel, "PressureGrid", _pressureTexture);
            interactionShader.SetVector("BrushPosition", new Vector4(position.x, position.y, position.z, 0));
            interactionShader.SetFloat("BrushRadius", radius);
            interactionShader.SetFloat("PressureAmount", pressure);

            interactionShader.Dispatch(addPressureKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }

        /// <summary>
        /// Removes all elements within a radius.
        /// </summary>
        /// <param name="position">Grid position</param>
        /// <param name="radius">Brush radius</param>
        public void EraseElements(Vector3Int position, float radius)
        {
            int eraseKernel = interactionShader.FindKernel("Erase");

            interactionShader.SetTexture(eraseKernel, "VoxelGrid", _voxelGridTexture);
            interactionShader.SetTexture(eraseKernel, "VelocityGrid", _velocityTexture);
            interactionShader.SetVector("BrushPosition", new Vector4(position.x, position.y, position.z, 0));
            interactionShader.SetFloat("BrushRadius", radius);

            interactionShader.Dispatch(eraseKernel,
                Mathf.CeilToInt(gridDimensions.x / (float)_threadGroupSizeX),
                Mathf.CeilToInt(gridDimensions.y / (float)_threadGroupSizeY),
                Mathf.CeilToInt(gridDimensions.z / (float)_threadGroupSizeZ)
            );
        }

        /// <summary>
        /// Converts grid coordinate to world space.
        /// </summary>
        public Vector3 GridToWorldPosition(Vector3Int gridPosition)
        {
            Vector3 normalizedPosition = new Vector3(
                ((float)gridPosition.x / gridDimensions.x) - 0.5f,
                ((float)gridPosition.y / gridDimensions.y) - 0.5f,
                ((float)gridPosition.z / gridDimensions.z) - 0.5f
            );

            return new Vector3(
                normalizedPosition.x * gridDimensions.x,
                normalizedPosition.y * gridDimensions.y,
                normalizedPosition.z * gridDimensions.z
            ) + transform.position;
        }

        /// <summary>
        /// Converts world position to grid coordinate.
        /// </summary>
        public Vector3Int WorldToGridPosition(Vector3 worldPosition)
        {
            Vector3 localPosition = worldPosition - transform.position;
            Vector3 normalizedPosition = new Vector3(
                (localPosition.x / gridDimensions.x) + 0.5f,
                (localPosition.y / gridDimensions.y) + 0.5f,
                (localPosition.z / gridDimensions.z) + 0.5f
            );

            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(normalizedPosition.x * gridDimensions.x), 0, gridDimensions.x - 1),
                Mathf.Clamp(Mathf.FloorToInt(normalizedPosition.y * gridDimensions.y), 0, gridDimensions.y - 1),
                Mathf.Clamp(Mathf.FloorToInt(normalizedPosition.z * gridDimensions.z), 0, gridDimensions.z - 1)
            );
        }
        #endregion

        #region Save/Load System
        /// <summary>
        /// Saves the current simulation state to a byte array.
        /// </summary>
        public byte[] SaveSimulationState()
        {
            SimulationSaveData saveData = new SimulationSaveData();

            // Save grid dimensions
            saveData.GridDimensionsX = gridDimensions.x;
            saveData.GridDimensionsY = gridDimensions.y;
            saveData.GridDimensionsZ = gridDimensions.z;

            // Save voxel data
            saveData.VoxelData = GetTextureData(_voxelGridTexture);
            saveData.TemperatureData = GetTextureData(_temperatureTexture);
            saveData.PressureData = GetTextureData(_pressureTexture);
            saveData.VelocityData = GetTextureData(_velocityTexture);

            // Save simulation parameters
            saveData.GravityValue = gravity;
            saveData.TemperatureDiffusionRate = temperatureDiffusionRate;
            saveData.PressureDiffusionRate = pressureDiffusionRate;

            // Serialize to JSON and compress
            string jsonData = JsonUtility.ToJson(saveData);
            byte[] compressedData = System.Text.Encoding.UTF8.GetBytes(jsonData);

            return compressedData;
        }

        /// <summary>
        /// Loads a simulation state from a byte array.
        /// </summary>
        public void LoadSimulationState(byte[] compressedData)
        {
            // Decompress and deserialize
            string jsonData = System.Text.Encoding.UTF8.GetString(compressedData);
            SimulationSaveData saveData = JsonUtility.FromJson<SimulationSaveData>(jsonData);

            // Check if grid dimensions match
            if (saveData.GridDimensionsX != gridDimensions.x ||
                saveData.GridDimensionsY != gridDimensions.y ||
                saveData.GridDimensionsZ != gridDimensions.z)
            {
                // Recreate textures with new dimensions
                gridDimensions = new Vector3Int(
                    saveData.GridDimensionsX,
                    saveData.GridDimensionsY,
                    saveData.GridDimensionsZ
                );

                // Reinitialize with new dimensions
                CreateVoxelTextures();
                InitializeComputeShaders();
            }

            // Set texture data
            SetTextureData(_voxelGridTexture, saveData.VoxelData);
            SetTextureData(_temperatureTexture, saveData.TemperatureData);
            SetTextureData(_pressureTexture, saveData.PressureData);
            SetTextureData(_velocityTexture, saveData.VelocityData);

            // Set simulation parameters
            gravity = saveData.GravityValue;
            temperatureDiffusionRate = saveData.TemperatureDiffusionRate;
            pressureDiffusionRate = saveData.PressureDiffusionRate;
        }

        /// <summary>
        /// Gets raw data from a 3D texture.
        /// </summary>
        private byte[] GetTextureData(RenderTexture texture)
        {
            // Create temporary texture to read data from
            Texture3D tempTexture = new Texture3D(
                texture.width,
                texture.height,
                texture.volumeDepth,
                TextureFormat.RGBA32, // Use TextureFormat instead of graphicsFormat
                false // No mipmaps
            );

            // Read data from GPU
            Graphics.CopyTexture(texture, tempTexture);

            // Get raw data using GetPixelData
            var pixelData = tempTexture.GetPixelData<byte>(0);
            byte[] data = pixelData.ToArray();

            // Destroy temporary texture
            Destroy(tempTexture);

            return data;
        }

        /// <summary>
        /// Sets raw data to a 3D texture.
        /// </summary>
        private void SetTextureData(RenderTexture texture, byte[] data)
        {
            // Create temporary texture to write data to
            Texture3D tempTexture = new Texture3D(
                texture.width,
                texture.height,
                texture.volumeDepth,
                texture.graphicsFormat,
                TextureCreationFlags.None
            );

            // Set raw data
            tempTexture.SetPixelData(data, 0);
            tempTexture.Apply();

            // Copy to render texture
            Graphics.CopyTexture(tempTexture, texture);

            // Destroy temporary texture
            Destroy(tempTexture);
        }
        #endregion
    }

    /// <summary>
    /// Structure to hold simulation save data.
    /// </summary>
    [System.Serializable]
    public class SimulationSaveData
    {
        public int GridDimensionsX;
        public int GridDimensionsY;
        public int GridDimensionsZ;

        public byte[] VoxelData;
        public byte[] TemperatureData;
        public byte[] PressureData;
        public byte[] VelocityData;

        public float GravityValue;
        public float TemperatureDiffusionRate;
        public float PressureDiffusionRate;
    }
}