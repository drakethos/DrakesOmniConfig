namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>Registers every built-in trigger, condition and action once, at plugin Awake.</summary>
internal static class NodeLibrary
{
    private static bool _done;

    public static void RegisterAll()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        ItemTriggers.Register();
        WorldTriggers.Register();
        Conditions.Register();
        Actions.Register();
        AreaActions.Register();
        FieldActions.Register();
        UiActions.Register();
    }
}
