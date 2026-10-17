using System;
using HoodGlobalOffensive.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Presentation-only reticle profile (reference <c>UI.CrosshairSettings</c>): it
    /// deliberately holds no combat, camera, player or simulation state, so changing it
    /// never changes aim, recoil, spread or hit-scan. All sizes are canvas pixels —
    /// the 1920×1080 scaler makes them reference pixels at 1080p.
    /// </summary>
    public sealed class CrosshairSettings
    {
        public CrosshairPreset? Preset { get; private set; } = CrosshairPreset.Classic;
        public CrosshairColorOption Color = CrosshairColorOption.Green;
        public CrosshairStyle Style = CrosshairStyle.Cross;
        public bool DynamicGap = true;
        public bool CentreDot = true;
        public int BaseGap = 4;
        public int ArmLength = 7;
        public int Thickness = 1;

        public string PresetLabel => Preset switch
        {
            CrosshairPreset.Classic => "CLASSIC",
            CrosshairPreset.Compact => "COMPACT",
            CrosshairPreset.TShape => "T-SHAPE",
            CrosshairPreset.Dot => "DOT",
            _ => "CUSTOM",
        };

        public string ColorLabel => Color switch
        {
            CrosshairColorOption.Green => "GREEN",
            CrosshairColorOption.Cyan => "CYAN",
            CrosshairColorOption.White => "WHITE",
            CrosshairColorOption.Yellow => "YELLOW",
            CrosshairColorOption.Red => "RED",
            _ => "GREEN",
        };

        public void CyclePreset(int direction)
        {
            int current = (int)(Preset ?? CrosshairPreset.Classic);
            int count = Enum.GetValues<CrosshairPreset>().Length;
            int next = ((current + direction) % count + count) % count;
            ApplyPreset((CrosshairPreset)next);
        }

        public void CycleColor(int direction)
        {
            int current = (int)Color;
            int count = Enum.GetValues<CrosshairColorOption>().Length;
            int next = ((current + direction) % count + count) % count;
            Color = (CrosshairColorOption)next;
            Preset = null;
        }

        public void ToggleDynamicGap()
        {
            DynamicGap = !DynamicGap;
            Preset = null;
        }

        public void ToggleCentreDot()
        {
            CentreDot = !CentreDot;
            Preset = null;
        }

        public void AdjustGap(int direction)
        {
            BaseGap = U.ClampInt(BaseGap + direction, 0, 24);
            Preset = null;
        }

        public void AdjustLength(int direction)
        {
            ArmLength = U.ClampInt(ArmLength + direction, 1, 24);
            Preset = null;
        }

        public void AdjustThickness(int direction)
        {
            Thickness = U.ClampInt(Thickness + direction, 1, 5);
            Preset = null;
        }

        public void Reset() => ApplyPreset(CrosshairPreset.Classic);

        /// <summary>Captures only local reticle presentation settings for persistence.</summary>
        public CrosshairPreference CapturePreference() => new CrosshairPreference(
            Preset, Color, Style, DynamicGap, CentreDot, BaseGap, ArmLength, Thickness);

        /// <summary>
        /// Restores a bounded presentation profile (the reference rules): invalid enum
        /// values recover to safe defaults, numeric fields clamp, and a named preset is
        /// retained only when every one of its actual values still matches, so a corrupt
        /// store cannot make the UI lie about "CUSTOM" versus a preset.
        /// </summary>
        public void Restore(CrosshairPreference preference)
        {
            Color = Enum.IsDefined(typeof(CrosshairColorOption), preference.Color)
                ? preference.Color
                : CrosshairColorOption.Green;
            Style = Enum.IsDefined(typeof(CrosshairStyle), preference.Style)
                ? preference.Style
                : CrosshairStyle.Cross;
            DynamicGap = preference.DynamicGap;
            CentreDot = preference.CentreDot;
            BaseGap = U.ClampInt(preference.BaseGap, 0, 24);
            ArmLength = U.ClampInt(preference.ArmLength, 1, 24);
            Thickness = U.ClampInt(preference.Thickness, 1, 5);

            if (preference.Preset is CrosshairPreset preset
                && Enum.IsDefined(typeof(CrosshairPreset), preset)
                && MatchesPreset(preset))
            {
                Preset = preset;
            }
            else
            {
                Preset = null;
            }
        }

        void ApplyPreset(CrosshairPreset preset) => Restore(ProfileFor(preset));

        bool MatchesPreset(CrosshairPreset preset)
        {
            CrosshairPreference expected = ProfileFor(preset);
            return Color == expected.Color
                && Style == expected.Style
                && DynamicGap == expected.DynamicGap
                && CentreDot == expected.CentreDot
                && BaseGap == expected.BaseGap
                && ArmLength == expected.ArmLength
                && Thickness == expected.Thickness;
        }

        static CrosshairPreference ProfileFor(CrosshairPreset preset) => preset switch
        {
            CrosshairPreset.Classic => new CrosshairPreference(preset, CrosshairColorOption.Green, CrosshairStyle.Cross, true, true, 4, 7, 1),
            CrosshairPreset.Compact => new CrosshairPreference(preset, CrosshairColorOption.Cyan, CrosshairStyle.Cross, true, false, 2, 5, 1),
            CrosshairPreset.TShape => new CrosshairPreference(preset, CrosshairColorOption.Yellow, CrosshairStyle.TShape, true, false, 4, 8, 2),
            CrosshairPreset.Dot => new CrosshairPreference(preset, CrosshairColorOption.White, CrosshairStyle.Dot, false, true, 0, 1, 3),
            _ => new CrosshairPreference(preset, CrosshairColorOption.Green, CrosshairStyle.Cross, true, true, 4, 7, 1),
        };

        public Color ResolveColor(byte alpha = 225) => Color switch
        {
            CrosshairColorOption.Green => Rgba(120, 255, 140, alpha),
            CrosshairColorOption.Cyan => Rgba(72, 232, 255, alpha),
            CrosshairColorOption.White => Rgba(245, 245, 240, alpha),
            CrosshairColorOption.Yellow => Rgba(255, 210, 72, alpha),
            CrosshairColorOption.Red => Rgba(255, 92, 82, alpha),
            _ => Rgba(120, 255, 140, alpha),
        };

        static Color Rgba(byte r, byte g, byte b, byte alpha) =>
            new Color(r / 255f, g / 255f, b / 255f, alpha / 255f);
    }

    /// <summary>Closed, render-only crosshair data used by local preference persistence.</summary>
    public readonly struct CrosshairPreference
    {
        public readonly CrosshairPreset? Preset;
        public readonly CrosshairColorOption Color;
        public readonly CrosshairStyle Style;
        public readonly bool DynamicGap;
        public readonly bool CentreDot;
        public readonly int BaseGap;
        public readonly int ArmLength;
        public readonly int Thickness;

        public CrosshairPreference(CrosshairPreset? preset, CrosshairColorOption color, CrosshairStyle style,
            bool dynamicGap, bool centreDot, int baseGap, int armLength, int thickness)
        {
            Preset = preset;
            Color = color;
            Style = style;
            DynamicGap = dynamicGap;
            CentreDot = centreDot;
            BaseGap = baseGap;
            ArmLength = armLength;
            Thickness = thickness;
        }
    }

    public enum CrosshairPreset
    {
        Classic,
        Compact,
        TShape,
        Dot,
    }

    public enum CrosshairStyle
    {
        Cross,
        TShape,
        Dot,
    }

    public enum CrosshairColorOption
    {
        Green,
        Cyan,
        White,
        Yellow,
        Red,
    }

    /// <summary>
    /// uGUI blockout of the reference <c>CrosshairRenderer</c>: four axis-aligned arm
    /// images (the top arm is skipped for T-shape style) and an optional centre dot,
    /// all anchored to the container centre. <see cref="Update"/> re-lays them from the
    /// profile and the live visual spread. Canvas coordinates are y-up — the opposite of
    /// the reference's y-down screen — so the vertical arms are placed by visual
    /// meaning: the T-shape skip removes the arm above the centre, exactly as the
    /// reference does. The reference rasteriser's exact branches (1 px DrawLine at
    /// scale 1, DrawCircle dot otherwise) are approximated by a thickness×thickness
    /// dot, which matches at the default 1 px and stays within a pixel elsewhere.
    /// </summary>
    public sealed class CrosshairGraphic
    {
        readonly Image[] _arms;
        readonly Image _dot;

        public Transform Root { get; }

        CrosshairGraphic(Transform root, Image[] arms, Image dot)
        {
            Root = root;
            _arms = arms;
            _dot = dot;
        }

        public static CrosshairGraphic Create(Transform parent, string name)
        {
            GameObject rootGo = new GameObject(name);
            RectTransform rect = rootGo.AddComponent<RectTransform>();
            rect.anchorMin = Centre;
            rect.anchorMax = Centre;
            rect.pivot = Centre;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rootGo.transform.SetParent(parent);

            Image[] arms = new Image[4];
            for (int i = 0; i < arms.Length; i++)
            {
                arms[i] = MakeImage(rootGo.transform, $"Arm {i}");
            }
            Image dot = MakeImage(rootGo.transform, "Dot");

            return new CrosshairGraphic(rootGo.transform, arms, dot);
        }

        /// <param name="spreadPixels">Visual-only spread expansion in canvas pixels.</param>
        /// <param name="scale">Presentation scale (the menu preview uses 1.75).</param>
        /// <param name="maxGap">Clamp for the dynamic gap, like the reference HUD's 0.32×height.</param>
        public void Update(CrosshairSettings settings, float spreadPixels, float scale = 1f,
            float maxGap = float.MaxValue)
        {
            float gap = (settings.BaseGap + (settings.DynamicGap ? MathF.Max(0f, spreadPixels) : 0f)) * scale;
            gap = MathF.Min(gap, maxGap);
            float length = settings.ArmLength * scale;
            float thickness = settings.Thickness * scale;
            Color color = settings.ResolveColor();

            bool armsVisible = settings.Style != CrosshairStyle.Dot;
            LayArm(_arms[0], armsVisible, new Vector2(-(gap + length * 0.5f), 0f), new Vector2(length, thickness), color);
            LayArm(_arms[1], armsVisible, new Vector2(gap + length * 0.5f, 0f), new Vector2(length, thickness), color);
            LayArm(_arms[2], armsVisible && settings.Style != CrosshairStyle.TShape,
                new Vector2(0f, gap + length * 0.5f), new Vector2(thickness, length), color);
            LayArm(_arms[3], armsVisible, new Vector2(0f, -(gap + length * 0.5f)), new Vector2(thickness, length), color);

            bool dotVisible = settings.CentreDot;
            _dot.gameObject.SetActive(dotVisible);
            if (dotVisible)
            {
                _dot.rectTransform.anchoredPosition = Vector2.zero;
                _dot.rectTransform.sizeDelta = new Vector2(thickness, thickness);
                _dot.color = color;
            }
        }

        static void LayArm(Image arm, bool visible, Vector2 position, Vector2 size, Color color)
        {
            arm.gameObject.SetActive(visible);
            if (!visible) return;
            arm.rectTransform.anchoredPosition = position;
            arm.rectTransform.sizeDelta = size;
            arm.color = color;
        }

        static Image MakeImage(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Centre;
            rect.anchorMax = Centre;
            rect.pivot = Centre;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            go.transform.SetParent(parent);
            return go.AddComponent<Image>();
        }

        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
    }
}
