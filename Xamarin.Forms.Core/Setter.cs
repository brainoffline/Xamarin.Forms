using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Xaml;

namespace Xamarin.Forms
{
	[ContentProperty("Value")]
	[ProvideCompiled("Xamarin.Forms.Core.XamlC.SetterValueProvider")]
	public sealed class Setter : IValueProvider
	{
		readonly ConditionalWeakTable<BindableObject, object> _originalValues = new ConditionalWeakTable<BindableObject, object>();

		public BindableObject Target { get; set; }

		public string TargetName { get; set; }

		public BindableProperty Property { get; set; }

		public object Value { get; set; }

		object IValueProvider.ProvideValue(IServiceProvider serviceProvider)
		{
			if (Property == null)
				throw new XamlParseException("Property not set", serviceProvider);
			var valueconverter = serviceProvider.GetService(typeof(IValueConverterProvider)) as IValueConverterProvider;

			Func<MemberInfo> minforetriever =
				() =>
				{
					MemberInfo minfo = null;
					try {
						if (Target != null)
							minfo = Target.GetType().GetRuntimeProperty(Property.PropertyName);
						else
							minfo = Property.DeclaringType.GetRuntimeProperty(Property.PropertyName);
					} catch (AmbiguousMatchException e) {
						throw new XamlParseException($"Multiple properties with name '{Property.DeclaringType}.{Property.PropertyName}' found.", serviceProvider, innerException: e);
					}
					if (minfo != null)
						return minfo;
					try {
						return Property.DeclaringType.GetRuntimeMethod("Get" + Property.PropertyName, new[] { typeof(BindableObject) });
					} catch (AmbiguousMatchException e) {
						throw new XamlParseException($"Multiple methods with name '{Property.DeclaringType}.Get{Property.PropertyName}' found.", serviceProvider, innerException: e);
					}
				};

			object value = valueconverter.Convert(Value, Property.ReturnType, minforetriever, serviceProvider);
			Value = value;
			return this;
		}

		internal void Apply(BindableObject bindableObject, bool fromStyle = false)
		{
			var target = FindTargetObject(bindableObject, Target, TargetName);
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (Property == null)
				return;

			object originalValue = target.GetValue(Property);
			if (!Equals(originalValue, Property.DefaultValue))
			{
				_originalValues.Remove(target);
				_originalValues.Add(target, originalValue);
			}

			var dynamicResource = Value as DynamicResource;
			if (Value is BindingBase binding)
				target.SetBinding(Property, binding.Clone(), fromStyle);
			else if (dynamicResource != null)
				target.SetDynamicResource(Property, dynamicResource.Key, fromStyle);
			else
			{
				if (Value is IList<VisualStateGroup> visualStateGroupCollection)
					target.SetValue(Property, visualStateGroupCollection.Clone(), fromStyle);
				else
					target.SetValue(Property, Value, fromStyle);
			}
		}

		internal void UnApply(BindableObject bindableObject, bool fromStyle = false)
		{
			var target = FindTargetObject(bindableObject, Target, TargetName);
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (Property == null)
				return;

			object actual = target.GetValue(Property);
			if (!Equals(actual, Value) && !(Value is Binding) && !(Value is DynamicResource))
			{
				//Do not reset default value if the value has been changed
				_originalValues.Remove(target);
				return;
			}

			if (_originalValues.TryGetValue(target, out object defaultValue))
			{
				//reset default value, unapply bindings and dynamicResource
				target.SetValue(Property, defaultValue, fromStyle);
				_originalValues.Remove(target);
			}
			else
				target.ClearValue(Property);
		}

		BindableObject FindTargetObject(BindableObject root, BindableObject target, string targetName)
		{
			root = target ?? root;
			if (root == null)
				return null;
			if (!string.IsNullOrWhiteSpace(targetName))
			{
				if (root is Element element)
				{
					if (element.FindByName(TargetName) is BindableObject locatedTarget)
						return locatedTarget;
				}

				return null;
			}

			return root;
		}
	}
}