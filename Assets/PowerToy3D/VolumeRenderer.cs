using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PowderToy3D
{
    /// <summary>
    /// High-performance volume renderer for the 3D Powder Toy simulation.
    /// Uses a combination of raymarching and instanced rendering for optimized visualization.
    /// </summary>
    public class VolumeRenderer : MonoBehaviour
    {
        #region Serialized Fields
        [Header("Simulation Reference")]
        [SerializeField] private SimulationController simulation;

        [Header("Rendering Settings")]
        [SerializeField] private RenderingMode renderingMode = RenderingMode.Raymarching;
        [SerializeField] private float maxRenderDistance = 100f;
        [SerializeField] private float rayStepSize = 0.5f;
        [SerializeField] private float densityThreshold = 0.05f;
        [SerializeField] private int maxRaySteps = 128;
        [SerializeField] private bool enableShadows = true;
        [SerializeField] private bool enableLighting = true;
        [SerializeField] private bool enableAmbientOcclusion = true;

        [Header("Visual Effects")]
        [SerializeField] private float heatDistortion = 0.2f;
        [SerializeField] private float heatThreshold = 100.0f;
        [SerializeField] private float emissiveIntensity = 1.5f;
        [SerializeField] private bool showTemperature = true;
        [SerializeField] private bool showPressure = false;

        [Header("Level of Detail")]
        [SerializeField] private bool enableLOD = true;
        [SerializeField] private float lodDistance = 50.0f;
        [SerializeField] private int lodLevels = 3;
        [SerializeField] private float lodBias = 1.0f;

        [Header("Materials and Resources")]
        [SerializeField] private Material volumeRenderMaterial;
        [SerializeField] private Material instancedParticleMaterial;
        [SerializeField] private Mesh particleMesh;
        [SerializeField] private ComputeShader renderingComputeShader;

        [Header("Debug Visualization")]
        [SerializeField] private bool showBounds = true;
        [SerializeField] private bool showMeshRendering = false;
        #endregion

        #region Enums
        /// <summary>
        /// Available rendering modes for the volume visualization.
        /// </summary>
        public enum RenderingMode
        {
            Raymarching,         // Full volumetric raymarching
            InstancingLarge,     // Instanced rendering for larger particles
            InstancingSmall,     // Many small particles (higher detail)
            MarchingCubes,       // Surface extraction with marching cubes
            Hybrid               // Combination of methods based on distance
        }

        public enum ColorMode
        {
            ElementColor,        // Based on element type
            Temperature,         // Heat visualization
            Pressure,            // Pressure visualization
            Velocity,            // Movement visualization
            Density              // Element density
        }
        #endregion

        #region Private Fields
        // Camera reference
        private Camera _mainCamera;

        // Render textures
        private RenderTexture _colorVolume;
        private RenderTexture _densityVolume;

        // Compute shader resources
        private int _prepareVolumeKernel;
        private int _marchingCubesKernel;

        // Rendering buffers
        private ComputeBuffer _meshVerticesBuffer;
        private ComputeBuffer _meshTrianglesBuffer;
        private ComputeBuffer _meshCountBuffer;
        private ComputeBuffer _instanceDataBuffer;
        private ComputeBuffer _drawArgsBuffer;

        // Marching cubes resources
        private Mesh _generatedMesh;
        private const int MAX_VERTICES = 1000000;
        private const int MAX_TRIANGLES = 2000000;

        // Instanced rendering resources
        private const int MAX_INSTANCES = 1000000;
        private Matrix4x4[] _instanceMatrices;
        private Vector4[] _instanceColors;
        private MaterialPropertyBlock _instanceProperties;

        // Performance tracking
        private float _lastRenderTime;
        private int _lastRenderedParticles;
        private int _lastRenderedTriangles;

        // LOD control
        private float _currentLOD;
        private Vector3 _lastCameraPosition;

        // Render passes
        private CustomRenderPass _customRenderPass;
        #endregion

        #region Initialization
        private void Awake()
        {
            _mainCamera = Camera.main;
            InitializeRenderTextures();
            InitializeComputeShader();
            InitializeRenderingBuffers();
            InitializeRenderPasses();
        }

        private void InitializeRenderTextures()
        {
            // Create color volume texture
            _colorVolume = new RenderTexture(
                simulation.GridDimensions.x,
                simulation.GridDimensions.y,
                0,
                RenderTextureFormat.ARGBFloat
            );
            _colorVolume.dimension = TextureDimension.Tex3D;
            _colorVolume.volumeDepth = simulation.GridDimensions.z;
            _colorVolume.enableRandomWrite = true;
            _colorVolume.filterMode = FilterMode.Bilinear;
            _colorVolume.Create();

            // Create density volume texture
            _densityVolume = new RenderTexture(
                simulation.GridDimensions.x,
                simulation.GridDimensions.y,
                0,
                RenderTextureFormat.RFloat
            );
            _densityVolume.dimension = TextureDimension.Tex3D;
            _densityVolume.volumeDepth = simulation.GridDimensions.z;
            _densityVolume.enableRandomWrite = true;
            _densityVolume.filterMode = FilterMode.Bilinear;
            _densityVolume.Create();
        }

        private void InitializeComputeShader()
        {
            // Find kernels
            _prepareVolumeKernel = renderingComputeShader.FindKernel("PrepareVolumeData");
            _marchingCubesKernel = renderingComputeShader.FindKernel("GenerateMarchingCubes");

            // Set constant parameters
            renderingComputeShader.SetVector("GridDimensions", new Vector4(
                simulation.GridDimensions.x,
                simulation.GridDimensions.y,
                simulation.GridDimensions.z,
                0
            ));

            // Set textures
            renderingComputeShader.SetTexture(_prepareVolumeKernel, "VoxelGrid", simulation.VoxelGridTexture);
            renderingComputeShader.SetTexture(_prepareVolumeKernel, "TemperatureGrid", simulation.TemperatureTexture);
            renderingComputeShader.SetTexture(_prepareVolumeKernel, "PressureGrid", simulation.PressureTexture);
            renderingComputeShader.SetTexture(_prepareVolumeKernel, "ColorVolume", _colorVolume);
            renderingComputeShader.SetTexture(_prepareVolumeKernel, "DensityVolume", _densityVolume);

            renderingComputeShader.SetTexture(_marchingCubesKernel, "DensityVolume", _densityVolume);
            renderingComputeShader.SetTexture(_marchingCubesKernel, "ColorVolume", _colorVolume);
        }

        private void InitializeRenderingBuffers()
        {
            // Initialize buffers for marching cubes
            _meshVerticesBuffer = new ComputeBuffer(MAX_VERTICES, sizeof(float) * 7); // Pos(3) + Normal(3) + Color(1)
            _meshTrianglesBuffer = new ComputeBuffer(MAX_TRIANGLES, sizeof(int));
            _meshCountBuffer = new ComputeBuffer(1, sizeof(int) * 2, ComputeBufferType.Raw);

            renderingComputeShader.SetBuffer(_marchingCubesKernel, "Vertices", _meshVerticesBuffer);
            renderingComputeShader.SetBuffer(_marchingCubesKernel, "Triangles", _meshTrianglesBuffer);
            renderingComputeShader.SetBuffer(_marchingCubesKernel, "VertexTriangleCounter", _meshCountBuffer);

            // Initialize buffers for instanced rendering
            _instanceDataBuffer = new ComputeBuffer(MAX_INSTANCES, sizeof(float) * 8); // Pos(3) + Scale(1) + Color(4)
            _drawArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);

            // Initialize instance data arrays
            _instanceMatrices = new Matrix4x4[MAX_INSTANCES];
            _instanceColors = new Vector4[MAX_INSTANCES];
            _instanceProperties = new MaterialPropertyBlock();

            // Create mesh for marching cubes
            _generatedMesh = new Mesh();
            _generatedMesh.indexFormat = IndexFormat.UInt32;
            _generatedMesh.MarkDynamic();
        }

        private void InitializeRenderPasses()
        {
            // Create custom render pass for volume rendering
            _customRenderPass = new CustomRenderPass(this);

            // Set material properties
            volumeRenderMaterial.SetTexture("_ColorVolume", _colorVolume);
            volumeRenderMaterial.SetTexture("_DensityVolume", _densityVolume);
            volumeRenderMaterial.SetVector("_GridDimensions", new Vector4(
                simulation.GridDimensions.x,
                simulation.GridDimensions.y,
                simulation.GridDimensions.z,
                0
            ));
            volumeRenderMaterial.SetVector("_VolumeScale", Vector3.one);
            volumeRenderMaterial.SetVector("_VolumePosition", transform.position);
            volumeRenderMaterial.SetFloat("_StepSize", rayStepSize);
            volumeRenderMaterial.SetFloat("_DensityThreshold", densityThreshold);
            volumeRenderMaterial.SetFloat("_MaxSteps", maxRaySteps);
            volumeRenderMaterial.SetFloat("_HeatDistortion", heatDistortion);
            volumeRenderMaterial.SetFloat("_HeatThreshold", heatThreshold);
            volumeRenderMaterial.SetFloat("_EmissiveIntensity", emissiveIntensity);

            // Register with Universal Render Pipeline
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }
        #endregion

        #region Unity Lifecycle Methods
        private void OnEnable()
        {
            // Register with URP
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            // Unregister from URP
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void Update()
        {
            UpdateLOD();
            PrepareVolumeData();

            if (renderingMode == RenderingMode.MarchingCubes || renderingMode == RenderingMode.Hybrid)
            {
                GenerateMesh();
            }
            else if (renderingMode == RenderingMode.InstancingLarge ||
                     renderingMode == RenderingMode.InstancingSmall ||
                     renderingMode == RenderingMode.Hybrid)
            {
                PrepareSparseInstancing();
            }

            UpdateMaterialProperties();
        }

        private void OnDestroy()
        {
            // Release resources
            _colorVolume?.Release();
            _densityVolume?.Release();
            _meshVerticesBuffer?.Release();
            _meshTrianglesBuffer?.Release();
            _meshCountBuffer?.Release();
            _instanceDataBuffer?.Release();
            _drawArgsBuffer?.Release();

            // Unregister from URP
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnDrawGizmos()
        {
            if (showBounds && simulation != null)
            {
                Gizmos.color = Color.green;
                Vector3 size = new Vector3(
                    simulation.GridDimensions.x,
                    simulation.GridDimensions.y,
                    simulation.GridDimensions.z
                );
                Gizmos.DrawWireCube(transform.position, size);
            }
        }
        #endregion

        #region Rendering Methods
        private void UpdateLOD()
        {
            if (!enableLOD || _mainCamera == null)
                return;

            float distanceToCamera = Vector3.Distance(transform.position, _mainCamera.transform.position);
            float cameraMovementMagnitude = Vector3.Distance(_mainCamera.transform.position, _lastCameraPosition);

            // Update LOD based on distance and camera movement
            _currentLOD = Mathf.Clamp(distanceToCamera / lodDistance, 0, lodLevels - 1) * lodBias;

            // If camera is moving quickly, reduce detail further
            if (cameraMovementMagnitude > 1.0f)
            {
                _currentLOD += cameraMovementMagnitude * 0.1f;
            }

            _lastCameraPosition = _mainCamera.transform.position;

            // Update rendering parameters based on LOD
            rayStepSize = Mathf.Lerp(0.5f, 2.0f, _currentLOD / (lodLevels - 1));
            maxRaySteps = Mathf.RoundToInt(Mathf.Lerp(128, 32, _currentLOD / (lodLevels - 1)));
        }

        private void PrepareVolumeData()
        {
            // Set dynamic parameters
            renderingComputeShader.SetBool("ShowTemperature", showTemperature);
            renderingComputeShader.SetBool("ShowPressure", showPressure);
            renderingComputeShader.SetFloat("CurrentLOD", _currentLOD);
            renderingComputeShader.SetBool("EnableEmission", enableLighting);

            // Prepare volume data
            uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
            renderingComputeShader.GetKernelThreadGroupSizes(
                _prepareVolumeKernel,
                out threadGroupSizeX,
                out threadGroupSizeY,
                out threadGroupSizeZ
            );

            renderingComputeShader.Dispatch(
                _prepareVolumeKernel,
                Mathf.CeilToInt(simulation.GridDimensions.x / (float)threadGroupSizeX),
                Mathf.CeilToInt(simulation.GridDimensions.y / (float)threadGroupSizeY),
                Mathf.CeilToInt(simulation.GridDimensions.z / (float)threadGroupSizeZ)
            );
        }

        private void GenerateMesh()
        {
            // Reset counters
            int[] resetCounters = { 0, 0 }; // vertices, triangles
            _meshCountBuffer.SetData(resetCounters);

            // Set dynamic parameters
            renderingComputeShader.SetFloat("IsoLevel", densityThreshold);

            // Run marching cubes algorithm
            uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
            renderingComputeShader.GetKernelThreadGroupSizes(
                _marchingCubesKernel,
                out threadGroupSizeX,
                out threadGroupSizeY,
                out threadGroupSizeZ
            );

            renderingComputeShader.Dispatch(
                _marchingCubesKernel,
                Mathf.CeilToInt(simulation.GridDimensions.x / (float)threadGroupSizeX),
                Mathf.CeilToInt(simulation.GridDimensions.y / (float)threadGroupSizeY),
                Mathf.CeilToInt(simulation.GridDimensions.z / (float)threadGroupSizeZ)
            );

            // Read back counter values
            int[] counters = new int[2];
            _meshCountBuffer.GetData(counters);
            int vertexCount = Mathf.Min(counters[0], MAX_VERTICES);
            int triangleCount = Mathf.Min(counters[1], MAX_TRIANGLES);

            _lastRenderedTriangles = triangleCount;

            if (vertexCount == 0 || triangleCount == 0)
                return;

            // Read data from buffers
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[triangleCount * 3];

            // Temporary buffer to read combined vertex data
            Vector3[] tempVertices = new Vector3[vertexCount];
            Vector3[] tempNormals = new Vector3[vertexCount];
            Color[] tempColors = new Color[vertexCount];

            // Clear mesh
            _generatedMesh.Clear();

            // Set mesh data
            _generatedMesh.SetVertices(vertices, 0, vertexCount);
            _generatedMesh.SetNormals(normals, 0, vertexCount);
            _generatedMesh.SetColors(colors, 0, vertexCount);
            _generatedMesh.SetTriangles(triangles, 0, triangleCount * 3, 0);

            // Recalculate mesh bounds
            _generatedMesh.RecalculateBounds();
        }

        private void PrepareSparseInstancing()
        {
            // Instead of marching cubes, we use instanced rendering for visible voxels
            // This is much faster for large simulations but less accurate for surface representation

            // Use a compute shader to find visible particles
            int findVisibleParticlesKernel = renderingComputeShader.FindKernel("FindVisibleParticles");

            // Reset counters
            uint[] args = new uint[] { 0, 0, 0, 0, 0 };
            args[0] = (particleMesh != null) ? (uint)particleMesh.GetIndexCount(0) : 0;
            args[1] = 0; // Instance count, will be filled by compute shader

            _drawArgsBuffer.SetData(args);

            // Set buffer bindings
            renderingComputeShader.SetBuffer(findVisibleParticlesKernel, "InstanceData", _instanceDataBuffer);
            renderingComputeShader.SetBuffer(findVisibleParticlesKernel, "DrawArgs", _drawArgsBuffer);
            renderingComputeShader.SetTexture(findVisibleParticlesKernel, "VoxelGrid", simulation.VoxelGridTexture);
            renderingComputeShader.SetTexture(findVisibleParticlesKernel, "ColorVolume", _colorVolume);
            renderingComputeShader.SetTexture(findVisibleParticlesKernel, "DensityVolume", _densityVolume);

            // Set camera parameters for visibility culling
            if (_mainCamera != null)
            {
                renderingComputeShader.SetVector("CameraPosition", _mainCamera.transform.position);
                renderingComputeShader.SetVector("CameraForward", _mainCamera.transform.forward);
                renderingComputeShader.SetFloat("CameraFOV", _mainCamera.fieldOfView);
                renderingComputeShader.SetFloat("MaxRenderDistance", maxRenderDistance);
            }

            // Set particle size based on rendering mode
            float particleScale = (renderingMode == RenderingMode.InstancingSmall) ? 0.8f : 1.0f;
            renderingComputeShader.SetFloat("ParticleScale", particleScale);
            renderingComputeShader.SetFloat("DensityCutoff", densityThreshold);

            // Run compute shader to find visible particles
            uint threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ;
            renderingComputeShader.GetKernelThreadGroupSizes(
                findVisibleParticlesKernel,
                out threadGroupSizeX,
                out threadGroupSizeY,
                out threadGroupSizeZ
            );

            renderingComputeShader.Dispatch(
                findVisibleParticlesKernel,
                Mathf.CeilToInt(simulation.GridDimensions.x / (float)threadGroupSizeX),
                Mathf.CeilToInt(simulation.GridDimensions.y / (float)threadGroupSizeY),
                Mathf.CeilToInt(simulation.GridDimensions.z / (float)threadGroupSizeZ)
            );

            // Read back instance count for stats
            uint[] drawArgs = new uint[5];
            _drawArgsBuffer.GetData(drawArgs);
            _lastRenderedParticles = (int)drawArgs[1];

            // Set instance data to material
            instancedParticleMaterial.SetBuffer("_InstanceData", _instanceDataBuffer);
        }

        private void UpdateMaterialProperties()
        {
            // Update volume render material
            volumeRenderMaterial.SetVector("_VolumePosition", transform.position);
            volumeRenderMaterial.SetFloat("_StepSize", rayStepSize);
            volumeRenderMaterial.SetFloat("_DensityThreshold", densityThreshold);
            volumeRenderMaterial.SetFloat("_MaxSteps", maxRaySteps);
            volumeRenderMaterial.SetFloat("_HeatDistortion", heatDistortion);
            volumeRenderMaterial.SetFloat("_HeatThreshold", heatThreshold);
            volumeRenderMaterial.SetFloat("_EmissiveIntensity", emissiveIntensity);
            volumeRenderMaterial.SetFloat("_LODLevel", _currentLOD);

            // Update instanced material
            instancedParticleMaterial.SetFloat("_EmissiveStrength", emissiveIntensity);
            instancedParticleMaterial.SetFloat("_HeatThreshold", heatThreshold);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _mainCamera)
                return;

            switch (renderingMode)
            {
                case RenderingMode.Raymarching:
                    // Raymarching is done via the custom render pass
                    break;

                case RenderingMode.InstancingLarge:
                case RenderingMode.InstancingSmall:
                    // Render particles using GPU instancing
                    if (particleMesh != null)
                    {
                        Graphics.DrawMeshInstancedIndirect(
                            particleMesh,
                            0,
                            instancedParticleMaterial,
                            new Bounds(transform.position, Vector3.one * simulation.GridDimensions.magnitude),
                            _drawArgsBuffer,
                            0,
                            null,
                            ShadowCastingMode.On,
                            true
                        );
                    }
                    break;

                case RenderingMode.MarchingCubes:
                    // Render generated mesh
                    if (_generatedMesh != null && _generatedMesh.vertexCount > 0)
                    {
                        Graphics.DrawMesh(
                            _generatedMesh,
                            transform.localToWorldMatrix,
                            instancedParticleMaterial,
                            0,
                            camera,
                            0,
                            null,
                            ShadowCastingMode.On,
                            true
                        );
                    }
                    break;

                case RenderingMode.Hybrid:
                    // Hybrid approach uses both techniques based on distance
                    float distanceToCamera = Vector3.Distance(transform.position, camera.transform.position);

                    if (distanceToCamera < lodDistance * 0.5f)
                    {
                        // Near: Use marching cubes for better quality
                        if (_generatedMesh != null && _generatedMesh.vertexCount > 0)
                        {
                            Graphics.DrawMesh(
                                _generatedMesh,
                                transform.localToWorldMatrix,
                                instancedParticleMaterial,
                                0,
                                camera,
                                0,
                                null,
                                ShadowCastingMode.On,
                                true
                            );
                        }
                    }
                    else
                    {
                        // Far: Use particle instancing for performance
                        if (particleMesh != null)
                        {
                            Graphics.DrawMeshInstancedIndirect(
                                particleMesh,
                                0,
                                instancedParticleMaterial,
                                new Bounds(transform.position, Vector3.one * simulation.GridDimensions.magnitude),
                                _drawArgsBuffer,
                                0,
                                null,
                                ShadowCastingMode.On,
                                true
                            );
                        }
                    }
                    break;
            }
        }
        #endregion

        #region Helper Classes
        /// <summary>
        /// Custom render pass for volume rendering.
        /// </summary>
        public class CustomRenderPass : ScriptableRenderPass
        {
            private VolumeRenderer _volumeRenderer;
            private RenderTargetIdentifier _cameraColorTarget;

            public CustomRenderPass(VolumeRenderer renderer)
            {
                _volumeRenderer = renderer;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            public void Setup(RenderTargetIdentifier cameraColorTarget)
            {
                _cameraColorTarget = cameraColorTarget;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_volumeRenderer.renderingMode != RenderingMode.Raymarching)
                    return;

                // Command buffer for raymarching
                CommandBuffer cmd = CommandBufferPool.Get("Volume Raymarching");

                // Set camera parameters to shader
                _volumeRenderer.volumeRenderMaterial.SetMatrix("_CameraInverseProjection",
                                                           renderingData.cameraData.camera.projectionMatrix.inverse);
                _volumeRenderer.volumeRenderMaterial.SetMatrix("_CameraToWorld",
                                                           renderingData.cameraData.camera.cameraToWorldMatrix);
                _volumeRenderer.volumeRenderMaterial.SetVector("_CameraPosition",
                                                           renderingData.cameraData.camera.transform.position);

                // Draw full-screen quad with raymarching shader
                cmd.Blit(null, _cameraColorTarget, _volumeRenderer.volumeRenderMaterial);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the currently active rendering mode.
        /// </summary>
        public void SetRenderingMode(RenderingMode mode)
        {
            renderingMode = mode;
        }

        /// <summary>
        /// Returns performance statistics.
        /// </summary>
        public (int, int, float) GetRenderStats()
        {
            return (_lastRenderedParticles, _lastRenderedTriangles, _lastRenderTime);
        }

        /// <summary>
        /// Toggles temperature visualization.
        /// </summary>
        public void ToggleTemperatureView(bool show)
        {
            showTemperature = show;
        }

        /// <summary>
        /// Toggles pressure visualization.
        /// </summary>
        public void TogglePressureView(bool show)
        {
            showPressure = show;
        }
        #endregion
    }
}