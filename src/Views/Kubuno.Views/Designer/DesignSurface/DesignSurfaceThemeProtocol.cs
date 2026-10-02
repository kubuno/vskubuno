using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Kubuno.Views.Logging;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>The <c>setCanvasBackground</c> message (see <c>RustDesignSurfaceHost</c>'s theme part).</summary>
    public static class DesignSurfaceThemeProtocol
    {
        private static readonly JsonSerializerOptions Wire = new JsonSerializerOptions();

        /// <summary><c>setCanvasBackground {color}</c> with <paramref name="color"/> as <c>#RRGGBB</c>.</summary>
        public static string EncodeSetCanvasBackground(string color) => JsonSerializer.Serialize(new { type = "setCanvasBackground", color }, Wire);
    }
}
