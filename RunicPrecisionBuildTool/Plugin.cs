using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Valheim.SettingsGui;

namespace QuietBuildRotation
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicPrecisionBuildTool";
        public const string Name = "Runic Precision Build Tool";
        public const string Version = "1.0.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> PitchModifier;
        internal static ConfigEntry<KeyboardShortcut> RollModifier;
        internal static ConfigEntry<KeyboardShortcut> FineModifier;
        internal static ConfigEntry<KeyboardShortcut> ResetShortcut;
        internal static ConfigEntry<float> NormalStep;
        internal static ConfigEntry<float> FineStep;
        internal static ConfigEntry<float> MoveStep;
        internal static ConfigEntry<float> FineMoveStep;
        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Enable advanced rotation. Vanilla rotation is unchanged when disabled.");
            PitchModifier = Config.Bind("Controls", "Pitch", new KeyboardShortcut(KeyCode.LeftAlt), "Hold and scroll to pitch.");
            RollModifier = Config.Bind("Controls", "Roll", new KeyboardShortcut(KeyCode.LeftAlt, KeyCode.LeftShift), "Hold and scroll to roll.");
            FineModifier = Config.Bind("Controls", "Fine", new KeyboardShortcut(KeyCode.V), "Hold as well for fine rotation or movement. Ctrl is intentionally unused.");
            ResetShortcut = Config.Bind("Controls", "Reset", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt), "Reserved reset binding. Alt+R resets rotation, vanilla yaw, and translation.");
            NormalStep = Config.Bind("Rotation", "StepDegrees", 15f, new ConfigDescription("Normal pitch/roll step.", new AcceptableValueRange<float>(0.1f, 90f)));
            FineStep = Config.Bind("Rotation", "FineStepDegrees", 1f, new ConfigDescription("Fine pitch/roll step.", new AcceptableValueRange<float>(0.01f, 45f)));
            MoveStep = Config.Bind("Movement", "StepMeters", 0.25f, new ConfigDescription("Normal local-axis movement step.", new AcceptableValueRange<float>(0.01f, 5f)));
            FineMoveStep = Config.Bind("Movement", "FineStepMeters", 0.05f, new ConfigDescription("Fine local-axis movement step.", new AcceptableValueRange<float>(0.001f, 1f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo($"{Name} v{Version} loaded. Alt+wheel pitches, Alt+Shift+wheel rolls, Alt+arrows/PageUp/PageDown moves, Alt+R resets.");
        }

        private void Update()
        {
            RotationController.PollKeyboard();
            AxisGuide.Update();
        }

        private void OnDestroy()
        {
            AxisGuide.Destroy();
            NativeBuildHints.Destroy();
            _harmony?.UnpatchSelf();
        }
    }

    internal struct PlacementState
    {
        internal Quaternion Rotation;
        internal Vector3 WorldOffset;

        internal static PlacementState Identity => new PlacementState
        {
            Rotation = Quaternion.identity,
            WorldOffset = Vector3.zero
        };
    }

    internal static class RotationController
    {
        private static string _activePiece;
        private static PlacementState _state = PlacementState.Identity;
        private static int _rotationBeforeInput;
        private static bool _advancedInput;
        private static bool _guideVisible;
        private static bool _resetRequested;
        private static readonly AccessTools.FieldRef<Player, int> PlaceRotation = AccessTools.FieldRefAccess<Player, int>("m_placeRotation");
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, float> ScrollAmount = AccessTools.FieldRefAccess<Player, float>("m_scrollCurrAmount");
        private static readonly AccessTools.FieldRef<Player, int> ManualSnapPoint = AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");
        private static readonly MethodInfo SetupPlacementGhost = AccessTools.Method(typeof(Player), "SetupPlacementGhost");
        private static readonly MethodInfo UpdatePlacementGhost = AccessTools.Method(typeof(Player), "UpdatePlacementGhost");

        internal static void BeforePlacementInput(Player player)
        {
            ExecutePendingReset(player);
            _rotationBeforeInput = PlaceRotation(player);
            _advancedInput = CanOperate(player) && (Plugin.PitchModifier.Value.IsPressed() || Plugin.RollModifier.Value.IsPressed());
        }

        internal static void AfterPlacementInput(Player player)
        {
            if (!CanOperate(player)) return;
            GameObject ghost = PlacementGhost(player);
            if (!ghost || !ghost.activeInHierarchy) return;
            Piece piece = ghost.GetComponent<Piece>();
            if (!piece || !piece.m_canRotate) return;

            string key = ghost.name;
            PlacementState state = GetState(key);

            if (_advancedInput)
            {
                // Vanilla sees the wheel too. Put yaw back so the modifier never steals or
                // synthesizes input globally; it only cancels vanilla yaw for this player.
                PlaceRotation(player) = _rotationBeforeInput;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    float direction = Mathf.Sign(wheel);
                    float step = Plugin.FineModifier.Value.IsPressed() ? Plugin.FineStep.Value : Plugin.NormalStep.Value;
                    // Post-multiplication makes every increment intrinsic: the axis belongs to
                    // the object in its current orientation, not to the original prefab/world.
                    Vector3 localAxis = Plugin.RollModifier.Value.IsPressed() ? Vector3.forward : Vector3.right;
                    state.Rotation = state.Rotation * Quaternion.AngleAxis(direction * step, localAxis);
                    state.Rotation.Normalize();
                    SaveState(key, state);
                }
            }

        }

        internal static void PollKeyboard()
        {
            Player player = Player.m_localPlayer;
            if (!CanOperate(player)) return;
            GameObject ghost = PlacementGhost(player);
            if (!ghost || !ghost.activeInHierarchy) return;
            GetState(ghost.name); // Also resets manipulation when selection changes.

            if (Input.GetKeyDown(KeyCode.G))
            {
                _guideVisible = !_guideVisible;
                return;
            }

            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool resetChord = (alt && Input.GetKeyDown(KeyCode.R)) ||
                              (Input.GetKey(KeyCode.R) && (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)));
            if (resetChord)
            {
                _resetRequested = true;
                return;
            }
            if (!alt) return;

            float step = Plugin.FineModifier.Value.IsPressed() ? Plugin.FineMoveStep.Value : Plugin.MoveStep.Value;
            Vector3 movement = ReadWorldMovementStep();
            if (movement != Vector3.zero) _state.WorldOffset += movement * step;
        }

        internal static bool GuideVisible => _guideVisible;

        internal static GameObject CurrentGhost
        {
            get
            {
                Player player = Player.m_localPlayer;
                return player ? PlacementGhost(player) : null;
            }
        }

        private static void ExecutePendingReset(Player player)
        {
            if (!_resetRequested || player != Player.m_localPlayer) return;
            _resetRequested = false;
            _state = PlacementState.Identity;
            _activePiece = null;
            PlaceRotation(player) = 0;
            ScrollAmount(player) = 0f;
            ManualSnapPoint(player) = -1;
            SetupPlacementGhost.Invoke(player, null);
            UpdatePlacementGhost.Invoke(player, new object[] { false });
        }

        internal static Quaternion ComposePlacementRotation(float x, float y, float z)
        {
            Quaternion vanilla = Quaternion.Euler(x, y, z);
            Player player = Player.m_localPlayer;
            if (!CanOperate(player)) return vanilla;
            GameObject ghost = PlacementGhost(player);
            if (!ghost) return vanilla;

            // This quaternion is returned where vanilla creates its placement rotation. Every
            // subsequent snap-point transform, overlap check, preview, and placement sees it.
            return vanilla * GetState(ghost.name).Rotation;
        }

        internal static void ApplyTranslation(Player player)
        {
            if (!CanOperate(player)) return;
            GameObject ghost = PlacementGhost(player);
            if (!ghost || !ghost.activeInHierarchy) return;
            PlacementState state = GetState(ghost.name);
            if (state.WorldOffset.sqrMagnitude < 0.0000001f) return;

            // Translation is deliberately world-space. Rotation never changes what the movement
            // keys mean: X remains east/west, Y remains vertical, and Z remains north/south.
            ghost.transform.position += state.WorldOffset;
        }

        internal static bool HasPrecisionOffset(Player player)
        {
            if (!CanOperate(player)) return false;
            GameObject ghost = PlacementGhost(player);
            return ghost && GetState(ghost.name).WorldOffset.sqrMagnitude > 0.0000001f;
        }

        private static bool CanOperate(Player player)
        {
            return Plugin.Enabled.Value && player == Player.m_localPlayer &&
                   !Console.IsVisible() && (Chat.instance == null || !Chat.instance.HasFocus()) &&
                   Hud.instance != null && !Hud.IsPieceSelectionVisible();
        }

        private static PlacementState GetState(string key)
        {
            if (_activePiece != key)
            {
                _activePiece = key;
                _state = PlacementState.Identity;
            }
            return _state;
        }

        private static void SaveState(string key, PlacementState state)
        {
            GetState(key);
            _state = state;
        }

        private static Vector3 ReadWorldMovementStep()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow)) return Vector3.left;
            if (Input.GetKeyDown(KeyCode.RightArrow)) return Vector3.right;
            if (Input.GetKeyDown(KeyCode.UpArrow)) return Vector3.up;
            if (Input.GetKeyDown(KeyCode.DownArrow)) return Vector3.down;
            if (Input.GetKeyDown(KeyCode.PageUp)) return Vector3.forward;
            if (Input.GetKeyDown(KeyCode.PageDown)) return Vector3.back;
            return Vector3.zero;
        }
    }

    internal static class NativeBuildHints
    {
        private static readonly List<GameObject> Entries = new List<GameObject>();

        internal static void Create(KeyHints hints)
        {
            Destroy();
            if (!hints || !hints.m_buildHints) return;
            UIInputHint inputHint = hints.m_buildHints.GetComponent<UIInputHint>();
            Transform keyboard = inputHint?.m_mouseKeyboardHint?.transform;
            GameObject template = keyboard?.Find("Place")?.gameObject;
            if (!template)
            {
                Plugin.Log.LogWarning("Could not find Valheim's native Place key hint; precision hints were not added.");
                return;
            }

            Add(template, keyboard, "Alt+Wheel", "Pitch");
            Add(template, keyboard, "Alt+Shift+Wheel", "Roll");
            Add(template, keyboard, "Alt+Arrows", "Move X/Y");
            Add(template, keyboard, "Alt+PgUp/PgDn", "Move Z");
            Add(template, keyboard, "V", "Fine");
            Add(template, keyboard, "Alt+R", "Reset");
            Add(template, keyboard, "G", "Guides");
        }

        internal static void Refresh()
        {
            bool visible = RotationController.GuideVisible;
            foreach (GameObject entry in Entries)
            {
                if (entry && entry.activeSelf != visible) entry.SetActive(visible);
            }
        }

        internal static void Destroy()
        {
            foreach (GameObject entry in Entries)
            {
                if (entry) Object.Destroy(entry);
            }
            Entries.Clear();
        }

        private static void Add(GameObject template, Transform parent, string key, string label)
        {
            GameObject entry = Object.Instantiate(template, parent, false);
            entry.name = "RunicPrecision_" + label.Replace(" ", string.Empty);
            TextMeshProUGUI keyText = entry.transform.Find("key_bkg/Key")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI labelText = entry.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (keyText) keyText.text = key;
            if (labelText) labelText.text = label;
            entry.SetActive(false);
            Entries.Add(entry);
        }
    }

    internal static class AxisGuide
    {
        private const int Segments = 72;
        private static readonly Color[] LocalColors =
        {
            new Color(1f, 0.15f, 0.12f, 0.95f),
            new Color(0.2f, 1f, 0.25f, 0.95f),
            new Color(0.2f, 0.55f, 1f, 0.95f)
        };
        private static readonly Color[] WorldColors =
        {
            new Color(1f, 0.25f, 0.22f, 0.28f),
            new Color(0.3f, 1f, 0.35f, 0.28f),
            new Color(0.3f, 0.65f, 1f, 0.28f)
        };
        private static GameObject _root;
        private static LineRenderer[] _rings;
        private static Material _material;

        internal static void Update()
        {
            GameObject ghost = RotationController.CurrentGhost;
            bool visible = RotationController.GuideVisible && ghost && ghost.activeInHierarchy;
            if (!visible)
            {
                if (_root) _root.SetActive(false);
                return;
            }

            EnsureCreated();
            _root.SetActive(true);
            Bounds bounds = CalculateBounds(ghost);
            Vector3 center = bounds.center;
            float radius = Mathf.Clamp(bounds.extents.magnitude * 0.72f, 0.45f, 2.75f);
            float width = Mathf.Clamp(Vector3.Distance(center, Camera.main ? Camera.main.transform.position : center) * 0.0025f, 0.018f, 0.055f);
            Quaternion local = ghost.transform.rotation;

            for (int axis = 0; axis < 3; axis++)
            {
                DrawRing(_rings[axis], center, radius, width, axis, local);
                DrawRing(_rings[axis + 3], center, radius * 1.08f, width * 0.65f, axis, Quaternion.identity);
            }
        }

        internal static void Destroy()
        {
            if (_root) Object.Destroy(_root);
            if (_material) Object.Destroy(_material);
            _root = null;
            _rings = null;
            _material = null;
        }

        private static void EnsureCreated()
        {
            if (_root) return;
            _root = new GameObject("QuietBuildRotation_AxisGuide") { hideFlags = HideFlags.HideAndDontSave };
            _material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            _rings = new LineRenderer[6];
            for (int i = 0; i < _rings.Length; i++)
            {
                GameObject ring = new GameObject(i < 3 ? $"LocalAxis_{i}" : $"WorldAxis_{i - 3}");
                ring.hideFlags = HideFlags.HideAndDontSave;
                ring.transform.SetParent(_root.transform, false);
                LineRenderer line = ring.AddComponent<LineRenderer>();
                line.sharedMaterial = _material;
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = Segments;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.startColor = line.endColor = i < 3 ? LocalColors[i] : WorldColors[i - 3];
                _rings[i] = line;
            }
        }

        private static Bounds CalculateBounds(GameObject ghost)
        {
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(ghost.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void DrawRing(LineRenderer line, Vector3 center, float radius, float width, int axis, Quaternion orientation)
        {
            line.startWidth = line.endWidth = width;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                float a = Mathf.Cos(angle) * radius;
                float b = Mathf.Sin(angle) * radius;
                Vector3 point = axis == 0 ? new Vector3(0f, a, b) :
                                axis == 1 ? new Vector3(a, 0f, b) : new Vector3(a, b, 0f);
                line.SetPosition(i, center + orientation * point);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class PlayerUpdatePlacementPatch
    {
        private static void Prefix(Player __instance) => RotationController.BeforePlacementInput(__instance);
        private static void Postfix(Player __instance) => RotationController.AfterPlacementInput(__instance);
    }

    [HarmonyPatch(typeof(KeyHints), "Awake")]
    internal static class KeyHintsAwakePatch
    {
        private static void Postfix(KeyHints __instance) => NativeBuildHints.Create(__instance);
    }

    [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
    internal static class KeyHintsUpdatePatch
    {
        private static void Postfix() => NativeBuildHints.Refresh();
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class PlayerUpdatePlacementGhostPatch
    {
        private static readonly MethodInfo QuaternionEuler = AccessTools.Method(
            typeof(Quaternion), nameof(Quaternion.Euler), new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo ComposeRotation = AccessTools.Method(
            typeof(RotationController), nameof(RotationController.ComposePlacementRotation));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!replaced && instruction.opcode == OpCodes.Call && Equals(instruction.operand, QuaternionEuler))
                {
                    instruction.operand = ComposeRotation;
                    replaced = true;
                }
                yield return instruction;
            }

            if (!replaced)
                Plugin.Log.LogError("Could not locate Valheim's placement quaternion. Advanced rotation was not patched.");
        }

        private static void Postfix(Player __instance) => RotationController.ApplyTranslation(__instance);
    }

    [HarmonyPatch(typeof(Player), "TestGhostClipping")]
    internal static class PlayerTestGhostClippingPatch
    {
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (!RotationController.HasPrecisionOffset(__instance)) return true;
            __result = false;
            return false;
        }
    }
}

