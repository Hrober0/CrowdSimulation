using UnityEngine;

namespace HCore.UI
{
    public static class UIColors
    {
       // ── Font sizes ───────────────────────────────────────────────────────
       // Sized in the UIPanelScale reference units the rest of the UI uses. These are the numbers to change
       // to resize every label in the project at once - the panels themselves set no font size.
        public const float FontSizeXS = 18f;
        public const float FontSizeS  = 22f;
        public const float FontSizeM  = 24f;
        public const float FontSizeL  = 28f;

        // ── Surfaces ─────────────────────────────────────────────────────────
        public static readonly Color Background   = new(0.08f, 0.09f, 0.11f);
        public static readonly Color Surface      = new(0.12f, 0.14f, 0.17f);
        public static readonly Color SurfaceRaised= new(0.17f, 0.19f, 0.23f);
        public static readonly Color SurfaceHover = new(0.22f, 0.24f, 0.29f);

        // ── Borders ───────────────────────────────────────────────────────────
        public static readonly Color Border       = new(0.22f, 0.25f, 0.30f);
        public static readonly Color BorderFaint  = new(0.16f, 0.18f, 0.21f);
        public static readonly Color BorderHover  = new(0.35f, 0.38f, 0.44f);

        // ── Text ─────────────────────────────────────────────────────────────
        public static readonly Color TextPrimary  = new(0.92f, 0.93f, 0.95f);
        public static readonly Color TextSecondary= new(0.60f, 0.63f, 0.68f);
        public static readonly Color TextMuted    = new(0.38f, 0.41f, 0.46f);
        public static readonly Color TextDisabled = new(0.28f, 0.30f, 0.33f);
        public static readonly Color TextOnAccent = new(0.05f, 0.10f, 0.07f);

        // ── Accent ────────────────────────────────────────────────────────────
        public static readonly Color Accent       = new(0.20f, 0.80f, 0.40f);
        public static readonly Color AccentHover  = new(0.28f, 0.92f, 0.50f);
        public static readonly Color AccentSurface= new(0.20f, 0.80f, 0.40f, 0.12f);

        // ── Semantic ──────────────────────────────────────────────────────────
        public static readonly Color Positive     = new(0.20f, 0.75f, 0.38f);
        public static readonly Color Warning      = new(0.90f, 0.65f, 0.10f);
        public static readonly Color Danger       = new(0.85f, 0.25f, 0.22f);
        public static readonly Color DangerSurface= new(0.85f, 0.25f, 0.22f, 0.12f);
        public static readonly Color Info         = new(0.25f, 0.60f, 0.95f);
        public static readonly Color InfoSurface  = new(0.25f, 0.60f, 0.95f, 0.12f);
    }
}
