# Type Design for Performance

Sealing, static pure functions, deferred enumeration, and frozen collections — the parts of type-design-for-performance not already covered by [performance-and-api-design.md](performance-and-api-design.md)'s Span/Memory/API-design content or the main [SKILL.md](SKILL.md)'s `readonly record struct`/`ValueTask` guidance.

## Seal Classes by Default

Sealing classes enables JIT devirtualization and communicates API intent.

```csharp
// DO: Seal classes not designed for inheritance
public sealed class OrderProcessor
{
    public void Process(Order order) { }
}

// DO: Seal records (they're classes)
public sealed record OrderCreated(OrderId Id, CustomerId CustomerId);

// DON'T: Leave unsealed without reason
public class OrderProcessor  // Can be subclassed - intentional?
{
    public virtual void Process(Order order) { }  // Virtual = slower
}
```

**Benefits:** JIT can devirtualize method calls; communicates "this is not an extension point"; prevents accidental breaking changes.

## Prefer Static Pure Functions

Static methods with no side effects are faster and more testable — no vtable lookup, no hidden state, thread-safe by design, and they force explicit dependencies.

```csharp
// DO: Static pure function
public static class OrderCalculator
{
    public static Money CalculateTotal(IReadOnlyList<OrderItem> items)
    {
        var total = items.Sum(i => i.Price * i.Quantity);
        return new Money(total, "USD");
    }
}

// DON'T: Instance method hiding dependencies
public class OrderCalculator
{
    private readonly ITaxService _taxService;        // Hidden dependency
    private readonly IDiscountService _discountService; // Hidden dependency

    public Money CalculateTotal(IReadOnlyList<OrderItem> items) { /* ... */ }
}
```

**Don't go overboard** — use instance methods when you genuinely need state or polymorphism.

## Defer Enumeration

Don't materialize enumerables until necessary. Avoid excessive LINQ chains that re-enumerate.

```csharp
// BAD: Premature materialization
public IReadOnlyList<Order> GetActiveOrders()
{
    return _orders
        .Where(o => o.IsActive)
        .ToList()              // Materialized!
        .OrderBy(o => o.CreatedAt)
        .ToList();              // Materialized again!
}

// GOOD: Defer until the end
public IReadOnlyList<Order> GetActiveOrders()
{
    return _orders
        .Where(o => o.IsActive)
        .OrderBy(o => o.CreatedAt)
        .ToList();  // Single materialization
}
```

Watch for the async version of this mistake — `Select(async ...)` over an `IEnumerable` creates a `Task` per item without awaiting them in order; use `IAsyncEnumerable<T>` for streaming or `Task.WhenAll` for explicit parallel batches instead.

## Frozen Collections for Static Data

Use `FrozenDictionary`/`FrozenSet` (.NET 8+) for lookup data that's built once and read many times — faster lookups than a regular `Dictionary` in exchange for a slower one-time build.

```csharp
private static readonly FrozenDictionary<string, Handler> _handlers =
    new Dictionary<string, Handler>
    {
        ["create"] = new CreateHandler(),
        ["update"] = new UpdateHandler(),
    }.ToFrozenDictionary();
```

## Quick Reference

| Pattern | Benefit |
|---------|---------|
| `sealed class` | Devirtualization, clear API |
| Static pure functions | No vtable, testable, thread-safe |
| Defer `.ToList()` | Single materialization |
| `FrozenDictionary`/`FrozenSet` | Fastest lookup for static data |

## Anti-Patterns

```csharp
// DON'T: Unsealed class without reason
public class OrderService { }  // Seal it!

// DON'T: Instance method that could be static
public int Add(int a, int b) => a + b;  // Make static

// DON'T: Multiple ToList() calls
items.Where(...).ToList().OrderBy(...).ToList();  // One ToList at end
```

## Resources

- **Performance Best Practices**: https://learn.microsoft.com/en-us/dotnet/standard/performance/
- **Frozen Collections**: https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen
