using System.Windows.Media;

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>
    /// What the icon editors need from the views language server (implemented by the designer pane, like
    /// <see cref="PropertyBrowser.IKbviewDesignServices"/>): the Kubuno icon set (<c>kubuno/icons</c>) and an icon value
    /// rendered exactly as the application draws it (<c>kubuno/renderIcon</c>: SVG, every raster format, the options).
    /// </summary>
    public interface IKbviewIconServices
    {
        /// <summary>The icon set; <see cref="IconCatalog.Empty"/> while the server cannot tell. Cached once read.</summary>
        IconCatalog GetIconCatalog();

        /// <summary><paramref name="value"/> (relative to the view) rendered at <paramref name="size"/> pixels, glyphs and <c>currentColor</c> in <paramref name="color"/>; null when it draws nothing.</summary>
        ImageSource? RenderIcon(string value, int size, Color color);
    }
}
