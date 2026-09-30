using System;
using System.ComponentModel;
using System.Linq;
using Kubuno.Rust.Logic.SolutionExplorer;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// What the Properties window (F4 / Alt+Enter) shows for a dependency node - read-only rows with a
    /// description for the bottom pane, the component name and class name at the top ("serde
    /// Propriétés de référence de crate"), like a .NET package reference. Solution Explorer asks the
    /// node for it through <see cref="Microsoft.Internal.VisualStudio.PlatformUI.IBrowsablePattern"/>.
    /// </summary>
    internal sealed class DependencyBrowseObject : ICustomTypeDescriptor
    {
        private readonly DependencyItem _item;
        private readonly PropertyDescriptorCollection _properties;

        public DependencyBrowseObject(DependencyItem item)
        {
            _item = item;
            var category = DependenciesText.Misc;
            _properties = new PropertyDescriptorCollection(
                DependencyProperties.For(item).Select(row => (PropertyDescriptor)new RowDescriptor(row, category)).ToArray(),
                readOnly: true);
        }

        public AttributeCollection GetAttributes() => AttributeCollection.Empty;

        public string GetClassName() => DependencyProperties.ClassName(_item);

        public string GetComponentName() => _item.DisplayName;

        public TypeConverter GetConverter() => new TypeConverter();

        public EventDescriptor? GetDefaultEvent() => null;

        public PropertyDescriptor? GetDefaultProperty() => _properties.Count > 0 ? _properties[0] : null;

        public object? GetEditor(Type editorBaseType) => null;

        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

        public PropertyDescriptorCollection GetProperties() => _properties;

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes) => _properties;

        public object GetPropertyOwner(PropertyDescriptor pd) => this;

        private sealed class RowDescriptor : PropertyDescriptor
        {
            private readonly DependencyPropertyRow _row;
            private readonly string _category;

            public RowDescriptor(DependencyPropertyRow row, string category)
                : base(row.Key, new Attribute[] { ReadOnlyAttribute.Yes })
            {
                _row = row;
                _category = category;
            }

            public override string DisplayName => _row.DisplayName;

            public override string Description => _row.Description;

            public override string Category => _category;

            public override Type ComponentType => typeof(DependencyBrowseObject);

            public override bool IsReadOnly => true;

            public override Type PropertyType => typeof(string);

            public override bool CanResetValue(object component) => false;

            public override object GetValue(object component) => _row.Value;

            public override void ResetValue(object component)
            {
            }

            public override void SetValue(object component, object value)
            {
            }

            public override bool ShouldSerializeValue(object component) => false;
        }
    }
}
