namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The default <see cref="IDesignSurfaceHostFactory"/> installed in
    /// <see cref="DesignSurfaceHostFactoryHost"/> until DSG-7 supplies a real one. Stateless, so a
    /// single shared instance is safe even though <see cref="Create"/> is called once per open
    /// designer pane.
    /// </summary>
    public sealed class PlaceholderDesignSurfaceHostFactory : IDesignSurfaceHostFactory
    {
        public static readonly PlaceholderDesignSurfaceHostFactory Instance = new();

        private PlaceholderDesignSurfaceHostFactory()
        {
        }

        public IDesignSurfaceHost Create(DesignSurfaceDocument? document) => new PlaceholderDesignSurfaceHost();
    }
}
