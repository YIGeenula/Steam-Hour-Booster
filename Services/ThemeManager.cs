using System;
using System.Windows;
using System.Windows.Media;

namespace SteamPulse.Services
{
    public static class ThemeManager
    {
        public static bool IsDarkTheme { get; private set; } = false; // Default to Light Mode as requested

        public static event Action? ThemeChanged;

        public static void ApplyTheme(bool isDark)
        {
            IsDarkTheme = isDark;
            var app = Application.Current;
            if (app == null) return;

            if (isDark)
            {
                // Dark Gaming Theme
                SetResource(app, "BrushAppBg", "#0B0D14");
                SetResource(app, "BrushSidebarBg", "#0E111B");
                SetResource(app, "BrushCardBg", "#131726");
                SetResource(app, "BrushCardBgHover", "#191F33");
                SetResource(app, "BrushGlassBorder", "#1F263D");
                SetResource(app, "BrushPanelBg", "#111524");
                SetResource(app, "BrushInputBg", "#0A0C13");
                SetResource(app, "BrushHeaderBg", "#0A0C14");
                SetResource(app, "BrushFooterBg", "#080A10");

                SetResource(app, "BrushTextTitle", "#FFFFFF");
                SetResource(app, "BrushTextPrimary", "#E1E7F5");
                SetResource(app, "BrushTextSecondary", "#7E88A6");
                SetResource(app, "BrushTextMuted", "#4A536E");

                SetResource(app, "BrushCyan", "#00E5FF");
                SetResource(app, "BrushPurple", "#7C4DFF");
            }
            else
            {
                // Clean Modern Light Theme (Clean White & Slate)
                SetResource(app, "BrushAppBg", "#F4F6FB");
                SetResource(app, "BrushSidebarBg", "#FFFFFF");
                SetResource(app, "BrushCardBg", "#FFFFFF");
                SetResource(app, "BrushCardBgHover", "#F8FAFC");
                SetResource(app, "BrushGlassBorder", "#E2E8F0");
                SetResource(app, "BrushPanelBg", "#F1F5F9");
                SetResource(app, "BrushInputBg", "#FFFFFF");
                SetResource(app, "BrushHeaderBg", "#FFFFFF");
                SetResource(app, "BrushFooterBg", "#FFFFFF");

                SetResource(app, "BrushTextTitle", "#0F172A");
                SetResource(app, "BrushTextPrimary", "#1E293B");
                SetResource(app, "BrushTextSecondary", "#475569");
                SetResource(app, "BrushTextMuted", "#94A3B8");

                SetResource(app, "BrushCyan", "#0284C7");
                SetResource(app, "BrushPurple", "#6366F1");
            }

            ThemeChanged?.Invoke();
        }

        private static void SetResource(Application app, string key, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            app.Resources[key] = new SolidColorBrush(color);
        }
    }
}
