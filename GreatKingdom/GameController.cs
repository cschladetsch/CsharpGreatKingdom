using System;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Raylib_cs;

namespace GreatKingdom;

public class GameController
{
    // --- Config & Layout Constants ---
    private readonly ConfigData _config;
    private const int CellSize = 80;
    private const int GridSize = 9;
    private const int Padding = 40;
    private const int ScreenWidth = (GridSize * CellSize) + (Padding * 2);
    private const int ScreenHeight = ScreenWidth + 80;
    private const int DisplayLimit = 12;

    // --- Engines & Managers ---
    private readonly Renderer _renderer;
    private readonly MCTS _mcts;
    private DQNAgent _neuralNet; // Not readonly (set in async task)
    private NetworkManager _net; // Not readonly
    private Brain _brainManager; // Not readonly

    // --- Core State ---
    public AppScreen CurrentScreen { get; private set; } = AppScreen.Loading;
    public GameMode CurrentMode { get; private set; } = GameMode.Hotseat;
    public GameState GameState { get; private set; } 
    
    // --- Selection/Threading State ---
    private bool _isBrainReady = false;
    private bool _isTrainingActive = false;
    private Task? _trainingTask = null;
    private int? _pendingAiMove = null;
    private int? _pendingPlayerMove = null;
    private bool _pendingPass = false;

    // --- UI/Data State ---
    public string LoadStatus { get; private set; } = "Initializing...";
    public bool IsAiThinking { get; private set; } = false;
    public string StatusMessage { get; private set; } = "";
    public string PublicIp { get; private set; } = "Fetching...";
    public string TargetIp { get; private set; } = "";
    
    // --- Training UI State ---
    public string GymMessage { get; private set; } = "";
    public float GymMessageTimer { get; private set; } = 0;
    public string[] AvailableBrains { get; private set; } = Array.Empty<string>();
    public int SelectedBrainIndex { get; private set; } = 0;
    public int ScrollOffset { get; private set; } = 0;

    // --- Private Flags ---
    private float _time = 0.0f;
    private bool _fetchedIp = false;
    private bool _hasAutoSaved = false;
    private const float AutoSaveThreshold = 0.005f;


    // --- CONSTRUCTOR ---
    public GameController(ConfigData config, Renderer renderer, MCTS mcts, DQNAgent neuralNet, NetworkManager net, Brain brainManager)
    {
        _config = config;
        _renderer = renderer;
        _mcts = mcts;
        
        // Assign non-readonly fields from arguments
        _neuralNet = neuralNet; 
        _net = net;
        _brainManager = brainManager;
        
        GameState = new GameState();
        TargetIp = config.Game.DefaultIP;

        Task.Run(AsyncLoadWorker);
    }
    
    // ------------------------------------------
    // ASYNC WORKERS (These methods are defined inside the class)
    // ------------------------------------------
    private void AsyncLoadWorker()
    {
        try
        {
            LoadStatus = "Loading saved brain...";
            _brainManager.LoadBrain(Brain.LatestFileAlias);
            _isBrainReady = true;
            LoadStatus = "Ready!";
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Critical Init Error: {ex.Message}");
             LoadStatus = "FATAL: Brain Load Failed.";
             _isBrainReady = true;
        }
    }

    private void TrainingWorker()
    {
        _neuralNet.SetTrainingMode(true);
        while (_isTrainingActive)
        {
            TrainSingleGame();
        }
    }

    private void TrainSingleGame()
    {
        GameState tState = new GameState();
        int moves = 0;

        var blueExperiences = new List<(GameState state, int action, GameState nextState, bool done, float shapedReward)>();

        while (tState.Winner == Player.None && moves < 80)
        {
            if (!_isTrainingActive) return;

            int action;
            Player current = tState.CurrentTurn;
            GameState oldState = tState.DeepCopy();

            if (current == Player.Blue) action = _neuralNet.GetAction(tState, isTraining: true);
            else action = _mcts.GetBestMove(tState, _config.Game.MCTSIterations);

            tState.ApplyMove(action);

            if (current == Player.Blue) {
                float shapedReward = CalculateShapedReward(oldState, tState);
                blueExperiences.Add((oldState, action, tState, tState.Winner != Player.None, shapedReward));
            }
            moves++;
        }

        // Assign final rewards after game ends
        float finalReward = (tState.Winner == Player.Blue) ? 1.0f : (tState.Winner == Player.Orange ? -1.0f : 0.0f);

        // Store all experiences with proper rewards (final + shaped)
        foreach (var exp in blueExperiences)
        {
            float totalReward = exp.shapedReward;
            if (exp.done) totalReward += finalReward;
            _neuralNet.Remember(exp.state, exp.action, totalReward, exp.nextState, exp.done);
        }

        // Train after collecting all experiences from the game
        for (int i = 0; i < 5; i++)
        {
            _neuralNet.Train();
        }

        _neuralNet.EndEpisode();
    }

    private float CalculateShapedReward(GameState before, GameState after)
    {
        float reward = 0.0f;

        // Count stones for each player
        int blueBefore = 0, orangeBefore = 0;
        int blueAfter = 0, orangeAfter = 0;

        for (int i = 0; i < 81; i++)
        {
            if (before.Board[i] == (byte)Player.Blue) blueBefore++;
            if (before.Board[i] == (byte)Player.Orange) orangeBefore++;
            if (after.Board[i] == (byte)Player.Blue) blueAfter++;
            if (after.Board[i] == (byte)Player.Orange) orangeAfter++;
        }

        // Reward for capturing opponent stones
        int opponentCaptured = orangeBefore - orangeAfter;
        if (opponentCaptured > 0) reward += 0.3f * opponentCaptured;

        // Penalty for losing own stones (getting captured)
        int selfCaptured = blueBefore - blueAfter;
        if (selfCaptured > 0) reward -= 0.5f * selfCaptured;

        return reward;
    }
    
    // --- MAIN UPDATE LOOP (Called every frame from Program.cs) ---
    public void Update(float dt)
    {
        _time += dt;
        if (GymMessageTimer > 0) GymMessageTimer -= dt;

        switch (CurrentScreen)
        {
            case AppScreen.Loading:     if (_isBrainReady && _time > 1.0f) CurrentScreen = AppScreen.Menu; break;
            case AppScreen.Menu:        UpdateMenu(); break;
            case AppScreen.NetLobby:    UpdateLobby(); break;
            case AppScreen.Game:        UpdateGame(); break;
            case AppScreen.Training:    UpdateTraining(); break;
            case AppScreen.BrainSelect: UpdateBrainSelect(); break;
        }
    }
    
    // --- DRAW CALL (Called every frame from Program.cs) ---
    public void Draw(int fps)
    {
        switch (CurrentScreen)
        {
            case AppScreen.Loading:     DrawLoading(); break; 
            case AppScreen.Menu:        _renderer.DrawMenu(_isBrainReady, _time, fps); break;
            case AppScreen.NetLobby:    _renderer.DrawLobby(PublicIp, StatusMessage, _time, fps); break;
            case AppScreen.Game:        _renderer.DrawGame(GameState, IsAiThinking, StatusMessage, _time, fps); break;
            case AppScreen.Training:    _renderer.DrawTraining(_neuralNet?.GamesPlayed ?? 0, _neuralNet?.CurrentLoss ?? 0, _time, GymMessage, GymMessageTimer, fps); break;
            case AppScreen.BrainSelect: _renderer.DrawBrainSelect(AvailableBrains, SelectedBrainIndex, ScrollOffset); break;
        }
    }

    // --- DRAWING UTILITY (Internal to Controller) ---
    private void DrawLoading()
    {
        Raylib.ClearBackground(new Color(30, 30, 35, 255));
        int cx = ScreenWidth / 2, cy = ScreenHeight / 2;
        Raylib.DrawPolyLines(new Vector2(cx, cy - 50), 6, 40, _time * 5.0f, Color.Blue);
        Raylib.DrawText("GREAT KINGDOM", cx - 100, cy + 20, 24, Color.White);
        Raylib.DrawText(LoadStatus, cx - 100, cy + 60, 20, Color.Gray);
    }
    
    // --- INPUT HELPER ---
    private bool BtnClick(float y) {
        float w = 300, h = 60, x = ScreenWidth/2 - w/2;
        Rectangle rec = new Rectangle(x,y,w,h);
        if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rec)) {
            if (Raylib.IsMouseButtonReleased(MouseButton.Left)) return true;
        }
        return false;
    }
    
    // ==========================================
    //           UPDATE METHODS (Logic)
    // ==========================================

    public void UpdateMenu()
    {
        if(BtnClick(200)) StartGame(GameMode.Hotseat);
        if(BtnClick(280)) StartGame(GameMode.VsAI);

        if (_isBrainReady)
        {
            if(BtnClick(360)) StartGame(GameMode.VsNeuralNet);
            if(BtnClick(440)) CurrentScreen = AppScreen.Training;
            if(BtnClick(600)) {
                AvailableBrains = _brainManager.ListAvailableBrains();
                SelectedBrainIndex = 0;
                ScrollOffset = 0;
                CurrentScreen = AppScreen.BrainSelect;
            }
        }

        if(BtnClick(520)) CurrentScreen = AppScreen.NetLobby;
    }

    public void UpdateLobby()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) CurrentScreen = AppScreen.Menu;
        
        if (!_fetchedIp) {
            _fetchedIp = true;
            Task.Run(async () => {
                try { using var c = new HttpClient(); PublicIp = await c.GetStringAsync("https://api.ipify.org"); }
                catch { PublicIp = "Unknown"; }
            });
        }
        if (BtnClick(200)) {
            StatusMessage = "Starting Server...";
            Task.Run(async () => {
                if(await _net.HostGame()) StartGame(GameMode.Network);
                else StatusMessage = "Error: Port 7777 busy!";
            });
        }
        if (BtnClick(300)) {
            StatusMessage = "Connecting...";
            Task.Run(async () => {
                if(await _net.JoinGame(TargetIp)) StartGame(GameMode.Network);
                else StatusMessage = "Connection Failed.";
            });
        }
    }

    public void UpdateBrainSelect()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            CurrentScreen = AppScreen.Training;
            return;
        }

        if (AvailableBrains.Length > 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S))
            {
                SelectedBrainIndex = Math.Min(SelectedBrainIndex + 1, AvailableBrains.Length - 1);
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W))
            {
                SelectedBrainIndex = Math.Max(SelectedBrainIndex - 1, 0);
            }

            if (SelectedBrainIndex < ScrollOffset) ScrollOffset = SelectedBrainIndex;
            if (SelectedBrainIndex >= ScrollOffset + DisplayLimit) ScrollOffset = SelectedBrainIndex - DisplayLimit + 1;
        }


        if (Raylib.IsKeyPressed(KeyboardKey.Enter) && AvailableBrains.Length > 0)
        {
            string selectedFile = AvailableBrains[SelectedBrainIndex];

            if (_brainManager.LoadBrain(selectedFile))
                GymMessage = $"Loaded {selectedFile}!";
            else
                GymMessage = "Load failed.";

            GymMessageTimer = 3.0f;
            CurrentScreen = AppScreen.Training;
        }
    }

    public void UpdateTraining()
    {
        if (!_isTrainingActive && _isBrainReady && _trainingTask == null)
        {
            _isTrainingActive = true;
            _trainingTask = Task.Run(TrainingWorker);
            _hasAutoSaved = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || BtnClick(ScreenHeight - 80)) 
        {
            _isTrainingActive = false;
            _trainingTask = null;
            CurrentScreen = AppScreen.Menu;
        }
        
        // Auto-Save Check
        if (_isTrainingActive && !_hasAutoSaved && (_neuralNet?.CurrentLoss ?? 1.0f) < AutoSaveThreshold)
        {
            if (_neuralNet != null)
            {
                _brainManager.SaveCurrentBrain(_neuralNet);
                GymMessage = $"AUTO-SAVED! Loss hit {AutoSaveThreshold} at {_neuralNet.GamesPlayed} games.";
                GymMessageTimer = 5.0f;
                _hasAutoSaved = true;
            }
        }

        // Manual Save Button
        if (BtnClick(ScreenHeight - 160)) {
            if (_neuralNet != null)
            {
                _brainManager.SaveCurrentBrain(_neuralNet);
                GymMessage = "Brain Saved!";
                GymMessageTimer = 3.0f;
            }
        }

        // Load Specific Button (Switches Screen)
        if (BtnClick(ScreenHeight - 240)) {
            AvailableBrains = _brainManager.ListAvailableBrains(); 
            SelectedBrainIndex = 0;
            ScrollOffset = 0;
            CurrentScreen = AppScreen.BrainSelect;
        }
    }

    private void StartGame(GameMode mode)
    {
        CurrentMode = mode;
        GameState = new GameState();
        CurrentScreen = AppScreen.Game;
        IsAiThinking = false;
        StatusMessage = mode == GameMode.Network 
            ? (_net.IsHost ? "You are BLUE" : "You are ORANGE") 
            : "Blue Start!";
    }

    public void UpdateGame()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.R)) {
            if (GameState.Winner != Player.None || CurrentMode != GameMode.Network) { CurrentScreen = AppScreen.Menu; return; }
        }
        if (GameState.Winner != Player.None) return;

        // Capture input at the start of the frame (before any early returns)
        bool isMyTurnForInput = true;
        if ((CurrentMode == GameMode.VsAI || CurrentMode == GameMode.VsNeuralNet) && GameState.CurrentTurn == Player.Orange) isMyTurnForInput = false;
        if (CurrentMode == GameMode.Network) {
            if (_net.IsHost && GameState.CurrentTurn != Player.Blue) isMyTurnForInput = false;
            if (!_net.IsHost && GameState.CurrentTurn != Player.Orange) isMyTurnForInput = false;
        }

        if (isMyTurnForInput && !IsAiThinking && GameState.Winner == Player.None)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Space)) {
                _pendingPass = true;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
                Vector2 m = Raylib.GetMousePosition();
                int gx = ((int)m.X - Padding) / CellSize;
                int gy = ((int)m.Y - Padding) / CellSize;
                if (gx >= 0 && gx < 9 && gy >= 0 && gy < 9) {
                    int idx = GameState.Idx(gx, gy);
                    if (GameState.Board[idx] == (byte)Player.None) {
                        _pendingPlayerMove = idx;
                    }
                }
            }
        }

        if (CurrentMode == GameMode.Network)
        {
            while (_net.IncomingMoves.TryDequeue(out int m))
            {
                var tempState = GameState;
                tempState.ApplyMove(m);
                GameState = tempState;
            }
        }

        // Check if we have a pending AI move to apply on main thread
        if (_pendingAiMove.HasValue)
        {
            var tempState = GameState;
            tempState.ApplyMove(_pendingAiMove.Value);
            GameState = tempState;
            _pendingAiMove = null;
            IsAiThinking = false;
            return;
        }

        if (GameState.CurrentTurn == Player.Orange && !IsAiThinking)
        {
            if (CurrentMode == GameMode.VsAI) {
                IsAiThinking = true;
                StatusMessage = "MCTS Thinking...";
                var stateCopy = GameState.DeepCopy();
                Task.Run(() => {
                    int move = _mcts.GetBestMove(stateCopy, _config.Game.MCTSIterations);
                    _pendingAiMove = move;
                });
            }
            else if (CurrentMode == GameMode.VsNeuralNet && _isBrainReady) {
                IsAiThinking = true;
                StatusMessage = "Neural Net Thinking...";
                var stateCopy = GameState.DeepCopy();
                Task.Run(() => {
                    _neuralNet.SetTrainingMode(false);
                    int move = _neuralNet.GetAction(stateCopy, isTraining: false);
                    _pendingAiMove = move;
                });
            }
            if (CurrentMode == GameMode.VsAI || CurrentMode == GameMode.VsNeuralNet) return;
        }

        // Process queued player moves
        if (_pendingPass)
        {
            var tempState = GameState;
            tempState.ApplyMove(-1);
            GameState = tempState;
            if (CurrentMode == GameMode.Network) _net.SendMove(-1);
            _pendingPass = false;
        }
        else if (_pendingPlayerMove.HasValue)
        {
            var tempState = GameState;
            tempState.ApplyMove(_pendingPlayerMove.Value);
            GameState = tempState;
            if (CurrentMode == GameMode.Network) _net.SendMove(_pendingPlayerMove.Value);
            _pendingPlayerMove = null;
        }
    }
}
