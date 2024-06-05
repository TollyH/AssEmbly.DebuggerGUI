namespace AssEmbly.DebuggerGUI
{
    public static class Extensions
    {
        public static T? MaxOrDefault<T>(this IReadOnlyList<T> enumerable, T defaultValue)
        {
            return enumerable.Count == 0 ? defaultValue : enumerable.Max();
        }
    }
}
