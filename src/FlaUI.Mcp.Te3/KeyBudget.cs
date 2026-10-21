/// <summary>A key the object walk presses, checked against the walk's 18 s stop just before it goes out.</summary>
internal static class KeyBudget
{
    /// <summary>Presses unless the walk is out of time; then throws, naming the key, with nothing sent.</summary>
    public static Task Press(Func<bool> outOfTime, Func<Task> press, string what) =>
        outOfTime()
            ? throw new TimeoutException(
                $"The walk's time ran out before {what}, so it stopped inside Core's 30 s; no key sent."
            )
            : press();
}
