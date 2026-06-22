using Game.SceneFlow;

namespace RealisticPathFinding.Systems
{
    internal static class SimulationGuard
    {
        public static bool IsGameLoading()
        {
            var gameManager = GameManager.instance;
            return gameManager != null && gameManager.isGameLoading;
        }
    }
}
