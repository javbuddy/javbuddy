using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Javbuddy.Services.Metrics;

/// <summary>Creates components the way Blazor's default activator does (constructor injection included), and
/// when the component is a page, names the hub span it's created under after the page's route (see
/// <see cref="BlazorTraceProcessor.NameRender"/>). Pages here pick their own interactive render mode, so the
/// page itself is the circuit's root component and its creation is the one place that knows which page an
/// interactive render is for. Only registered while tracing is on.</summary>
public sealed class TracingComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, (ObjectFactory Factory, string? Route)> Cache = new();

    public IComponent CreateInstance([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(Component)] Type componentType)
    {
        var (factory, route) = Cache.GetOrAdd(componentType, static type =>
        {
            if (!typeof(IComponent).IsAssignableFrom(type))
                throw new ArgumentException($"The type {type.FullName} does not implement {nameof(IComponent)}.", nameof(componentType));
            return (ActivatorUtilities.CreateFactory(type, Type.EmptyTypes), type.GetCustomAttribute<RouteAttribute>()?.Template);
        });

        if (route is not null) BlazorTraceProcessor.NameRender(Activity.Current, route);
        return (IComponent)factory(services, null);
    }

    private const System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes Component =
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors;
}
