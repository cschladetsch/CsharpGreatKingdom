using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace GreatKingdom;

public class GreatKingdomNet : Module<Tensor, Tensor>
{
    private Module<Tensor, Tensor> layers;

    public GreatKingdomNet(string name = "model") : base(name)
    {
        layers = Sequential(
            Linear(81, 256),
            ReLU(),
            Linear(256, 256),
            ReLU(),
            Linear(256, 81)
        );
        RegisterComponents();
    }

    public override Tensor forward(Tensor input) => layers.forward(input);
}

public class DQNAgent
{
    private GreatKingdomNet _net;
    private GreatKingdomNet _targetNet;
    private optim.Optimizer _optimizer; 
    private Random _rng = new Random();
    
    private ConfigData _config;
    private float _epsilon; 
    private float _gamma; 
    private float _epsilonMin;
    private float _epsilonDecay;
    
    private int _capacity;
    private (float[] s, int a, float r, float[] ns, bool d)[] _memory;
    private int _pushIndex = 0;
    private int _count = 0;
    private int _targetUpdateCounter = 0;

    private float _avgLoss = 0;
    public float CurrentLoss => _avgLoss; 
    public int GamesPlayed { get; private set; } = 0;

    public DQNAgent(ConfigData config)
    {
        _config = config; 

        _capacity = config.AI.Memory.Capacity;
        _epsilon = config.AI.Exploration.EpsilonStart;
        _epsilonMin = config.AI.Exploration.EpsilonMin;
        _epsilonDecay = config.AI.Exploration.EpsilonDecay;
        _gamma = config.AI.Hyperparameters.Gamma;
        
        _net = new GreatKingdomNet("policy_net");
        _targetNet = new GreatKingdomNet("target_net");
        UpdateTargetNet(); 

        _optimizer = optim.Adam(_net.parameters(), (float)config.AI.Hyperparameters.LearningRate);
        _net.train();
        _memory = new (float[], int, float, float[], bool)[_capacity];
    }
    
    public void UpdateTargetNet()
    {
        _targetNet.load_state_dict(_net.state_dict());
    }

    public void SaveModel(string path)
    {
        _net.save(path);
    }

    public bool LoadModel(string path)
    {
        if (File.Exists(path))
        {
            _net.load(path);
            _targetNet.load(path);
            _epsilon = _config.AI.Exploration.EpsilonMin;
            return true;
        }
        return false;
    }

    public void SetTrainingMode(bool training)
    {
        if (training) _net.train();
        else _net.eval();
    }

    public float[] Encode(GameState state, Player p)
    {
        float[] input = new float[81];
        byte self = (byte)p;
        byte opp = (p == Player.Blue) ? (byte)Player.Orange : (byte)Player.Blue;

        for (int i = 0; i < 81; i++)
        {
            if (state.Board[i] == self) input[i] = 1.0f;
            else if (state.Board[i] == opp) input[i] = -1.0f;
            else if (state.Board[i] == (byte)Player.Neutral) input[i] = 0.1f;
            else input[i] = 0.0f;
        }
        return input;
    }

    public int GetAction(GameState state, bool isTraining)
    {
        var valid = state.GetLegalMoves();
        if (valid.Count == 0) return -1;

        if (isTraining && _rng.NextDouble() <= _epsilon)
            return valid[_rng.Next(valid.Count)];

        using (torch.no_grad())
        {
            var t = tensor(Encode(state, state.CurrentTurn)).unsqueeze(0);
            var q = _net.forward(t).data<float>().ToArray();
            
            int bestMove = valid[0];
            float bestVal = float.NegativeInfinity;
            foreach (var m in valid)
            {
                if (q[m] > bestVal) { bestVal = q[m]; bestMove = m; }
            }
            return bestMove;
        }
    }

    public void Train()
    {
        int batchSize = _config.AI.Hyperparameters.BatchSize; 
        if (_count < batchSize) return;

        var batch = new List<(float[] s, int a, float r, float[] ns, bool d)>(batchSize);
        for(int i=0; i<batchSize; i++) batch.Add(_memory[_rng.Next(_count)]);

        var states = tensor(batch.SelectMany(x => x.s).ToArray()).reshape(batchSize, 81);
        var nextStates = tensor(batch.SelectMany(x => x.ns).ToArray()).reshape(batchSize, 81);
        var actions = tensor(batch.Select(x => (long)x.a).ToArray()).view(batchSize, 1);
        var rewards = tensor(batch.Select(x => x.r).ToArray()).view(batchSize, 1);
        var dones = tensor(batch.Select(x => x.d ? 0f : 1f).ToArray()).view(batchSize, 1);

        var q = _net.forward(states).gather(1, actions);
        Tensor target;
        using (torch.no_grad())
        {
            float gamma = _config.AI.Hyperparameters.Gamma;
            var nextQ = _targetNet.forward(nextStates).max(1).values.view(batchSize, 1);
            target = rewards + (gamma * nextQ * dones);
        }

        var loss = nn.functional.smooth_l1_loss(q, target);
        
        float rawLoss = loss.item<float>();
        _avgLoss = (_avgLoss * 0.99f) + (rawLoss * 0.01f);
        
        _optimizer.zero_grad();
        loss.backward(); 
        nn.utils.clip_grad_norm_(_net.parameters(), 1.0);
        _optimizer.step();

        if (_epsilon > _config.AI.Exploration.EpsilonMin) _epsilon *= _config.AI.Exploration.EpsilonDecay;
    
        if (_targetUpdateCounter++ % _config.AI.Memory.TargetUpdateFrequency == 0)
        {
            UpdateTargetNet();
        }
    }

    public void Remember(GameState s, int a, float r, GameState ns, bool d)
    {
        _memory[_pushIndex] = (Encode(s, s.CurrentTurn), a, r, Encode(ns, ns.CurrentTurn), d);
        _pushIndex = (_pushIndex + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    public void EndEpisode() { GamesPlayed++; }
}
