using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering.Universal;

namespace PowderToy3D
{
    /// <summary>
    /// Handles user interaction with the 3D Powder Toy simulation.
    /// Manages tools, element selection, and input processing.
    /// </summary>
    public class UserInteractionController : MonoBehaviour
    {
        #region Serialized Fields
        [Header("Simulation References")]
        [SerializeField] private SimulationController simulation;
        [SerializeField] private VolumeRenderer volumeRenderer;
        [SerializeField] private Camera mainCamera;

        [Header("Input Settings")]
        [SerializeField] private LayerMask simulationLayer;
        [SerializeField] private float raycastDistance = 1000f;
        [SerializeField] private float brushFalloffExponent = 2.0f;

        [Header("Tools")]
        [SerializeField] private ToolType currentTool = ToolType.AddElement;
        [SerializeField] private ElementType currentElement = ElementType.Sand;
        [SerializeField] private float brushSize = 5.0f;
        [SerializeField] private float brushIntensity = 1.0f;
        [SerializeField] private float heatTemperature = 500.0f;
        [SerializeField] private float pressureAmount = 5.0f;
        [SerializeField] private float shootVelocity = 10.0f;
        [SerializeField] private bool randomizeColor = false;
        [SerializeField] private bool useGaussianBrush = true;

        [Header("Camera Control")]
        [SerializeField] private float cameraMoveSpeed = 10.0f;
        [SerializeField] private float cameraRotationSpeed = 100.0f;
        [SerializeField] private float cameraDragSpeed = 0.3f;
        [SerializeField] private float cameraZoomSpeed = 5.0f;
        [SerializeField] private float minZoomDistance = 10.0f;
        [SerializeField] private float maxZoomDistance = 500.0f;

        [Header("UI References")]
        [SerializeField] private GameObject uiContainer;
        [SerializeField] private TMP_Dropdown toolDropdown;
        [SerializeField] private TMP_Dropdown elementDropdown;
        [SerializeField] private Slider brushSizeSlider;
        [SerializeField] private Slider brushIntensitySlider;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private TMP_Dropdown renderModeDropdown;
        [SerializeField] private Toggle temperatureViewToggle;
        [SerializeField] private Toggle pressureViewToggle;
        [SerializeField] private Slider heatSlider;
        [SerializeField] private Slider pressureSlider;
        [SerializeField] private TextMeshProUGUI statsText;
        [SerializeField] private GameObject elementInfoPanel;
        [SerializeField] private TextMeshProUGUI elementInfoText;
        [SerializeField] private Image currentElementImage;
        #endregion

        #region Enums
        /// <summary>
        /// Available tool types for interacting with the simulation.
        /// </summary>
        public enum ToolType
        {
            AddElement,     // Add elements
            Erase,          // Remove elements
            Heat,           // Add heat
            Cool,           // Remove heat
            Pressure,       // Add pressure
            Vacuum,         // Remove pressure
            Shoot,          // Shoot elements with velocity
            Attract,        // Pull elements towards cursor
            Repel,          // Push elements away from cursor
            Sample,         // Sample element under cursor
            Barrier,        // Create indestructible walls
            Lightning       // Create electricity
        }
        #endregion

        #region Private Fields
        // Input state tracking
        private bool _isLeftMouseDown = false;
        private bool _isRightMouseDown = false;
        private bool _isMiddleMouseDown = false;
        private bool _isShiftDown = false;
        private bool _isControlDown = false;
        private bool _isAltDown = false;
        private Vector3 _lastMousePosition;
        private Vector3 _dragStartPosition;
        private bool _isDragging = false;

        // Camera control
        private Vector3 _cameraTargetPosition;
        private Quaternion _cameraTargetRotation;
        private float _cameraTargetZoom;
        private Transform _cameraTransform;
        private float _currentZoomDistance;

        // Ray and hit detection
        private Ray _mouseRay;
        private RaycastHit _mouseHit;
        private Vector3 _hitPosition;
        private Vector3Int _lastGridPosition = new Vector3Int(-1, -1, -1);

        // UI state
        private bool _showUI = true;
        private string _saveLoadPath;
        private float _nextStatsUpdateTime;
        private float _statsUpdateInterval = 0.5f;

        // Element categories for UI organization
        private Dictionary<string, List<ElementType>> _elementCategories = new Dictionary<string, List<ElementType>>();

        // History for undo/redo
        private Stack<SimulationHistoryState> _undoStack = new Stack<SimulationHistoryState>();
        private Stack<SimulationHistoryState> _redoStack = new Stack<SimulationHistoryState>();
        private const int MAX_HISTORY_STATES = 10;
        #endregion

        #region Unity Lifecycle Methods
        private void Awake()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;

            _cameraTransform = mainCamera.transform;
            _currentZoomDistance = Vector3.Distance(_cameraTransform.position, simulation.transform.position);
            _cameraTargetPosition = _cameraTransform.position;
            _cameraTargetRotation = _cameraTransform.rotation;
            _cameraTargetZoom = _currentZoomDistance;

            // Initialize save path
            _saveLoadPath = Path.Combine(Application.persistentDataPath, "PowderToy3D");
            if (!Directory.Exists(_saveLoadPath))
            {
                Directory.CreateDirectory(_saveLoadPath);
            }

            // Setup element categories
            InitializeElementCategories();
        }

        private void Start()
        {
            // Initialize UI
            InitializeUI();
        }

        private void Update()
        {
            UpdateInputState();
            HandleCameraControls();

            if (!EventSystem.current.IsPointerOverGameObject()) // Ignore UI interaction
            {
                HandleToolInput();
            }

            // Update UI
            if (Time.time >= _nextStatsUpdateTime)
            {
                UpdateStatsUI();
                _nextStatsUpdateTime = Time.time + _statsUpdateInterval;
            }

            // Toggle UI with Tab key
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _showUI = !_showUI;
                uiContainer.SetActive(_showUI);
            }

            // Undo/Redo
            if (_isControlDown && Input.GetKeyDown(KeyCode.Z) && _undoStack.Count > 0)
            {
                PerformUndo();
            }
            else if (_isControlDown && Input.GetKeyDown(KeyCode.Y) && _redoStack.Count > 0)
            {
                PerformRedo();
            }

            // Quick save with F5
            if (Input.GetKeyDown(KeyCode.F5))
            {
                QuickSave();
            }

            // Quick load with F9
            if (Input.GetKeyDown(KeyCode.F9))
            {
                QuickLoad();
            }
        }
        #endregion

        #region Input Handling
        private void UpdateInputState()
        {
            // Track mouse buttons
            _isLeftMouseDown = Input.GetMouseButton(0);
            _isRightMouseDown = Input.GetMouseButton(1);
            _isMiddleMouseDown = Input.GetMouseButton(2);

            // Track modifier keys
            _isShiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            _isControlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            _isAltDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // Track mouse position
            Vector3 currentMousePosition = Input.mousePosition;

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                _dragStartPosition = currentMousePosition;
                _isDragging = false;
            }

            if (Vector3.Distance(currentMousePosition, _dragStartPosition) > 5.0f)
            {
                _isDragging = true;
            }

            _lastMousePosition = currentMousePosition;

            // Cast ray from mouse position
            _mouseRay = mainCamera.ScreenPointToRay(currentMousePosition);
        }

        private void HandleCameraControls()
        {
            // WASD movement
            Vector3 moveDirection = Vector3.zero;

            if (Input.GetKey(KeyCode.W))
                moveDirection += _cameraTransform.forward;
            if (Input.GetKey(KeyCode.S))
                moveDirection -= _cameraTransform.forward;
            if (Input.GetKey(KeyCode.A))
                moveDirection -= _cameraTransform.right;
            if (Input.GetKey(KeyCode.D))
                moveDirection += _cameraTransform.right;
            if (Input.GetKey(KeyCode.E))
                moveDirection += Vector3.up;
            if (Input.GetKey(KeyCode.Q))
                moveDirection += Vector3.down;

            moveDirection.Normalize();
            _cameraTargetPosition += moveDirection * cameraMoveSpeed * Time.deltaTime;

            // Middle mouse drag for camera rotation
            if (_isMiddleMouseDown && _isDragging)
            {
                Vector3 mouseDelta = Input.mousePosition - _lastMousePosition;

                // Rotate camera
                float rotationX = _cameraTargetRotation.eulerAngles.x;
                float rotationY = _cameraTargetRotation.eulerAngles.y;

                rotationY += mouseDelta.x * cameraRotationSpeed * Time.deltaTime;
                rotationX -= mouseDelta.y * cameraRotationSpeed * Time.deltaTime;

                // Clamp vertical rotation to avoid flipping
                rotationX = ClampAngle(rotationX, -89f, 89f);

                _cameraTargetRotation = Quaternion.Euler(rotationX, rotationY, 0);
            }

            // Right mouse drag for camera panning
            if (_isRightMouseDown && _isDragging && !_isControlDown)
            {
                Vector3 mouseDelta = Input.mousePosition - _lastMousePosition;
                Vector3 moveOffset = _cameraTransform.right * -mouseDelta.x * cameraDragSpeed * 0.01f * _currentZoomDistance;
                moveOffset += _cameraTransform.up * -mouseDelta.y * cameraDragSpeed * 0.01f * _currentZoomDistance;

                _cameraTargetPosition += moveOffset;
            }

            // Mouse wheel zoom
            float scrollDelta = Input.mouseScrollDelta.y;
            if (scrollDelta != 0)
            {
                _cameraTargetZoom -= scrollDelta * cameraZoomSpeed;
                _cameraTargetZoom = Mathf.Clamp(_cameraTargetZoom, minZoomDistance, maxZoomDistance);
            }

            // Apply camera movements smoothly
            _cameraTransform.position = Vector3.Lerp(_cameraTransform.position, _cameraTargetPosition, Time.deltaTime * 10f);
            _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, _cameraTargetRotation, Time.deltaTime * 10f);

            // Apply zoom by adjusting distance from target
            Vector3 cameraDirection = _cameraTransform.forward;
            float targetDistance = Mathf.Lerp(_currentZoomDistance, _cameraTargetZoom, Time.deltaTime * 10f);

            // Update distance
            _currentZoomDistance = targetDistance;
        }

        private float ClampAngle(float angle, float min, float max)
        {
            if (angle < 0f)
                angle += 360f;
            if (angle > 360f)
                angle -= 360f;

            return Mathf.Clamp(angle, min, max);
        }

        private void HandleToolInput()
        {
            // Cast ray to find interaction point
            bool rayHit = Physics.Raycast(_mouseRay, out _mouseHit, raycastDistance, simulationLayer);

            if (rayHit)
            {
                _hitPosition = _mouseHit.point;

                // Convert to grid space
                Vector3Int gridPosition = simulation.WorldToGridPosition(_hitPosition);

                // Apply tool if mouse button is down
                if (_isLeftMouseDown)
                {
                    if (!gridPosition.Equals(_lastGridPosition))
                    {
                        ApplyTool(gridPosition, true);
                        _lastGridPosition = gridPosition;
                    }
                }
                // Alternative tool actions with right click
                else if (_isRightMouseDown && _isControlDown)
                {
                    if (!gridPosition.Equals(_lastGridPosition))
                    {
                        ApplyAlternativeTool(gridPosition);
                        _lastGridPosition = gridPosition;
                    }
                }
                else
                {
                    _lastGridPosition = new Vector3Int(-1, -1, -1);

                    // Show element info when hovering
                    float4 voxelData = GetVoxelData(gridPosition);
                    if (voxelData.x > 0)
                    {
                        UpdateElementInfoPanel(gridPosition, (ElementType)voxelData.x);
                    }
                    else
                    {
                        HideElementInfoPanel();
                    }
                }
            }
            else
            {
                _lastGridPosition = new Vector3Int(-1, -1, -1);
                HideElementInfoPanel();
            }

            // Process keyboard shortcuts for tools
            ProcessToolShortcuts();
        }

        private void ProcessToolShortcuts()
        {
            // Tool selection shortcuts
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetCurrentTool(ToolType.AddElement);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetCurrentTool(ToolType.Erase);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetCurrentTool(ToolType.Heat);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetCurrentTool(ToolType.Cool);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetCurrentTool(ToolType.Pressure);
            if (Input.GetKeyDown(KeyCode.Alpha6)) SetCurrentTool(ToolType.Vacuum);
            if (Input.GetKeyDown(KeyCode.Alpha7)) SetCurrentTool(ToolType.Shoot);
            if (Input.GetKeyDown(KeyCode.Alpha8)) SetCurrentTool(ToolType.Attract);
            if (Input.GetKeyDown(KeyCode.Alpha9)) SetCurrentTool(ToolType.Repel);
            if (Input.GetKeyDown(KeyCode.Alpha0)) SetCurrentTool(ToolType.Sample);

            // Brush size adjustment
            if (Input.GetKey(KeyCode.LeftBracket))
            {
                brushSize = Mathf.Max(1.0f, brushSize - 0.5f);
                UpdateBrushSizeUI();
            }
            if (Input.GetKey(KeyCode.RightBracket))
            {
                brushSize = Mathf.Min(50.0f, brushSize + 0.5f);
                UpdateBrushSizeUI();
            }

            // Simulation control
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
            }
            if (Input.GetKeyDown(KeyCode.R) && _isControlDown)
            {
                ResetSimulation();
            }
        }
        #endregion

        #region Tool Application
        private void ApplyTool(Vector3Int gridPosition, bool recordHistory)
        {
            if (recordHistory)
            {
                PushUndoState();
            }

            switch (currentTool)
            {
                case ToolType.AddElement:
                    ApplyElementBrush(gridPosition, (int)currentElement);
                    break;

                case ToolType.Erase:
                    ApplyEraser(gridPosition);
                    break;

                case ToolType.Heat:
                    ApplyHeatBrush(gridPosition, heatTemperature);
                    break;

                case ToolType.Cool:
                    ApplyHeatBrush(gridPosition, -heatTemperature);
                    break;

                case ToolType.Pressure:
                    ApplyPressureBrush(gridPosition, pressureAmount);
                    break;

                case ToolType.Vacuum:
                    ApplyPressureBrush(gridPosition, -pressureAmount);
                    break;

                case ToolType.Shoot:
                    ShootElement(gridPosition);
                    break;

                case ToolType.Attract:
                    ApplyForceField(gridPosition, true);
                    break;

                case ToolType.Repel:
                    ApplyForceField(gridPosition, false);
                    break;

                case ToolType.Sample:
                    SampleElement(gridPosition);
                    break;

                case ToolType.Barrier:
                    ApplyElementBrush(gridPosition, (int)ElementType.Wall);
                    break;

                case ToolType.Lightning:
                    CreateLightning(gridPosition);
                    break;
            }
        }

        private void ApplyAlternativeTool(Vector3Int gridPosition)
        {
            switch (currentTool)
            {
                case ToolType.AddElement:
                    // Right-click samples element instead
                    SampleElement(gridPosition);
                    break;

                case ToolType.Shoot:
                    // Right-click shoots with random velocity
                    ShootElementRandom(gridPosition);
                    break;

                case ToolType.Heat:
                case ToolType.Cool:
                    // Right-click extreme temperature
                    ApplyHeatBrush(gridPosition, heatTemperature * 2.0f * Mathf.Sign(heatTemperature));
                    break;

                case ToolType.Pressure:
                case ToolType.Vacuum:
                    // Right-click extreme pressure
                    ApplyPressureBrush(gridPosition, pressureAmount * 2.0f * Mathf.Sign(pressureAmount));
                    break;

                case ToolType.Lightning:
                    // Right-click creates plasma instead
                    ApplyElementBrush(gridPosition, (int)ElementType.Plasma);
                    break;

                default:
                    // Default to sampling
                    SampleElement(gridPosition);
                    break;
            }
        }

        private void ApplyElementBrush(Vector3Int centerPosition, int elementId)
        {
            // Apply elements in a brush pattern
            int radius = Mathf.CeilToInt(brushSize);

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        Vector3Int offset = new Vector3Int(x, y, z);
                        Vector3Int position = centerPosition + offset;

                        // Calculate distance for falloff
                        float distance = offset.magnitude;

                        if (distance <= brushSize)
                        {
                            // Calculate falloff factor (1 at center, 0 at edge)
                            float falloff;

                            if (useGaussianBrush)
                            {
                                // Gaussian falloff
                                falloff = Mathf.Exp(-(distance * distance) / (2 * brushSize * brushSize));
                            }
                            else
                            {
                                // Power falloff
                                falloff = 1.0f - Mathf.Pow(distance / brushSize, brushFalloffExponent);
                            }

                            // Randomize based on intensity and falloff
                            if (Random.value < falloff * brushIntensity)
                            {
                                // Add element with zero initial velocity
                                simulation.AddElement(elementId, position, 1.0f, Vector3.zero);
                            }
                        }
                    }
                }
            }
        }

        private void ApplyEraser(Vector3Int centerPosition)
        {
            // Erase elements in a brush pattern
            simulation.EraseElements(centerPosition, brushSize);
        }

        private void ApplyHeatBrush(Vector3Int centerPosition, float temperature)
        {
            // Apply heat/cold in a brush pattern
            simulation.AddHeat(centerPosition, brushSize, temperature);
        }

        private void ApplyPressureBrush(Vector3Int centerPosition, float pressure)
        {
            // Apply pressure in a brush pattern
            simulation.AddPressure(centerPosition, brushSize, pressure);
        }

        private void ShootElement(Vector3Int position)
        {
            // Calculate velocity vector from camera forward
            Vector3 velocity = mainCamera.transform.forward * shootVelocity;

            // Add element with velocity
            simulation.AddElement((int)currentElement, position, brushSize, velocity);
        }

        private void ShootElementRandom(Vector3Int position)
        {
            // Create random velocity
            Vector3 velocity = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f)
            ).normalized * shootVelocity;

            // Add element with velocity
            simulation.AddElement((int)currentElement, position, brushSize, velocity);
        }

        private void ApplyForceField(Vector3Int centerPosition, bool attract)
        {
            // TODO: Implement force field to attract/repel particles
            // This requires modification of the simulation shader
            // For now, we'll just apply some pressure

            float pressureValue = attract ? -pressureAmount : pressureAmount;
            ApplyPressureBrush(centerPosition, pressureValue);
        }

        private void SampleElement(Vector3Int position)
        {
            // Get element at position
            float4 voxelData = GetVoxelData(position);
            int elementId = (int)voxelData.x;

            if (elementId > 0)
            {
                // Set current element to sampled type
                SetCurrentElement((ElementType)elementId);
            }
        }

        private void CreateLightning(Vector3Int startPosition)
        {
            // Create lightning effect from start point to target or random direction
            Vector3Int endPosition;

            if (_isAltDown)
            {
                // Random target point
                endPosition = new Vector3Int(
                    startPosition.x + Random.Range(-20, 20),
                    startPosition.y + Random.Range(-20, 20),
                    startPosition.z + Random.Range(-20, 20)
                );
            }
            else
            {
                // Target in camera forward direction
                Vector3 targetWorld = _hitPosition + mainCamera.transform.forward * 20f;
                endPosition = simulation.WorldToGridPosition(targetWorld);
            }

            // Draw lightning path
            DrawLightningBolt(startPosition, endPosition);
        }

        private void DrawLightningBolt(Vector3Int start, Vector3Int end)
        {
            List<Vector3Int> lightningPoints = new List<Vector3Int>();
            lightningPoints.Add(start);

            // Generate lightning path with random offsets
            Vector3 currentPosition = start;

            // FIX: Convert Vector3Int to Vector3 before normalization
            Vector3 startPos = new Vector3(start.x, start.y, start.z);
            Vector3 endPos = new Vector3(end.x, end.y, end.z);
            Vector3 targetDirection = (endPos - startPos).normalized;

            float totalDistance = Vector3.Distance(start, end);
            float segmentLength = totalDistance / 10f;

            while (Vector3.Distance(currentPosition, end) > segmentLength)
            {
                // Calculate next point along path with random deviation
                Vector3 randomOffset = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                ) * segmentLength * 0.5f;

                currentPosition += (targetDirection * segmentLength) + randomOffset;
                lightningPoints.Add(Vector3Int.RoundToInt(currentPosition));
            }

            lightningPoints.Add(end);

            // Draw lightning by adding electricity elements
            for (int i = 0; i < lightningPoints.Count; i++)
            {
                simulation.AddElement((int)ElementType.Electricity, lightningPoints[i], 1.0f, Vector3.zero);

                // Add branches with probability
                if (i > 0 && i < lightningPoints.Count - 1 && Random.value < 0.3f)
                {
                    Vector3Int branchStart = lightningPoints[i];
                    Vector3Int branchEnd = branchStart + new Vector3Int(
                        Random.Range(-10, 10),
                        Random.Range(-10, 10),
                        Random.Range(-10, 10)
                    );

                    DrawLightningBranch(branchStart, branchEnd, 2);
                }
            }
        }

        private void DrawLightningBranch(Vector3Int start, Vector3Int end, int depth)
        {
            if (depth <= 0) return;

            List<Vector3Int> branchPoints = new List<Vector3Int>();
            branchPoints.Add(start);

            // Generate shorter branch with more randomness
            Vector3 currentPosition = start;

            // FIX: Convert Vector3Int to Vector3 before normalization
            Vector3 startPos = new Vector3(start.x, start.y, start.z);
            Vector3 endPos = new Vector3(end.x, end.y, end.z);
            Vector3 targetDirection = (endPos - startPos).normalized;

            float totalDistance = Vector3.Distance(start, end);
            float segmentLength = totalDistance / 5f;

            while (Vector3.Distance(currentPosition, end) > segmentLength)
            {
                // Calculate next point along path with random deviation
                Vector3 randomOffset = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                ) * segmentLength * 0.8f;

                currentPosition += (targetDirection * segmentLength) + randomOffset;
                branchPoints.Add(Vector3Int.RoundToInt(currentPosition));
            }

            branchPoints.Add(end);

            // Draw branch with electricity elements
            for (int i = 0; i < branchPoints.Count; i++)
            {
                simulation.AddElement((int)ElementType.Electricity, branchPoints[i], 0.5f, Vector3.zero);

                // Add sub-branches with probability
                if (i > 0 && i < branchPoints.Count - 1 && Random.value < 0.3f)
                {
                    Vector3Int subBranchStart = branchPoints[i];
                    Vector3Int subBranchEnd = subBranchStart + new Vector3Int(
                        Random.Range(-5, 5),
                        Random.Range(-5, 5),
                        Random.Range(-5, 5)
                    );

                    DrawLightningBranch(subBranchStart, subBranchEnd, depth - 1);
                }
            }
        }

        private float4 GetVoxelData(Vector3Int position)
        {
            // This is a placeholder. In a real implementation, we would read the voxel data from the GPU
            // using a compute buffer. For now, we'll fake it by checking the element at position.

            // TODO: Implement proper voxel data reading from GPU

            // Placeholder implementation
            return new float4((float)ElementType.Empty, 0, 0, 0);
        }
        #endregion

        #region UI Methods
        private void InitializeUI()
        {
            // Set up UI callbacks
            if (toolDropdown != null)
            {
                toolDropdown.onValueChanged.AddListener(OnToolDropdownChanged);
                PopulateToolDropdown();
            }

            if (elementDropdown != null)
            {
                elementDropdown.onValueChanged.AddListener(OnElementDropdownChanged);
                PopulateElementDropdown();
            }

            if (brushSizeSlider != null)
            {
                brushSizeSlider.onValueChanged.AddListener(OnBrushSizeChanged);
                brushSizeSlider.value = brushSize;
            }

            if (brushIntensitySlider != null)
            {
                brushIntensitySlider.onValueChanged.AddListener(OnBrushIntensityChanged);
                brushIntensitySlider.value = brushIntensity;
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(TogglePause);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(ResetSimulation);
            }

            if (saveButton != null)
            {
                saveButton.onClick.AddListener(ShowSaveDialog);
            }

            if (loadButton != null)
            {
                loadButton.onClick.AddListener(ShowLoadDialog);
            }

            if (renderModeDropdown != null)
            {
                renderModeDropdown.onValueChanged.AddListener(OnRenderModeChanged);
                PopulateRenderModeDropdown();
            }

            if (temperatureViewToggle != null)
            {
                temperatureViewToggle.onValueChanged.AddListener(OnTemperatureViewToggled);
            }

            if (pressureViewToggle != null)
            {
                pressureViewToggle.onValueChanged.AddListener(OnPressureViewToggled);
            }

            if (heatSlider != null)
            {
                heatSlider.onValueChanged.AddListener(OnHeatTemperatureChanged);
                heatSlider.value = heatTemperature;
            }

            if (pressureSlider != null)
            {
                pressureSlider.onValueChanged.AddListener(OnPressureAmountChanged);
                pressureSlider.value = pressureAmount;
            }

            // Hide element info panel initially
            HideElementInfoPanel();
        }

        private void PopulateToolDropdown()
        {
            toolDropdown.ClearOptions();

            List<string> options = new List<string>();
            foreach (ToolType tool in System.Enum.GetValues(typeof(ToolType)))
            {
                options.Add(tool.ToString());
            }

            toolDropdown.AddOptions(options);
            toolDropdown.value = (int)currentTool;
            toolDropdown.RefreshShownValue();
        }

        private void InitializeElementCategories()
        {
            // Organize elements by category
            _elementCategories.Clear();

            _elementCategories["Solids"] = new List<ElementType> {
                ElementType.Wall, ElementType.Wood, ElementType.Metal, ElementType.Glass,
                ElementType.Stone, ElementType.Ice, ElementType.C4
            };

            _elementCategories["Powders"] = new List<ElementType> {
                ElementType.Sand, ElementType.Salt, ElementType.Coal, ElementType.Gunpowder,
                ElementType.Snow, ElementType.Concrete
            };

            _elementCategories["Liquids"] = new List<ElementType> {
                ElementType.Water, ElementType.Oil, ElementType.Acid, ElementType.Lava,
                ElementType.Gel, ElementType.Slime, ElementType.Mercury
            };

            _elementCategories["Gases"] = new List<ElementType> {
                ElementType.Steam, ElementType.Smoke, ElementType.Fire, ElementType.Methane
            };

            _elementCategories["Special"] = new List<ElementType> {
                ElementType.Electricity, ElementType.Plasma, ElementType.Neutron, ElementType.Biomass
            };
        }

        private void PopulateElementDropdown()
        {
            elementDropdown.ClearOptions();

            // Add category headers and elements
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();

            foreach (var category in _elementCategories)
            {
                // Add category header
                TMP_Dropdown.OptionData categoryOption = new TMP_Dropdown.OptionData(category.Key);
                options.Add(categoryOption);

                // Add elements in category
                foreach (ElementType element in category.Value)
                {
                    TMP_Dropdown.OptionData elementOption = new TMP_Dropdown.OptionData("  " + element.ToString());
                    options.Add(elementOption);

                    // Match the current element
                    if (element == currentElement)
                    {
                        elementDropdown.value = options.Count - 1;
                    }
                }
            }

            elementDropdown.options = options;
            elementDropdown.RefreshShownValue();
        }

        private void PopulateRenderModeDropdown()
        {
            renderModeDropdown.ClearOptions();

            List<string> options = new List<string>();
            foreach (VolumeRenderer.RenderingMode mode in System.Enum.GetValues(typeof(VolumeRenderer.RenderingMode)))
            {
                options.Add(mode.ToString());
            }

            renderModeDropdown.AddOptions(options);
            renderModeDropdown.RefreshShownValue();
        }

        private void UpdateStatsUI()
        {
            if (statsText == null) return;

            // Get simulation stats
            int elementCount = 0; // TODO: Get actual element count
            int fps = Mathf.RoundToInt(1.0f / Time.smoothDeltaTime);

            // Get renderer stats
            (int particles, int triangles, float renderTime) = volumeRenderer.GetRenderStats();

            // Update text
            statsText.text = string.Format(
                "FPS: {0}\nElements: {1}\nTriangles: {2}\nParticles: {3}",
                fps, elementCount, triangles, particles
            );
        }

        private void UpdateBrushSizeUI()
        {
            if (brushSizeSlider != null)
            {
                brushSizeSlider.value = brushSize;
            }
        }

        private void UpdateElementInfoPanel(Vector3Int position, ElementType elementType)
        {
            if (elementInfoPanel == null || elementInfoText == null)
                return;

            // Show element info panel
            elementInfoPanel.SetActive(true);

            // Get element temperature
            float temperature = 0; // TODO: Get actual temperature from the simulation
            float pressure = 0;    // TODO: Get actual pressure from the simulation

            // Update text
            elementInfoText.text = string.Format(
                "Element: {0}\nTemperature: {1:F1}°C\nPressure: {2:F2}",
                elementType.ToString(), temperature, pressure
            );

            // Position the panel near the cursor but not directly under it
            Vector3 screenPos = Input.mousePosition + new Vector3(10, -10, 0);
            elementInfoPanel.transform.position = screenPos;

            // Update element image color
            if (currentElementImage != null)
            {
                // TODO: Get actual element color
                currentElementImage.color = Color.white;
            }
        }

        private void HideElementInfoPanel()
        {
            if (elementInfoPanel != null)
            {
                elementInfoPanel.SetActive(false);
            }
        }
        #endregion

        #region UI Callbacks
        private void OnToolDropdownChanged(int index)
        {
            currentTool = (ToolType)index;
        }

        private void OnElementDropdownChanged(int index)
        {
            // Find which element was selected
            int currentIndex = 0;

            foreach (var category in _elementCategories)
            {
                // Skip category header
                currentIndex++;

                // Check elements in category
                foreach (ElementType element in category.Value)
                {
                    if (currentIndex == index)
                    {
                        currentElement = element;
                        return;
                    }
                    currentIndex++;
                }
            }
        }

        private void OnBrushSizeChanged(float value)
        {
            brushSize = value;
        }

        private void OnBrushIntensityChanged(float value)
        {
            brushIntensity = value;
        }

        private void OnHeatTemperatureChanged(float value)
        {
            heatTemperature = value;
        }

        private void OnPressureAmountChanged(float value)
        {
            pressureAmount = value;
        }

        private void OnRenderModeChanged(int index)
        {
            volumeRenderer.SetRenderingMode((VolumeRenderer.RenderingMode)index);
        }

        private void OnTemperatureViewToggled(bool show)
        {
            volumeRenderer.ToggleTemperatureView(show);
        }

        private void OnPressureViewToggled(bool show)
        {
            volumeRenderer.TogglePressureView(show);
        }

        public void SetCurrentTool(ToolType tool)
        {
            currentTool = tool;
            if (toolDropdown != null)
            {
                toolDropdown.value = (int)tool;
            }
        }

        public void SetCurrentElement(ElementType element)
        {
            currentElement = element;

            // Update UI to match
            PopulateElementDropdown();
        }

        private void TogglePause()
        {
            simulation.IsPaused = !simulation.IsPaused;

            // Update button text
            if (pauseButton != null && pauseButton.GetComponentInChildren<TextMeshProUGUI>() != null)
            {
                pauseButton.GetComponentInChildren<TextMeshProUGUI>().text =
                    simulation.IsPaused ? "Resume" : "Pause";
            }
        }

        private void ResetSimulation()
        {
            // Clear all voxel textures
            simulation.ClearVoxelTextures();
        }
        #endregion

        #region Save/Load System
        private void ShowSaveDialog()
        {
            // In a real application, you would show a file dialog
            // For now, we'll just use a quick save
            QuickSave();
        }

        private void ShowLoadDialog()
        {
            // In a real application, you would show a file dialog
            // For now, we'll just use a quick load
            QuickLoad();
        }

        private void QuickSave()
        {
            string savePath = Path.Combine(_saveLoadPath, "quicksave.pt3d");

            // Save simulation state
            byte[] saveData = simulation.SaveSimulationState();

            // Write to file
            File.WriteAllBytes(savePath, saveData);

            Debug.Log("Saved simulation to " + savePath);
        }

        private void QuickLoad()
        {
            string savePath = Path.Combine(_saveLoadPath, "quicksave.pt3d");

            if (File.Exists(savePath))
            {
                // Read file
                byte[] saveData = File.ReadAllBytes(savePath);

                // Load simulation state
                simulation.LoadSimulationState(saveData);

                Debug.Log("Loaded simulation from " + savePath);
            }
            else
            {
                Debug.LogWarning("No quicksave file found at " + savePath);
            }
        }
        #endregion

        #region Undo/Redo System
        private void PushUndoState()
        {
            // TODO: In a full implementation, capture a region around the edit
            // For now, just add a placeholder
            SimulationHistoryState state = new SimulationHistoryState();

            _undoStack.Push(state);
            _redoStack.Clear();

            // Limit stack size
            if (_undoStack.Count > MAX_HISTORY_STATES)
            {
                // Remove oldest state
                List<SimulationHistoryState> tempList = new List<SimulationHistoryState>(_undoStack);
                tempList.RemoveAt(0);
                _undoStack = new Stack<SimulationHistoryState>(tempList);
            }
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0)
                return;

            SimulationHistoryState state = _undoStack.Pop();
            _redoStack.Push(state);

            // TODO: Apply the undo state
            Debug.Log("Undo performed");
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0)
                return;

            SimulationHistoryState state = _redoStack.Pop();
            _undoStack.Push(state);

            // TODO: Apply the redo state
            Debug.Log("Redo performed");
        }
        #endregion

        #region Helper Classes
        /// <summary>
        /// Simple vector with 4 float components (similar to float4 in shader).
        /// </summary>
        public struct float4
        {
            public float x, y, z, w;

            public float4(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }
        }

        /// <summary>
        /// Represents a single state in the undo/redo history.
        /// </summary>
        private class SimulationHistoryState
        {
            public Vector3Int RegionStart;
            public Vector3Int RegionSize;
            public byte[] VoxelData;
            public byte[] TemperatureData;
            public byte[] PressureData;

            // In a real implementation, this would store region data for efficient undo/redo
        }
        #endregion
    }
}