using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowderToy3D
{
    /// <summary>
    /// Structure representing element properties for GPU simulation.
    /// This struct must match the layout in compute shaders.
    /// </summary>
    [System.Serializable]
    public struct ElementProperties
    {
        // Physical properties
        public float Density;          // kg/m³
        public float Viscosity;        // Resistance to flow
        public float Elasticity;       // Bounce coefficient
        public float Friction;         // Friction coefficient

        // State properties
        public int State;              // 0=Solid, 1=Powder, 2=Liquid, 3=Gas
        public float MeltingPoint;     // Temperature at which solid→liquid
        public float BoilingPoint;     // Temperature at which liquid→gas
        public float FreezingPoint;    // Temperature at which liquid→solid
        public float CondensationPoint; // Temperature at which gas→liquid

        // Thermal properties
        public float DefaultTemperature; // Default temperature
        public float ThermalConductivity; // Heat transfer rate
        public float SpecificHeat;     // Energy to raise 1kg by 1°C
        public float HeatProduction;   // Heat produced per second

        // Interaction properties
        public float Flammability;     // 0-1 chance to ignite
        public float BurnTemperature;  // Temperature at which it burns
        public float BurnRate;         // How quickly it burns

        // Electrical properties
        public float Conductivity;     // Electrical conductivity
        public float ChargeCapacity;   // Maximum charge it can hold

        // Reaction properties - Pairs of Element IDs and reaction strengths with up to 4 possible reactions
        public int ReactantElementId1;
        public float ReactionStrength1;
        public int ReactantElementId2;
        public float ReactionStrength2;
        public int ReactantElementId3;
        public float ReactionStrength3;
        public int ReactantElementId4;
        public float ReactionStrength4;

        // Result element when conditions are met
        public int MeltResultElementId;      // When melted
        public int FreezeResultElementId;    // When frozen
        public int BurnResultElementId;      // When burned
        public int EvaporateResultElementId; // When evaporated
        public int CondensingResultElementId; // When condensed

        // Special behavior flags
        public int SpecialFlags; // Bit field for special behaviors

        // Visualization properties (stored in integer format for shader)
        public uint ColorDefault;  // RGBA color in normal state
        public uint ColorHot;      // RGBA color when heated
        public uint ColorCold;     // RGBA color when cooled

        // Defines the memory layout size for ComputeBuffer
        public static int Stride => 32 * sizeof(float) + 10 * sizeof(int) + 3 * sizeof(uint);
    }

    /// <summary>
    /// Enum defining all possible element types.
    /// Must match elements defined in the ElementDatabase.
    /// </summary>
    public enum ElementType
    {
        Empty = 0,
        // Solids
        Wall = 1,
        Wood = 2,
        Metal = 3,
        Glass = 4,
        Stone = 5,
        Ice = 6,
        C4 = 7,
        // Powders
        Sand = 100,
        Salt = 101,
        Coal = 102,
        Gunpowder = 103,
        // Liquids
        Water = 200,
        Oil = 201,
        Acid = 202,
        Lava = 203,
        // Gases
        Steam = 300,
        Smoke = 301,
        Fire = 302,
        Methane = 303,
        // Special
        Electricity = 400,
        Plasma = 401,
        Neutron = 402,
        // New elements
        Snow = 500,
        Concrete = 501,
        Gel = 502,
        Slime = 503,
        Mercury = 504,
        Biomass = 505
    }

    /// <summary>
    /// Special behavior flags for elements.
    /// Each bit represents a different behavior.
    /// </summary>
    [Flags]
    public enum ElementFlags
    {
        None = 0,
        Explosive = 1 << 0,
        Radioactive = 1 << 1,
        Conductive = 1 << 2,
        Reactive = 1 << 3,
        Corrosive = 1 << 4,
        LightEmitter = 1 << 5,
        Magnetic = 1 << 6,
        Antigravity = 1 << 7,
        Indestructible = 1 << 8,
        Cloneable = 1 << 9,
        GrowsPlants = 1 << 10,
        SelfReplicating = 1 << 11,
        Sticky = 1 << 12,
        Bouncy = 1 << 13,
        Teleporting = 1 << 14,
        Quantum = 1 << 15
    }

    /// <summary>
    /// Database containing all element definitions and properties.
    /// </summary>
    public class ElementDatabase
    {
        private Dictionary<int, ElementProperties> _elementProperties;

        /// <summary>
        /// Initializes the element database with all available elements.
        /// </summary>
        public ElementDatabase()
        {
            _elementProperties = new Dictionary<int, ElementProperties>();
            InitializeElements();
        }

        /// <summary>
        /// Gets properties for a specific element.
        /// </summary>
        public ElementProperties GetElementProperties(int elementId)
        {
            if (_elementProperties.TryGetValue(elementId, out ElementProperties props))
            {
                return props;
            }

            // Return empty properties if element not found
            return _elementProperties[(int)ElementType.Empty];
        }

        /// <summary>
        /// Gets a list of all element properties in the database.
        /// </summary>
        public List<ElementProperties> GetAllElementProperties()
        {
            List<ElementProperties> allProps = new List<ElementProperties>();

            foreach (var pair in _elementProperties)
            {
                allProps.Add(pair.Value);
            }

            return allProps;
        }

        /// <summary>
        /// Returns all elements of a specific state.
        /// </summary>
        /// <param name="state">State to filter by (0=Solid, 1=Powder, 2=Liquid, 3=Gas)</param>
        public List<int> GetElementsByState(int state)
        {
            List<int> elements = new List<int>();

            foreach (var pair in _elementProperties)
            {
                if (pair.Value.State == state)
                {
                    elements.Add(pair.Key);
                }
            }

            return elements;
        }

        /// <summary>
        /// Converts color to packed uint format for shaders.
        /// </summary>
        private uint ColorToUint(Color color)
        {
            byte r = (byte)(color.r * 255);
            byte g = (byte)(color.g * 255);
            byte b = (byte)(color.b * 255);
            byte a = (byte)(color.a * 255);

            return (uint)((a << 24) | (b << 16) | (g << 8) | r);
        }

        /// <summary>
        /// Initializes all elements with their properties.
        /// </summary>
        private void InitializeElements()
        {
            // Add Empty (must be element 0)
            ElementProperties emptyProps = new ElementProperties
            {
                State = -1,
                Density = 0f,
                ColorDefault = ColorToUint(new Color(0, 0, 0, 0))
            };
            _elementProperties.Add((int)ElementType.Empty, emptyProps);

            // ===================== SOLIDS =====================

            // Wall
            ElementProperties wallProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 2500f,
                Viscosity = 0f,
                Elasticity = 0.1f,
                Friction = 0.9f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.5f,
                SpecificHeat = 1000f,
                SpecialFlags = (int)ElementFlags.Indestructible,
                ColorDefault = ColorToUint(new Color(0.5f, 0.5f, 0.5f, 1f)),
                ColorHot = ColorToUint(new Color(0.7f, 0.5f, 0.5f, 1f)),
                ColorCold = ColorToUint(new Color(0.4f, 0.4f, 0.6f, 1f))
            };
            _elementProperties.Add((int)ElementType.Wall, wallProps);

            // Wood
            ElementProperties woodProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 700f,
                Viscosity = 0f,
                Elasticity = 0.2f,
                Friction = 0.7f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.15f,
                SpecificHeat = 1700f,
                Flammability = 0.7f,
                BurnTemperature = 300f,
                BurnRate = 0.4f,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.54f, 0.27f, 0.07f, 1f)),
                ColorHot = ColorToUint(new Color(0.65f, 0.32f, 0.1f, 1f)),
                ColorCold = ColorToUint(new Color(0.5f, 0.25f, 0.06f, 1f))
            };
            _elementProperties.Add((int)ElementType.Wood, woodProps);

            // Metal
            ElementProperties metalProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 7800f,
                Viscosity = 0f,
                Elasticity = 0.4f,
                Friction = 0.5f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.9f,
                SpecificHeat = 450f,
                MeltingPoint = 1500f,
                MeltResultElementId = (int)ElementType.Lava, // Molten metal is represented as lava
                SpecialFlags = (int)ElementFlags.Conductive | (int)ElementFlags.Magnetic,
                ColorDefault = ColorToUint(new Color(0.7f, 0.7f, 0.7f, 1f)),
                ColorHot = ColorToUint(new Color(1f, 0.6f, 0.4f, 1f)),
                ColorCold = ColorToUint(new Color(0.6f, 0.6f, 0.8f, 1f))
            };
            _elementProperties.Add((int)ElementType.Metal, metalProps);

            // Glass
            ElementProperties glassProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 2500f,
                Viscosity = 0f,
                Elasticity = 0.2f,
                Friction = 0.3f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 840f,
                MeltingPoint = 1400f,
                SpecialFlags = (int)ElementFlags.LightEmitter, // Glass allows light transmission
                ColorDefault = ColorToUint(new Color(0.8f, 0.9f, 0.95f, 0.7f)),
                ColorHot = ColorToUint(new Color(1f, 0.8f, 0.7f, 0.7f)),
                ColorCold = ColorToUint(new Color(0.75f, 0.85f, 1f, 0.7f))
            };
            _elementProperties.Add((int)ElementType.Glass, glassProps);

            // Stone
            ElementProperties stoneProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 2600f,
                Viscosity = 0f,
                Elasticity = 0.1f,
                Friction = 0.8f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.3f,
                SpecificHeat = 920f,
                MeltingPoint = 1200f,
                MeltResultElementId = (int)ElementType.Lava,
                ColorDefault = ColorToUint(new Color(0.5f, 0.5f, 0.5f, 1f)),
                ColorHot = ColorToUint(new Color(0.7f, 0.5f, 0.5f, 1f)),
                ColorCold = ColorToUint(new Color(0.4f, 0.45f, 0.5f, 1f))
            };
            _elementProperties.Add((int)ElementType.Stone, stoneProps);

            // Ice
            ElementProperties iceProps = new ElementProperties
            {
                State = 0, // Solid
                Density = 917f,
                Viscosity = 0f,
                Elasticity = 0.1f,
                Friction = 0.1f,
                DefaultTemperature = -5f,
                ThermalConductivity = 0.4f,
                SpecificHeat = 2100f,
                MeltingPoint = 0f,
                MeltResultElementId = (int)ElementType.Water,
                ColorDefault = ColorToUint(new Color(0.8f, 0.9f, 1f, 0.8f)),
                ColorHot = ColorToUint(new Color(0.9f, 0.95f, 1f, 0.6f)),
                ColorCold = ColorToUint(new Color(0.7f, 0.8f, 1f, 0.9f))
            };
            _elementProperties.Add((int)ElementType.Ice, iceProps);

            // C4
            ElementProperties c4Props = new ElementProperties
            {
                State = 0, // Solid
                Density = 1600f,
                Viscosity = 0f,
                Elasticity = 0.3f,
                Friction = 0.6f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 1000f,
                BurnTemperature = 200f,
                SpecialFlags = (int)ElementFlags.Explosive,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.8f, 0.75f, 0.6f, 1f)),
                ColorHot = ColorToUint(new Color(1f, 0.7f, 0.5f, 1f)),
                ColorCold = ColorToUint(new Color(0.75f, 0.7f, 0.6f, 1f))
            };
            _elementProperties.Add((int)ElementType.C4, c4Props);

            // ===================== POWDERS =====================

            // Sand
            ElementProperties sandProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 1600f,
                Viscosity = 0.5f,
                Elasticity = 0.1f,
                Friction = 0.8f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 830f,
                MeltingPoint = 1700f, // Silicon melting point
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.5f, // Can become mud when wet
                ColorDefault = ColorToUint(new Color(0.94f, 0.85f, 0.54f, 1f)),
                ColorHot = ColorToUint(new Color(1f, 0.8f, 0.5f, 1f)),
                ColorCold = ColorToUint(new Color(0.9f, 0.8f, 0.5f, 1f))
            };
            _elementProperties.Add((int)ElementType.Sand, sandProps);

            // Salt
            ElementProperties saltProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 2160f,
                Viscosity = 0.4f,
                Elasticity = 0.1f,
                Friction = 0.7f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.25f,
                SpecificHeat = 880f,
                ReactantElementId1 = (int)ElementType.Water, // Dissolves in water
                ReactionStrength1 = 0.9f,
                ColorDefault = ColorToUint(new Color(1f, 1f, 1f, 1f)),
                ColorHot = ColorToUint(new Color(1f, 0.95f, 0.9f, 1f)),
                ColorCold = ColorToUint(new Color(0.9f, 0.95f, 1f, 1f))
            };
            _elementProperties.Add((int)ElementType.Salt, saltProps);

            // Coal
            ElementProperties coalProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 1500f,
                Viscosity = 0.4f,
                Elasticity = 0.1f,
                Friction = 0.7f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 1000f,
                Flammability = 0.8f,
                BurnTemperature = 400f,
                BurnRate = 0.2f,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.1f, 0.1f, 0.1f, 1f)),
                ColorHot = ColorToUint(new Color(0.3f, 0.1f, 0.1f, 1f)),
                ColorCold = ColorToUint(new Color(0.1f, 0.1f, 0.15f, 1f))
            };
            _elementProperties.Add((int)ElementType.Coal, coalProps);

            // Gunpowder
            ElementProperties gunpowderProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 1700f,
                Viscosity = 0.4f,
                Elasticity = 0.1f,
                Friction = 0.7f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 1000f,
                Flammability = 1.0f,
                BurnTemperature = 300f,
                BurnRate = 0.9f,
                SpecialFlags = (int)ElementFlags.Explosive,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.2f, 0.2f, 0.2f, 1f)),
                ColorHot = ColorToUint(new Color(0.3f, 0.2f, 0.1f, 1f)),
                ColorCold = ColorToUint(new Color(0.2f, 0.2f, 0.25f, 1f))
            };
            _elementProperties.Add((int)ElementType.Gunpowder, gunpowderProps);

            // ===================== LIQUIDS =====================

            // Water
            ElementProperties waterProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 1000f,
                Viscosity = 0.9f,
                Elasticity = 0.0f,
                Friction = 0.2f,
                DefaultTemperature = 20f,
                ThermalConductivity = 0.6f,
                SpecificHeat = 4182f,
                FreezingPoint = 0f,
                BoilingPoint = 100f,
                EvaporateResultElementId = (int)ElementType.Steam,
                FreezeResultElementId = (int)ElementType.Ice,
                ReactantElementId1 = (int)ElementType.Fire,
                ReactionStrength1 = 0.8f, // Extinguishes fire
                ReactantElementId2 = (int)ElementType.Lava,
                ReactionStrength2 = 0.6f, // Creates steam and stone when touching lava
                SpecialFlags = (int)ElementFlags.Conductive, // Water conducts electricity
                ColorDefault = ColorToUint(new Color(0.2f, 0.5f, 0.8f, 0.8f)),
                ColorHot = ColorToUint(new Color(0.2f, 0.6f, 0.8f, 0.7f)),
                ColorCold = ColorToUint(new Color(0.2f, 0.4f, 0.8f, 0.9f))
            };
            _elementProperties.Add((int)ElementType.Water, waterProps);

            // Oil
            ElementProperties oilProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 900f, // Lighter than water
                Viscosity = 0.8f,
                Elasticity = 0.0f,
                Friction = 0.1f,
                DefaultTemperature = 20f,
                ThermalConductivity = 0.15f,
                SpecificHeat = 1800f,
                BoilingPoint = 250f,
                Flammability = 0.9f,
                BurnTemperature = 220f,
                BurnRate = 0.5f,
                EvaporateResultElementId = (int)ElementType.Smoke,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.4f, 0.35f, 0.2f, 0.85f)),
                ColorHot = ColorToUint(new Color(0.5f, 0.4f, 0.2f, 0.8f)),
                ColorCold = ColorToUint(new Color(0.35f, 0.3f, 0.2f, 0.9f))
            };
            _elementProperties.Add((int)ElementType.Oil, oilProps);

            // Acid
            ElementProperties acidProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 1100f,
                Viscosity = 0.7f,
                Elasticity = 0.0f,
                Friction = 0.2f,
                DefaultTemperature = 20f,
                ThermalConductivity = 0.5f,
                SpecificHeat = 3000f,
                BoilingPoint = 120f,
                EvaporateResultElementId = (int)ElementType.Smoke,
                SpecialFlags = (int)ElementFlags.Corrosive,
                ReactantElementId1 = (int)ElementType.Metal,
                ReactionStrength1 = 0.8f, // Dissolves metal
                ReactantElementId2 = (int)ElementType.Stone,
                ReactionStrength2 = 0.5f, // Slowly dissolves stone
                ColorDefault = ColorToUint(new Color(0.8f, 1f, 0.2f, 0.9f)),
                ColorHot = ColorToUint(new Color(0.9f, 1f, 0.3f, 0.85f)),
                ColorCold = ColorToUint(new Color(0.7f, 0.9f, 0.2f, 0.95f))
            };
            _elementProperties.Add((int)ElementType.Acid, acidProps);

            // Lava
            ElementProperties lavaProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 3000f,
                Viscosity = 0.95f,
                Elasticity = 0.0f,
                Friction = 0.4f,
                DefaultTemperature = 1500f,
                ThermalConductivity = 0.6f,
                SpecificHeat = 1000f,
                HeatProduction = 10f, // Lava generates heat
                FreezingPoint = 800f,
                FreezeResultElementId = (int)ElementType.Stone,
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.9f, // Creates steam and stone on contact with water
                SpecialFlags = (int)ElementFlags.LightEmitter, // Lava glows
                ColorDefault = ColorToUint(new Color(1f, 0.4f, 0.1f, 1f)),
                ColorHot = ColorToUint(new Color(1f, 0.6f, 0.1f, 1f)),
                ColorCold = ColorToUint(new Color(0.9f, 0.3f, 0.1f, 1f))
            };
            _elementProperties.Add((int)ElementType.Lava, lavaProps);

            // ===================== GASES =====================

            // Steam
            ElementProperties steamProps = new ElementProperties
            {
                State = 3, // Gas
                Density = 0.6f,
                Viscosity = 0.2f,
                Elasticity = 0.9f,
                Friction = 0.01f,
                DefaultTemperature = 110f,
                ThermalConductivity = 0.4f,
                SpecificHeat = 2000f,
                CondensationPoint = 100f,
                CondensingResultElementId = (int)ElementType.Water,
                ColorDefault = ColorToUint(new Color(0.9f, 0.9f, 0.9f, 0.5f)),
                ColorHot = ColorToUint(new Color(1f, 0.95f, 0.9f, 0.5f)),
                ColorCold = ColorToUint(new Color(0.8f, 0.8f, 0.9f, 0.7f))
            };
            _elementProperties.Add((int)ElementType.Steam, steamProps);

            // Smoke
            ElementProperties smokeProps = new ElementProperties
            {
                State = 3, // Gas
                Density = 0.3f,
                Viscosity = 0.1f,
                Elasticity = 0.8f,
                Friction = 0.01f,
                DefaultTemperature = 80f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 1000f,
                ColorDefault = ColorToUint(new Color(0.3f, 0.3f, 0.3f, 0.7f)),
                ColorHot = ColorToUint(new Color(0.4f, 0.3f, 0.3f, 0.6f)),
                ColorCold = ColorToUint(new Color(0.25f, 0.25f, 0.3f, 0.8f))
            };
            _elementProperties.Add((int)ElementType.Smoke, smokeProps);

            // Fire
            ElementProperties fireProps = new ElementProperties
            {
                State = 3, // Gas
                Density = 0.3f,
                Viscosity = 0.1f,
                Elasticity = 0.7f,
                Friction = 0.01f,
                DefaultTemperature = 400f,
                ThermalConductivity = 0.8f,
                SpecificHeat = 1000f,
                HeatProduction = 20f, // Fire generates heat
                SpecialFlags = (int)ElementFlags.LightEmitter, // Fire glows
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.9f, // Extinguished by water
                ColorDefault = ColorToUint(new Color(1f, 0.5f, 0.1f, 0.8f)),
                ColorHot = ColorToUint(new Color(1f, 0.7f, 0.2f, 0.9f)),
                ColorCold = ColorToUint(new Color(0.9f, 0.4f, 0.1f, 0.7f))
            };
            _elementProperties.Add((int)ElementType.Fire, fireProps);

            // Methane
            ElementProperties methaneProps = new ElementProperties
            {
                State = 3, // Gas
                Density = 0.7f,
                Viscosity = 0.1f,
                Elasticity = 0.9f,
                Friction = 0.01f,
                DefaultTemperature = 20f,
                ThermalConductivity = 0.3f,
                SpecificHeat = 2200f,
                Flammability = 1.0f,
                BurnTemperature = 50f,
                BurnRate = 0.8f,
                SpecialFlags = (int)ElementFlags.Explosive,
                BurnResultElementId = (int)ElementType.Fire,
                ColorDefault = ColorToUint(new Color(0.9f, 0.9f, 0.6f, 0.3f)),
                ColorHot = ColorToUint(new Color(1f, 0.9f, 0.5f, 0.4f)),
                ColorCold = ColorToUint(new Color(0.8f, 0.9f, 0.7f, 0.2f))
            };
            _elementProperties.Add((int)ElementType.Methane, methaneProps);

            // ===================== SPECIAL =====================

            // Electricity
            ElementProperties electricityProps = new ElementProperties
            {
                State = 4, // Special
                Density = 0.1f,
                Viscosity = 0.0f,
                Elasticity = 1.0f,
                Friction = 0.0f,
                DefaultTemperature = 500f,
                ThermalConductivity = 1.0f,
                SpecificHeat = 100f,
                HeatProduction = 5f,
                SpecialFlags = (int)ElementFlags.Conductive | (int)ElementFlags.LightEmitter,
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.8f, // Conducts through water
                ColorDefault = ColorToUint(new Color(1f, 1f, 0.2f, 0.9f)),
                ColorHot = ColorToUint(new Color(1f, 1f, 0.5f, 0.95f)),
                ColorCold = ColorToUint(new Color(0.8f, 0.8f, 0.2f, 0.8f))
            };
            _elementProperties.Add((int)ElementType.Electricity, electricityProps);

            // Plasma
            ElementProperties plasmaProps = new ElementProperties
            {
                State = 4, // Special
                Density = 0.1f,
                Viscosity = 0.0f,
                Elasticity = 1.0f,
                Friction = 0.0f,
                DefaultTemperature = 5000f,
                ThermalConductivity = 1.0f,
                SpecificHeat = 20000f,
                HeatProduction = 50f,
                SpecialFlags = (int)ElementFlags.LightEmitter | (int)ElementFlags.Radioactive,
                ColorDefault = ColorToUint(new Color(0.7f, 0.3f, 1f, 0.8f)),
                ColorHot = ColorToUint(new Color(0.9f, 0.4f, 1f, 0.9f)),
                ColorCold = ColorToUint(new Color(0.6f, 0.2f, 0.8f, 0.7f))
            };
            _elementProperties.Add((int)ElementType.Plasma, plasmaProps);

            // Neutron
            ElementProperties neutronProps = new ElementProperties
            {
                State = 4, // Special
                Density = 0.01f,
                Viscosity = 0.0f,
                Elasticity = 1.0f,
                Friction = 0.0f,
                DefaultTemperature = 0f,
                ThermalConductivity = 0.0f, // Doesn't transfer heat
                SpecificHeat = 0f,
                SpecialFlags = (int)ElementFlags.Radioactive,
                ColorDefault = ColorToUint(new Color(0.0f, 1f, 0.5f, 0.5f)),
                ColorHot = ColorToUint(new Color(0.0f, 1f, 0.5f, 0.5f)),
                ColorCold = ColorToUint(new Color(0.0f, 1f, 0.5f, 0.5f))
            };
            _elementProperties.Add((int)ElementType.Neutron, neutronProps);

            // ===================== NEW ELEMENTS =====================

            // Snow
            ElementProperties snowProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 100f,
                Viscosity = 0.3f,
                Elasticity = 0.1f,
                Friction = 0.5f,
                DefaultTemperature = -5f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 2100f,
                MeltingPoint = 0f,
                MeltResultElementId = (int)ElementType.Water,
                ColorDefault = ColorToUint(new Color(1f, 1f, 1f, 0.95f)),
                ColorHot = ColorToUint(new Color(0.9f, 0.95f, 1f, 0.8f)),
                ColorCold = ColorToUint(new Color(0.9f, 0.95f, 1f, 1f))
            };
            _elementProperties.Add((int)ElementType.Snow, snowProps);

            // Concrete
            ElementProperties concreteProps = new ElementProperties
            {
                State = 1, // Powder initially, then hardens
                Density = 2400f,
                Viscosity = 0.7f,
                Elasticity = 0.1f,
                Friction = 0.9f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.4f,
                SpecificHeat = 880f,
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.5f, // Concrete hardens when wet
                ReactantElementId2 = (int)ElementType.Fire,
                ReactionStrength2 = 0.2f, // Fire can make concrete crack
                ColorDefault = ColorToUint(new Color(0.65f, 0.65f, 0.65f, 1f)),
                ColorHot = ColorToUint(new Color(0.7f, 0.65f, 0.6f, 1f)),
                ColorCold = ColorToUint(new Color(0.6f, 0.6f, 0.65f, 1f))
            };
            _elementProperties.Add((int)ElementType.Concrete, concreteProps);

            // Gel
            ElementProperties gelProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 1100f,
                Viscosity = 0.95f,
                Elasticity = 0.6f,
                Friction = 0.3f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.35f,
                SpecificHeat = 2500f,
                SpecialFlags = (int)ElementFlags.Sticky | (int)ElementFlags.Bouncy,
                ColorDefault = ColorToUint(new Color(0.3f, 0.8f, 0.3f, 0.8f)),
                ColorHot = ColorToUint(new Color(0.4f, 0.8f, 0.3f, 0.7f)),
                ColorCold = ColorToUint(new Color(0.2f, 0.7f, 0.3f, 0.9f))
            };
            _elementProperties.Add((int)ElementType.Gel, gelProps);

            // Slime
            ElementProperties slimeProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 1200f,
                Viscosity = 0.98f,
                Elasticity = 0.4f,
                Friction = 0.5f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.3f,
                SpecificHeat = 2200f,
                SpecialFlags = (int)ElementFlags.Sticky | (int)ElementFlags.SelfReplicating,
                ReactantElementId1 = (int)ElementType.Biomass,
                ReactionStrength1 = 0.7f, // Grows on biomass
                ColorDefault = ColorToUint(new Color(0.4f, 0.9f, 0.4f, 0.85f)),
                ColorHot = ColorToUint(new Color(0.5f, 0.9f, 0.4f, 0.8f)),
                ColorCold = ColorToUint(new Color(0.3f, 0.8f, 0.4f, 0.9f))
            };
            _elementProperties.Add((int)ElementType.Slime, slimeProps);

            // Mercury
            ElementProperties mercuryProps = new ElementProperties
            {
                State = 2, // Liquid
                Density = 13600f, // Very dense
                Viscosity = 0.5f,
                Elasticity = 0.0f,
                Friction = 0.0f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.8f,
                SpecificHeat = 140f,
                BoilingPoint = 357f,
                FreezingPoint = -39f,
                SpecialFlags = (int)ElementFlags.Conductive,
                ColorDefault = ColorToUint(new Color(0.8f, 0.8f, 0.85f, 1f)),
                ColorHot = ColorToUint(new Color(0.85f, 0.85f, 0.9f, 1f)),
                ColorCold = ColorToUint(new Color(0.75f, 0.75f, 0.8f, 1f))
            };
            _elementProperties.Add((int)ElementType.Mercury, mercuryProps);

            // Biomass
            ElementProperties biomassProps = new ElementProperties
            {
                State = 1, // Powder
                Density = 800f,
                Viscosity = 0.6f,
                Elasticity = 0.2f,
                Friction = 0.6f,
                DefaultTemperature = 25f,
                ThermalConductivity = 0.2f,
                SpecificHeat = 1800f,
                Flammability = 0.6f,
                BurnTemperature = 150f,
                BurnRate = 0.3f,
                BurnResultElementId = (int)ElementType.Fire,
                SpecialFlags = (int)ElementFlags.GrowsPlants,
                ReactantElementId1 = (int)ElementType.Water,
                ReactionStrength1 = 0.6f, // Grows when wet
                ColorDefault = ColorToUint(new Color(0.2f, 0.6f, 0.2f, 1f)),
                ColorHot = ColorToUint(new Color(0.3f, 0.6f, 0.2f, 1f)),
                ColorCold = ColorToUint(new Color(0.2f, 0.5f, 0.2f, 1f))
            };
            _elementProperties.Add((int)ElementType.Biomass, biomassProps);
        }
    }
}