using System;

namespace Scrapelist.Extensions;

public static class ListExtensions
{
    public static List<T> RemoveNulls<T>(this List<T?> list) where T : class
    {
        return [.. list.Where(x => x != null).Select(x => x!)];
    }
}
