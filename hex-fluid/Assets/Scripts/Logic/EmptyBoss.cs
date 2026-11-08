namespace HexFluid
{
    /// <summary>
    /// A boss with no abilities: the level is marked and spawned as a boss level but
    /// plays like a normal one. Use it as a placeholder while a boss is being designed,
    /// or copy it as the starting point for a new boss (see BossBehaviour).
    ///
    /// Setup: empty GameObject -> add EmptyBoss (and a model if you like) -> drag it
    /// into the Project window to make a prefab -> assign that prefab to a level's
    /// 'Boss' slot (Level Editor window or the LevelData Inspector).
    /// </summary>
    public class EmptyBoss : BossBehaviour
    {
    }
}
