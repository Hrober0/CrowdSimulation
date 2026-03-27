using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    public static class UIStyledElements
    {
        public const int DEFAULT_NAME_WIDTH = 150;
        public const int SMALL_INPUT_WIDTH  = 50;
        public const float LINE_SPACE       = 10f;

        // ════════════════════════════════════════════════════════════════════
        // LAYOUT
        // ════════════════════════════════════════════════════════════════════

        public static VisualElement NewSpace(VisualElement root, float space = LINE_SPACE)
        {
            var e = new VisualElement();
            e.style.marginBottom = space;
            e.style.marginRight  = space;
            root.Add(e);
            return e;
        }

        public static VisualElement NewHorizontalGroup(VisualElement root)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.flexShrink    = 0;
            root.Add(group);
            return group;
        }

        public static VisualElement NewVerticalGroup(VisualElement root)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.flexShrink    = 0;
            root.Add(group);
            return group;
        }

        /// <summary>Bordered, padded container — visual grouping box.</summary>
        public static VisualElement NewContainer(VisualElement root)
        {
            var container = new VisualElement();
            container.style.flexShrink       = 0;
            container.style.SetMargin(5);
            container.style.SetPadding(5);
            container.style.SetBorderWidth(1);
            container.style.SetBorderColor(UIColors.Border);
            container.style.SetBorderRadius(4);
            container.style.backgroundColor  = UIColors.Surface;
            root.Add(container);
            return container;
        }

        /// <summary>Separator line.</summary>
        public static VisualElement NewDivider(VisualElement root)
        {
            var line = new VisualElement();
            line.style.height          = 1;
            line.style.backgroundColor = UIColors.Border;
            line.style.marginTop       = 6;
            line.style.marginBottom    = 6;
            root.Add(line);
            return line;
        }

        /// <summary>ScrollView — vertical by default.</summary>
        public static ScrollView NewScrollView(VisualElement root,
            ScrollViewMode mode = ScrollViewMode.Vertical)
        {
            var sv = new ScrollView(mode);
            sv.style.flexGrow                   = 1;
            sv.style.backgroundColor            = Color.clear;
            sv.verticalScrollerVisibility       = ScrollerVisibility.Auto;
            sv.horizontalScrollerVisibility     = ScrollerVisibility.Hidden;
            root.Add(sv);
            return sv;
        }

        // ════════════════════════════════════════════════════════════════════
        // TEXT
        // ════════════════════════════════════════════════════════════════════

        public static Label NewLabel(VisualElement root, string text)
        {
            var label = new Label { text = text };
            label.style.marginLeft = 2;
            label.style.color      = UIColors.TextPrimary;
            label.style.fontSize   = UIColors.FontSizeS;
            root.Add(label);
            return label;
        }

        /// <summary>Name + value pair in a horizontal row.</summary>
        public static (Label name, Label value) NewLabel(VisualElement root,
            string name, object content, int nameWidth = DEFAULT_NAME_WIDTH)
        {
            var group = NewHorizontalGroup(root);
            group.style.marginTop    = 2;
            group.style.marginBottom = 2;

            var nameLabel = NewLabel(group, name);
            nameLabel.style.minWidth = nameWidth;
            nameLabel.style.color    = UIColors.TextSecondary;

            var valueLabel = NewLabel(group, content?.ToString() ?? "—");
            return (nameLabel, valueLabel);
        }

        public static Label NewHeader(VisualElement root, string text)
        {
            var label = NewLabel(root, $"<b>{text}</b>");
            label.style.color        = UIColors.TextPrimary;
            label.style.fontSize     = UIColors.FontSizeM;
            label.style.marginTop    = 12;
            label.style.marginBottom = 4;
            return label;
        }

        public static Label NewSubHeader(VisualElement root, string text)
        {
            var label = NewLabel(root, text.ToUpper());
            label.style.color           = UIColors.TextMuted;
            label.style.fontSize        = UIColors.FontSizeXS;
            label.style.marginTop       = 8;
            label.style.marginBottom    = 2;
            label.style.letterSpacing   = 1;
            return label;
        }

        /// <summary>Colored inline badge — status, resource type, etc.</summary>
        public static Label NewBadge(VisualElement root, string text, Color foreground, Color background)
        {
            var badge = new Label { text = text };
            badge.style.fontSize        = UIColors.FontSizeXS;
            badge.style.color           = foreground;
            badge.style.backgroundColor = background;
            badge.style.SetPadding(2);
            badge.style.paddingLeft     = 6;
            badge.style.paddingRight    = 6;
            badge.style.SetBorderRadius(3);
            badge.style.marginLeft      = 4;
            badge.style.marginRight     = 4;
            badge.style.unityTextAlign  = TextAnchor.MiddleCenter;
            root.Add(badge);
            return badge;
        }

        // ════════════════════════════════════════════════════════════════════
        // BUTTONS
        // ════════════════════════════════════════════════════════════════════

        public static Button NewButton(VisualElement root, string text, Action onClick)
        {
            var btn = new Button { text = text };
            btn.style.color             = UIColors.TextPrimary;
            btn.style.backgroundColor   = UIColors.SurfaceRaised;
            btn.style.fontSize          = UIColors.FontSizeS;
            btn.style.SetBorderWidth(1);
            btn.style.SetBorderColor(UIColors.Border);
            btn.style.SetBorderRadius(4);
            btn.style.SetPadding(4);
            btn.style.paddingLeft       = 10;
            btn.style.paddingRight      = 10;
            btn.style.marginLeft        = 2;
            btn.style.marginRight       = 2;
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = UIColors.SurfaceHover;
                btn.style.SetBorderColor(UIColors.BorderHover);
                btn.style.color           = UIColors.TextPrimary;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = UIColors.SurfaceRaised;
                btn.style.SetBorderColor(UIColors.Border);
                btn.style.color           = UIColors.TextPrimary;
            });
            root.Add(btn);
            return btn;
        }

        /// <summary>Primary CTA button — filled accent color.</summary>
        public static Button NewButtonPrimary(VisualElement root, string text, Action onClick)
        {
            var btn = NewButton(root, text, onClick);
            btn.style.backgroundColor   = UIColors.Accent;
            btn.style.color             = UIColors.TextOnAccent;
            btn.style.SetBorderWidth(0);
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.backgroundColor = UIColors.AccentHover);
            btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.backgroundColor = UIColors.Accent);
            return btn;
        }

        /// <summary>Destructive action button — red hover.</summary>
        public static Button NewButtonDanger(VisualElement root, string text, Action onClick)
        {
            var btn = NewButton(root, text, onClick);
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = UIColors.DangerSurface;
                btn.style.SetBorderColor(UIColors.Danger);
                btn.style.color           = UIColors.Danger;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = UIColors.SurfaceRaised;
                btn.style.SetBorderColor(UIColors.Border);
                btn.style.color           = UIColors.TextPrimary;
            });
            return btn;
        }

        /// <summary>Small square icon button — expand, close, etc.</summary>
        public static Button NewButtonIcon(VisualElement root, string icon, Action onClick)
        {
            var btn = new Button { text = icon };
            btn.style.width             = 22;
            btn.style.height            = 22;
            btn.style.SetPadding(0);
            btn.style.SetBorderRadius(4);
            btn.style.SetBorderWidth(1);
            btn.style.SetBorderColor(UIColors.Border);
            btn.style.backgroundColor   = Color.clear;
            btn.style.color             = UIColors.TextSecondary;
            btn.style.fontSize          = UIColors.FontSizeS;
            btn.style.unityTextAlign    = TextAnchor.MiddleCenter;
            btn.style.marginLeft        = 2;
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = UIColors.SurfaceHover;
                btn.style.color           = UIColors.TextPrimary;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = Color.clear;
                btn.style.color           = UIColors.TextSecondary;
            });
            root.Add(btn);
            return btn;
        }

        /// <summary>±1 / ±10 stepper button for integer fields.</summary>
        public static Button NewButtonStepper(VisualElement root, string text, Action onClick)
        {
            var btn = new Button { text = text };
            btn.style.width             = 20;
            btn.style.height            = 20;
            btn.style.SetPadding(0);
            btn.style.SetBorderRadius(3);
            btn.style.SetBorderWidth(1);
            btn.style.SetBorderColor(UIColors.Border);
            btn.style.backgroundColor   = UIColors.Surface;
            btn.style.color             = UIColors.TextSecondary;
            btn.style.fontSize          = UIColors.FontSizeS;
            btn.style.unityTextAlign    = TextAnchor.MiddleCenter;
            btn.style.marginLeft        = 2;
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = UIColors.AccentSurface;
                btn.style.SetBorderColor(UIColors.Accent);
                btn.style.color           = UIColors.Accent;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = UIColors.Surface;
                btn.style.SetBorderColor(UIColors.Border);
                btn.style.color           = UIColors.TextSecondary;
            });
            root.Add(btn);
            return btn;
        }

        // ════════════════════════════════════════════════════════════════════
        // INPUTS
        // ════════════════════════════════════════════════════════════════════

        public static TextField NewTextField(VisualElement root, string label, string value,
            Action<string> onChange = null)
        {
            var field = new TextField(label) { value = value };
            ApplyFieldStyle(field);
            if (onChange != null)
                field.RegisterValueChangedCallback(e => onChange(e.newValue));
            root.Add(field);
            return field;
        }

        public static IntegerField NewIntegerField(VisualElement root, string label, int value,
            Action<int> onChange = null)
        {
            var field = new IntegerField(label) { value = value };
            ApplyFieldStyle(field);
            if (onChange != null)
                field.RegisterValueChangedCallback(e => onChange(e.newValue));
            root.Add(field);
            return field;
        }

        public static FloatField NewFloatField(VisualElement root, string label, float value,
            Action<float> onChange = null)
        {
            var field = new FloatField(label) { value = value };
            ApplyFieldStyle(field);
            if (onChange != null)
                field.RegisterValueChangedCallback(e => onChange(e.newValue));
            root.Add(field);
            return field;
        }

        /// <summary>
        /// Compact label-less toggle built from a Button to avoid Unity's Toggle USS overrides.
        /// Accent fill + "✓" when on; SurfaceRaised + empty when off.
        /// </summary>
        public static Button NewCheckbox(VisualElement root,
            bool defaultValue = false, Action<bool> onChange = null)
        {
            bool state = defaultValue;

            var btn = new Button();
            btn.style.width           = 16;
            btn.style.height          = 16;
            btn.style.SetPadding(0);
            btn.style.SetBorderRadius(3);
            btn.style.SetBorderWidth(1);
            btn.style.marginLeft      = 2;
            btn.style.marginRight     = 2;
            btn.style.fontSize        = 10;
            btn.style.unityTextAlign  = TextAnchor.MiddleCenter;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;

            void ApplyState(bool on)
            {
                btn.text                    = on ? "✓" : "";
                btn.style.backgroundColor   = on ? UIColors.Accent       : UIColors.SurfaceRaised;
                btn.style.color             = on ? UIColors.TextOnAccent  : UIColors.TextMuted;
                btn.style.SetBorderColor(       on ? UIColors.Accent       : UIColors.Border);
            }

            ApplyState(defaultValue);

            btn.RegisterCallback<ClickEvent>(_ => {
                state = !state;
                ApplyState(state);
                onChange?.Invoke(state);
            });
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                if (!state) { btn.style.backgroundColor = UIColors.SurfaceHover; btn.style.SetBorderColor(UIColors.BorderHover); }
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => ApplyState(state));

            root.Add(btn);
            return btn;
        }

        public static SliderInt NewSliderInt(VisualElement root, string label,
            int min, int max, int value, Action<int> onChange = null)
        {
            var slider = new SliderInt(label, min, max) { value = value };
            slider.style.flexGrow              = 1;
            slider.labelElement.style.color    = UIColors.TextSecondary;
            slider.labelElement.style.minWidth = 0;
            slider.labelElement.style.fontSize = UIColors.FontSizeS;
            if (onChange != null)
                slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            root.Add(slider);
            return slider;
        }

        public static EnumField NewEnumField<TEnum>(VisualElement root, string label,
            TEnum defaultValue, Action<TEnum> onChange = null) where TEnum : Enum
        {
            var field = new EnumField(label, defaultValue);
            ApplyFieldStyle(field);
            if (onChange != null)
                field.RegisterValueChangedCallback(e => onChange((TEnum)e.newValue));
            root.Add(field);
            return field;
        }

        /// <summary>
        /// Compact, label-less enum dropdown styled to match the panel theme.
        /// Looks like a small button; use instead of a raw EnumField in tight rows.
        /// Child elements (visualInput, text, arrow) are queried and styled directly
        /// so Unity's default USS is fully overridden.
        /// </summary>
        public static EnumField NewEnumPicker<TEnum>(VisualElement root,
            TEnum defaultValue, Action<TEnum> onChange = null) where TEnum : Enum
        {
            var field = new EnumField(defaultValue);

            // Hide the label portion — label is built into the hierarchy but unused here.
            field.labelElement.style.display = DisplayStyle.None;

            // Root just handles outer spacing; no background or border here.
            field.style.marginLeft  = 2;
            field.style.marginRight = 2;

            // ── visualInput — the visible clickable button ────────────────────
            // EnumField adds .unity-enum-field__input to this element.
            var input = field.Q(className: EnumField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = UIColors.SurfaceRaised;
                input.style.SetBorderWidth(1);
                input.style.SetBorderColor(UIColors.Border);
                input.style.SetBorderRadius(4);
                input.style.paddingTop    = 3;
                input.style.paddingBottom = 3;
                input.style.minHeight     = StyleKeyword.Auto;
                input.style.minWidth    = 100;
            }
            //
            // // ── TextElement — displays the selected enum name ─────────────────
            // // EnumField adds .unity-enum-field__text to this element.
            var text = field.Q<TextElement>(className: EnumField.textUssClassName);
            if (text != null)
            {
                text.style.color      = UIColors.TextSecondary;
                text.style.fontSize   = UIColors.FontSizeS;
                text.style.marginLeft = 0;
                text.style.marginRight = 0;
                text.style.flexGrow   = 1;
            }

            // ── Arrow — the dropdown chevron icon ─────────────────────────────
            // EnumField adds .unity-enum-field__arrow to this element.
            // The icon is rendered as a background-image mask, so tint with
            // unityBackgroundImageTintColor rather than color.
            var arrow = field.Q(className: EnumField.arrowUssClassName);
            if (arrow != null)
            {
                arrow.style.unityBackgroundImageTintColor = UIColors.TextMuted;
                arrow.style.marginLeft = 3;
            }

            // ── Hover ─────────────────────────────────────────────────────────
            field.RegisterCallback<MouseEnterEvent>(_ => {
                if (input != null)
                {
                    input.style.backgroundColor = UIColors.SurfaceHover;
                    input.style.SetBorderColor(UIColors.BorderHover);
                }
                // if (text  != null) text.style.color  = UIColors.TextPrimary;
                if (arrow != null) arrow.style.unityBackgroundImageTintColor = UIColors.TextSecondary;
            });
            field.RegisterCallback<MouseLeaveEvent>(_ => {
                if (input != null)
                {
                    input.style.backgroundColor = UIColors.SurfaceRaised;
                    input.style.SetBorderColor(UIColors.Border);
                }
                // if (text  != null) text.style.color  = UIColors.TextSecondary;
                if (arrow != null) arrow.style.unityBackgroundImageTintColor = UIColors.TextMuted;
            });

            // Popup opens in a separate panel overlay — style it after open.
            field.RegisterCallback<PointerDownEvent>(_ =>
                field.schedule.Execute(() => StyleDropdownPopup(field.panel?.visualTree)));

            if (onChange != null)
                field.RegisterValueChangedCallback(e => onChange((TEnum)e.newValue));

            root.Add(field);
            return field;
        }

        /// <summary>
        /// Compact, label-less string dropdown styled to match the panel theme.
        /// Update <c>choices</c> and <c>index</c> on the returned field each frame.
        /// </summary>
        public static DropdownField NewDropdownPicker(VisualElement root, Action<int> onChange = null)
        {
            var field = new DropdownField(new List<string>(), 0);
            field.labelElement.style.display = DisplayStyle.None;

            field.style.marginLeft  = 2;
            field.style.marginRight = 2;

            var input = field.Q(className: DropdownField.inputUssClassName);
            // Query text and arrow inside the input container, not the field root.
            // field.Q<TextElement>() would find the hidden labelElement first.
            var text  = input?.Q<TextElement>();
            var arrow = input?.Q(className: "unity-base-popup-field__arrow")
                     ?? input?.Q(className: "unity-dropdown-field__arrow");

            if (input != null)
            {
                input.style.backgroundColor = UIColors.SurfaceRaised;
                input.style.SetBorderWidth(1);
                input.style.SetBorderColor(UIColors.Border);
                input.style.SetBorderRadius(4);
                input.style.paddingTop    = 3;
                input.style.paddingBottom = 3;
                input.style.paddingLeft   = 6;
                input.style.paddingRight  = 6;
            }
            if (text != null)
            {
                text.style.color    = UIColors.TextSecondary;
                text.style.fontSize = UIColors.FontSizeS;
                text.style.flexGrow = 1;
            }
            if (arrow != null)
            {
                arrow.style.unityBackgroundImageTintColor = UIColors.TextMuted;
                arrow.style.marginLeft = 3;
            }

            field.RegisterCallback<MouseEnterEvent>(_ => {
                if (input != null) { input.style.backgroundColor = UIColors.SurfaceHover; input.style.SetBorderColor(UIColors.BorderHover); }
                if (arrow != null) arrow.style.unityBackgroundImageTintColor = UIColors.TextSecondary;
            });
            field.RegisterCallback<MouseLeaveEvent>(_ => {
                if (input != null) { input.style.backgroundColor = UIColors.SurfaceRaised; input.style.SetBorderColor(UIColors.Border); }
                if (arrow != null) arrow.style.unityBackgroundImageTintColor = UIColors.TextMuted;
            });

            field.RegisterCallback<PointerDownEvent>(_ =>
                field.schedule.Execute(() => StyleDropdownPopup(field.panel?.visualTree)));

            field.RegisterValueChangedCallback(_ =>
            {
                // field.value = field.choices[field.index];
                onChange?.Invoke(field.index);
            });

            root.Add(field);
            return field;
        }

        // ════════════════════════════════════════════════════════════════════
        // FILL BAR
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns (track, fill). Set fill.style.width = Length.Percent(pct) in Refresh().
        /// </summary>
        public static (VisualElement track, VisualElement fill) NewFillBar(
            VisualElement root, Color fillColor, float heightPx = 5f)
        {
            var track = new VisualElement();
            track.style.height          = heightPx;
            track.style.backgroundColor = UIColors.Border;
            track.style.SetBorderRadius(2);
            track.style.overflow        = Overflow.Hidden;
            track.style.marginTop       = 2;
            track.style.flexGrow        = 1;
            root.Add(track);

            var fill = new VisualElement();
            fill.style.height          = heightPx;
            fill.style.backgroundColor = fillColor;
            track.Add(fill);

            return (track, fill);
        }

        // ════════════════════════════════════════════════════════════════════
        // COLOR DOT
        // ════════════════════════════════════════════════════════════════════

        public static VisualElement NewColorDot(VisualElement root, Color color, float size = 8f)
        {
            var dot = new VisualElement();
            dot.style.width             = size;
            dot.style.height            = size;
            dot.style.SetBorderRadius(size / 2f);
            dot.style.backgroundColor   = color;
            dot.style.flexShrink        = 0;
            dot.style.marginRight       = 5;
            dot.style.alignSelf         = Align.Center;
            root.Add(dot);
            return dot;
        }
        
        // ════════════════════════════════════════════════════════════════════
        // INTERNAL HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the GenericDropdownMenu popup in the panel overlay and applies the dark theme.
        /// Called via schedule.Execute so it runs after the popup has been added to the tree.
        /// </summary>
        private static void StyleDropdownPopup(VisualElement panelRoot)
        {
            if (panelRoot == null) return;
            var dropdown = panelRoot.Q(className: "unity-base-dropdown");
            if (dropdown == null) return;

            // unity-base-dropdown is a transparent full-panel overlay (click-catcher) —
            // do NOT set backgroundColor on it or it floods the whole screen.
            // The visible popup box is __container-outer.
            var outer = dropdown.Q(className: "unity-base-dropdown__container-outer");
            if (outer != null)
            {
                outer.style.backgroundColor = UIColors.Surface;
                outer.style.SetBorderWidth(1);
                outer.style.SetBorderColor(UIColors.Border);
                outer.style.SetBorderRadius(4);
                outer.style.overflow      = Overflow.Hidden;
                outer.style.paddingTop    = 4;
                outer.style.paddingBottom = 4;
            }

            // The ScrollView inside the container has its own white background in Unity's USS.
            var scrollView = dropdown.Q(className: "unity-scroll-view");
            if (scrollView != null)
                scrollView.style.backgroundColor = Color.clear;

            dropdown.Query(className: "unity-base-dropdown__item").ForEach(item =>
            {
                item.style.backgroundColor = Color.clear;
                item.style.paddingTop      = 4;
                item.style.paddingBottom   = 4;
                item.style.paddingLeft     = 10;
                item.style.paddingRight    = 10;

                // Unity's USS styles the inner Label with a more-specific rule that beats
                // `color` set on the parent container — target the Label directly.
                var label = item.Q<Label>();
                if (label != null)
                {
                    label.style.color    = UIColors.TextPrimary;
                    label.style.fontSize = UIColors.FontSizeS;
                }

                var checkmark = item.Q(className: "unity-base-dropdown__checkmark");
                if (checkmark != null)
                    checkmark.style.unityBackgroundImageTintColor = UIColors.Accent;

                item.RegisterCallback<MouseEnterEvent>(_ => item.style.backgroundColor = UIColors.SurfaceHover);
                item.RegisterCallback<MouseLeaveEvent>(_ => item.style.backgroundColor = Color.clear);
            });

            dropdown.Query(className: "unity-base-dropdown__separator").ForEach(sep =>
            {
                sep.style.height          = 1;
                sep.style.backgroundColor = UIColors.BorderFaint;
                sep.style.marginTop       = 2;
                sep.style.marginBottom    = 2;
            });
        }

        private static void ApplyFieldStyle(VisualElement field)
        {
            field.style.color            = UIColors.TextPrimary;
            field.style.fontSize         = UIColors.FontSizeS;
            field.style.marginTop        = 2;
            field.style.marginBottom     = 2;

            // Label portion (for fields that have one)
            if (field is BaseField<string>  f1) StyleFieldLabel(f1.labelElement);
            else if (field is BaseField<int>   f2) StyleFieldLabel(f2.labelElement);
            else if (field is BaseField<float> f3) StyleFieldLabel(f3.labelElement);
            else if (field is BaseField<Enum>  f4) StyleFieldLabel(f4.labelElement);
        }

        private static void StyleFieldLabel(Label l)
        {
            if (l == null) return;
            l.style.color    = UIColors.TextSecondary;
            l.style.minWidth = DEFAULT_NAME_WIDTH;
            l.style.fontSize = UIColors.FontSizeS;
        }
    }
}