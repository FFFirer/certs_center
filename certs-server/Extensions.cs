using System;
using System.Diagnostics.CodeAnalysis;

namespace CertsServer;

public static class Extensions
{
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? source)
    {
        return source is null || !source.Any();
    }

}
