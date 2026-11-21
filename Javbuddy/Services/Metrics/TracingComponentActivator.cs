using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Javbuddy.Services.Metrics;
using Microsoft.AspNetCore.Components;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(TracingComponentActivator))]

namespace Javbuddy.Services.Metrics;

/// <summary>Creates components the way Blazor's default activator does (constructor injection included), and
/// when the component is a page, names the hub span it's created under after the page's route (see
/// <see cref="BlazorTraceProcessor.NameRender"/>). Pages here pick their own interactive render mode, so the
/// page itself is the circuit's root component and its creation is the one place that knows which page an
/// interactive render is for. Only registered while tracing is on.</summary>
public sealed class TracingComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, (ObjectFactory Factory, string? Route)> Cache = new();

    public IComponent CreateInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type componentType)
    {
        var (factory, route) = Cache.GetOrAdd(componentType, static type =>
        {
            if (!typeof(IComponent).IsAssignableFrom(type))
                throw new ArgumentException($"The type {type.FullName} does not implement {nameof(IComponent)}.", nameof(componentType));
            return (ActivatorUtilities.CreateFactory(type, Type.EmptyTypes), RouteOf(type));
        });

        if (route is not null) BlazorTraceProcessor.NameRender(Activity.Current, route);
        return (IComponent)factory(services, null);
    }

    /// <summary>The page's first route template, or null for a component that isn't a page. A page can have
    /// several (@page twice), and the first is as good a name as any for the trace.</summary>
    public static string? RouteOf(Type componentType) =>
        componentType.GetCustomAttributes<RouteAttribute>(inherit: false).FirstOrDefault()?.Template;

    /// <summary>Called by hot reload: a changed component's constructor or routes may no longer match the cache.</summary>
    public static void ClearCache(Type[]? updatedTypes) => Cache.Clear();
}
