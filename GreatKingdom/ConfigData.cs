namespace GreatKingdom;

public class ConfigData
{
    public GameConfig Game { get; set; } = new GameConfig();
    public AiConfig AI { get; set; } = new AiConfig();
}

public class GameConfig
{
    public int GridSize { get; set; } = 9;
    public int MCTSIterations { get; set; } = 5;
    public string DefaultIP { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7777;
}

public class AiConfig
{
    public AiHyperparameters Hyperparameters { get; set; } = new AiHyperparameters();
    public AiExploration Exploration { get; set; } = new AiExploration();
    public AiMemory Memory { get; set; } = new AiMemory();
}

public class AiHyperparameters
{
    public double LearningRate { get; set; } = 0.000005; 
    public float Gamma { get; set; } = 0.85f;
    public int BatchSize { get; set; } = 128;
}

public class AiExploration
{
    public float EpsilonStart { get; set; } = 1.0f;
    public float EpsilonMin { get; set; } = 0.05f;
    public float EpsilonDecay { get; set; } = 0.9995f;
}

public class AiMemory
{
    public int Capacity { get; set; } = 5000;
    public int TargetUpdateFrequency { get; set; } = 500;
}
